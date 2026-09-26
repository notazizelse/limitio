namespace LimitIO.Core.Ipc;

/// <summary>
/// Shared request/response contract between LimitIO.UI (client) and LimitIO.Service (server), so the two
/// can never structurally drift apart. Framing is newline-delimited JSON over a named pipe — see
/// docs/ARCHITECTURE.md section 6. Bump <see cref="CurrentVersion"/> whenever a message shape changes;
/// the Service rejects a mismatched version rather than guessing at an old/new schema.
/// </summary>
public static class ProtocolVersion
{
    public const int Current = 1;
}

public enum IpcRequestType
{
    GetStatus,
    VerifyPassword,
    SetInitialPassword,
    ChangePassword,
    RequestGrace,
    UpdateRules,
    PauseMonitoring,
    ResumeMonitoring,
    Heartbeat,
}

public sealed record IpcRequest
{
    public required Guid RequestId { get; init; }
    public required int ProtocolVersion { get; init; } = Ipc.ProtocolVersion.Current;
    public required IpcRequestType Type { get; init; }

    /// <summary>Required for any mutating request type; obtained from a prior successful VerifyPassword.</summary>
    public string? SessionToken { get; init; }

    public string? PayloadJson { get; init; }
}

public sealed record IpcResponse
{
    public required Guid RequestId { get; init; }
    public required bool Ok { get; init; }
    public string? Error { get; init; }
    public string? PayloadJson { get; init; }

    public static IpcResponse Success(Guid requestId, string? payloadJson = null) =>
        new() { RequestId = requestId, Ok = true, PayloadJson = payloadJson };

    public static IpcResponse Failure(Guid requestId, string error) =>
        new() { RequestId = requestId, Ok = false, Error = error };
}

// ---- Payload DTOs (serialized to/from IpcRequest.PayloadJson / IpcResponse.PayloadJson) ----

public sealed record RuleStatusDto(
    Guid Id,
    string TargetType,
    string Target,
    int DailyBudgetMinutes,
    int ConsumedMinutes,
    bool Enabled,
    bool Blocked,
    bool GraceAvailable);

public sealed record StatusResponseDto(
    bool PasswordConfigured,
    bool MonitoringPaused,
    DateTimeOffset? PausedUntil,
    IReadOnlyList<RuleStatusDto> Rules);

public sealed record VerifyPasswordRequestDto(string Password);

public sealed record VerifyPasswordResponseDto(bool Success, string? SessionToken, int? RetryAfterMs);

public sealed record SetInitialPasswordRequestDto(string Password);

public sealed record ChangePasswordRequestDto(string OldPassword, string NewPassword);

public sealed record RequestGraceRequestDto(Guid RuleId);

public sealed record RequestGraceResponseDto(bool Success, string? Reason);

public sealed record RuleUpsertDto(
    Guid? Id,
    string TargetType,
    string Target,
    int DailyBudgetMinutes,
    bool Enabled);

public sealed record UpdateRulesRequestDto(IReadOnlyList<RuleUpsertDto> Rules);

public sealed record PauseMonitoringRequestDto(int Minutes);
