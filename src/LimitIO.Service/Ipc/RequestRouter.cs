using System.Text.Json;
using LimitIO.Core.Ipc;
using LimitIO.Core.Models;
using LimitIO.Core.Security;
using LimitIO.Core.Storage;
using LimitIO.Core.Time;
using LimitIO.Service.Configuration;
using LimitIO.Service.Enforcement;
using Microsoft.Extensions.Logging;

namespace LimitIO.Service.Ipc;

/// <summary>
/// Dispatches a parsed <see cref="IpcRequest"/> to the right handler. This is where the password gate is
/// actually enforced (see docs/ARCHITECTURE.md "who owns the truth") - every mutating request type is
/// checked against <see cref="AuthSessionManager"/> here, server-side, regardless of what the UI thinks
/// its own state is.
/// </summary>
public sealed class RequestRouter
{
    private static readonly IReadOnlySet<IpcRequestType> RequiresAuth = new HashSet<IpcRequestType>
    {
        IpcRequestType.ChangePassword,
        IpcRequestType.UpdateRules,
        IpcRequestType.PauseMonitoring,
        IpcRequestType.ResumeMonitoring,
    };

    private readonly ConfigCache _configCache;
    private readonly UsageStore _usageStore;
    private readonly GraceManager _graceManager;
    private readonly AuthSessionManager _authSessionManager;
    private readonly IClock _clock;
    private readonly ILogger<RequestRouter> _logger;

    public RequestRouter(
        ConfigCache configCache,
        UsageStore usageStore,
        GraceManager graceManager,
        AuthSessionManager authSessionManager,
        IClock clock,
        ILogger<RequestRouter> logger)
    {
        _configCache = configCache;
        _usageStore = usageStore;
        _graceManager = graceManager;
        _authSessionManager = authSessionManager;
        _clock = clock;
        _logger = logger;
    }

    public IpcResponse Handle(IpcRequest request)
    {
        if (request.ProtocolVersion != ProtocolVersion.Current)
        {
            return IpcResponse.Failure(request.RequestId,
                $"Protocol version mismatch (client={request.ProtocolVersion}, service={ProtocolVersion.Current}). Reinstall or update LimitIO.");
        }

        if (RequiresAuth.Contains(request.Type) && !_authSessionManager.IsValid(request.SessionToken))
        {
            return IpcResponse.Failure(request.RequestId, "Authentication required.");
        }

        try
        {
            return request.Type switch
            {
                IpcRequestType.GetStatus => HandleGetStatus(request),
                IpcRequestType.VerifyPassword => HandleVerifyPassword(request),
                IpcRequestType.SetInitialPassword => HandleSetInitialPassword(request),
                IpcRequestType.ChangePassword => HandleChangePassword(request),
                IpcRequestType.RequestGrace => HandleRequestGrace(request),
                IpcRequestType.UpdateRules => HandleUpdateRules(request),
                IpcRequestType.PauseMonitoring => HandlePauseMonitoring(request),
                IpcRequestType.ResumeMonitoring => HandleResumeMonitoring(request),
                IpcRequestType.Heartbeat => IpcResponse.Success(request.RequestId),
                _ => IpcResponse.Failure(request.RequestId, "Unknown request type."),
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error processing IPC request {Type}", request.Type);
            return IpcResponse.Failure(request.RequestId, "Internal error handling request.");
        }
    }

    private IpcResponse HandleGetStatus(IpcRequest request)
    {
        var config = _configCache.Current;
        var localNow = _clock.LocalNow;

        var rules = new List<RuleStatusDto>(config.Rules.Count);
        foreach (var rule in config.Rules)
        {
            var periodDate = ResetScheduling.CurrentPeriodDate(localNow, rule.ResetTime);
            var record = _usageStore.GetOrCreate(rule.Id, periodDate);
            var graceActive = _graceManager.IsGraceActive(rule.Id);
            var overBudget = record.Consumed >= rule.DailyBudget;
            var blocked = rule.Enabled && overBudget && !graceActive;

            rules.Add(new RuleStatusDto(
                rule.Id,
                rule.TargetType.ToString(),
                rule.Target,
                (int)rule.DailyBudget.TotalMinutes,
                (int)record.Consumed.TotalMinutes,
                rule.Enabled,
                blocked,
                GraceAvailable: !record.GraceUsedToday));
        }

        var monitoringPaused = config.MonitoringPausedUntil is { } until && until > _clock.UtcNow;
        var statusDto = new StatusResponseDto(config.PasswordConfigured, monitoringPaused, config.MonitoringPausedUntil, rules);

        return IpcResponse.Success(request.RequestId, Serialize(statusDto));
    }

    private IpcResponse HandleVerifyPassword(IpcRequest request)
    {
        var payload = Deserialize<VerifyPasswordRequestDto>(request.PayloadJson);
        if (payload is null)
        {
            return IpcResponse.Failure(request.RequestId, "Malformed request.");
        }

        var backoff = _authSessionManager.GetRequiredBackoff();
        if (backoff is { } wait)
        {
            return IpcResponse.Success(request.RequestId,
                Serialize(new VerifyPasswordResponseDto(false, null, (int)wait.TotalMilliseconds)));
        }

        var config = _configCache.Current;
        if (!config.PasswordConfigured)
        {
            return IpcResponse.Success(request.RequestId, Serialize(new VerifyPasswordResponseDto(false, null, null)));
        }

        var ok = PasswordHasher.Verify(payload.Password, config.PasswordHash!, config.PasswordSalt!, config.PasswordIterations);
        if (!ok)
        {
            _authSessionManager.RecordFailure();
            return IpcResponse.Success(request.RequestId, Serialize(new VerifyPasswordResponseDto(false, null, null)));
        }

        var token = _authSessionManager.IssueSession();
        return IpcResponse.Success(request.RequestId, Serialize(new VerifyPasswordResponseDto(true, token, null)));
    }

    private IpcResponse HandleSetInitialPassword(IpcRequest request)
    {
        var payload = Deserialize<SetInitialPasswordRequestDto>(request.PayloadJson);
        if (payload is null || string.IsNullOrEmpty(payload.Password))
        {
            return IpcResponse.Failure(request.RequestId, "Malformed request.");
        }

        if (_configCache.Current.PasswordConfigured)
        {
            // First-run only: never let this overwrite an existing password without ChangePassword's
            // old-password check, no matter what the calling UI's own state believes.
            return IpcResponse.Failure(request.RequestId, "A password is already configured.");
        }

        var hash = PasswordHasher.Hash(payload.Password);
        _configCache.Update(c =>
        {
            c.PasswordHash = hash.Hash;
            c.PasswordSalt = hash.Salt;
            c.PasswordIterations = hash.Iterations;
        });

        return IpcResponse.Success(request.RequestId);
    }

    private IpcResponse HandleChangePassword(IpcRequest request)
    {
        var payload = Deserialize<ChangePasswordRequestDto>(request.PayloadJson);
        if (payload is null)
        {
            return IpcResponse.Failure(request.RequestId, "Malformed request.");
        }

        var config = _configCache.Current;
        if (!config.PasswordConfigured ||
            !PasswordHasher.Verify(payload.OldPassword, config.PasswordHash!, config.PasswordSalt!, config.PasswordIterations))
        {
            return IpcResponse.Failure(request.RequestId, "Current password is incorrect.");
        }

        var hash = PasswordHasher.Hash(payload.NewPassword);
        _configCache.Update(c =>
        {
            c.PasswordHash = hash.Hash;
            c.PasswordSalt = hash.Salt;
            c.PasswordIterations = hash.Iterations;
        });

        // A password change invalidates every other unlocked session (e.g. on a shared PC where
        // multiple people might have an open Settings window) - all must re-authenticate.
        _authSessionManager.RevokeAll();

        return IpcResponse.Success(request.RequestId);
    }

    private IpcResponse HandleRequestGrace(IpcRequest request)
    {
        var payload = Deserialize<RequestGraceRequestDto>(request.PayloadJson);
        if (payload is null)
        {
            return IpcResponse.Failure(request.RequestId, "Malformed request.");
        }

        var rule = _configCache.Current.Rules.Find(r => r.Id == payload.RuleId);
        if (rule is null)
        {
            return IpcResponse.Success(request.RequestId, Serialize(new RequestGraceResponseDto(false, "That limit no longer exists.")));
        }

        var result = _graceManager.TryStartGrace(rule.Id, rule.ResetTime);
        return result switch
        {
            GraceResult.Ok => IpcResponse.Success(request.RequestId, Serialize(new RequestGraceResponseDto(true, null))),
            GraceResult.AlreadyUsedToday => IpcResponse.Success(request.RequestId,
                Serialize(new RequestGraceResponseDto(false, "You've already used today's one-minute ignore for this limit."))),
            _ => IpcResponse.Failure(request.RequestId, "Unexpected grace result."),
        };
    }

    private IpcResponse HandleUpdateRules(IpcRequest request)
    {
        var payload = Deserialize<UpdateRulesRequestDto>(request.PayloadJson);
        if (payload is null)
        {
            return IpcResponse.Failure(request.RequestId, "Malformed request.");
        }

        var newRules = new List<LimitRule>();
        foreach (var dto in payload.Rules)
        {
            if (!Enum.TryParse<LimitTargetType>(dto.TargetType, ignoreCase: true, out var targetType))
            {
                return IpcResponse.Failure(request.RequestId, $"Unknown target type '{dto.TargetType}'.");
            }
            if (string.IsNullOrWhiteSpace(dto.Target))
            {
                return IpcResponse.Failure(request.RequestId, "A rule's target cannot be empty.");
            }
            if (dto.DailyBudgetMinutes <= 0)
            {
                return IpcResponse.Failure(request.RequestId, "A rule's daily budget must be positive.");
            }

            newRules.Add(new LimitRule
            {
                Id = dto.Id ?? Guid.NewGuid(),
                TargetType = targetType,
                Target = LimitRule.NormalizeTarget(targetType, dto.Target),
                DailyBudget = TimeSpan.FromMinutes(dto.DailyBudgetMinutes),
                Enabled = dto.Enabled,
            });
        }

        _configCache.Update(c => c.Rules = newRules);
        return IpcResponse.Success(request.RequestId);
    }

    private IpcResponse HandlePauseMonitoring(IpcRequest request)
    {
        var payload = Deserialize<PauseMonitoringRequestDto>(request.PayloadJson);
        if (payload is null || payload.Minutes <= 0)
        {
            return IpcResponse.Failure(request.RequestId, "Malformed request.");
        }

        _configCache.Update(c => c.MonitoringPausedUntil = _clock.UtcNow.AddMinutes(payload.Minutes));
        return IpcResponse.Success(request.RequestId);
    }

    private IpcResponse HandleResumeMonitoring(IpcRequest request)
    {
        _configCache.Update(c => c.MonitoringPausedUntil = null);
        return IpcResponse.Success(request.RequestId);
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, IpcJson.Options);

    private static T? Deserialize<T>(string? json) =>
        json is null ? default : JsonSerializer.Deserialize<T>(json, IpcJson.Options);
}
