using LimitIO.Core.Ipc;
using LimitIO.Core.Storage;
using LimitIO.Service.Configuration;
using LimitIO.Service.Enforcement;
using LimitIO.Service.Ipc;
using LimitIO.Service.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LimitIO.Service.Tests.Ipc;

[TestClass]
public class RequestRouterTests
{
    private string _tempDir = null!;
    private ConfigStore _configStore = null!;
    private ConfigCache _configCache = null!;
    private UsageStore _usageStore = null!;
    private FakeClock _clock = null!;
    private GraceManager _graceManager = null!;
    private AuthSessionManager _authSessionManager = null!;
    private RequestRouter _router = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LimitIOTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _configStore = new ConfigStore(Path.Combine(_tempDir, "config.json"));
        _configCache = new ConfigCache(_configStore);
        _usageStore = new UsageStore(Path.Combine(_tempDir, "usage.db"));
        _clock = new FakeClock(new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero));
        _graceManager = new GraceManager(_usageStore, _clock);
        _authSessionManager = new AuthSessionManager(_clock);
        _router = new RequestRouter(_configCache, _usageStore, _graceManager, _authSessionManager, _clock, NullLogger<RequestRouter>.Instance);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _usageStore.Dispose();
        Directory.Delete(_tempDir, recursive: true);
    }

    private static IpcRequest MakeRequest(IpcRequestType type, object? payload = null, string? sessionToken = null) => new()
    {
        RequestId = Guid.NewGuid(),
        ProtocolVersion = ProtocolVersion.Current,
        Type = type,
        SessionToken = sessionToken,
        PayloadJson = payload is null ? null : System.Text.Json.JsonSerializer.Serialize(payload, IpcJson.Options),
    };

    private static T DeserializePayload<T>(IpcResponse response) =>
        System.Text.Json.JsonSerializer.Deserialize<T>(response.PayloadJson!, IpcJson.Options)!;

    [TestMethod]
    public void Handle_ProtocolVersionMismatch_Fails()
    {
        var request = MakeRequest(IpcRequestType.GetStatus) with { ProtocolVersion = 999 };

        var response = _router.Handle(request);

        Assert.IsFalse(response.Ok);
    }

    [TestMethod]
    public void Handle_GetStatus_NoPasswordYet_ReportsNotConfigured()
    {
        var response = _router.Handle(MakeRequest(IpcRequestType.GetStatus));

        Assert.IsTrue(response.Ok);
        var status = DeserializePayload<StatusResponseDto>(response);
        Assert.IsFalse(status.PasswordConfigured);
    }

    [TestMethod]
    public void Handle_SetInitialPassword_ThenGetStatus_ReportsConfigured()
    {
        _router.Handle(MakeRequest(IpcRequestType.SetInitialPassword, new SetInitialPasswordRequestDto("hunter2")));

        var status = DeserializePayload<StatusResponseDto>(_router.Handle(MakeRequest(IpcRequestType.GetStatus)));

        Assert.IsTrue(status.PasswordConfigured);
    }

    [TestMethod]
    public void Handle_SetInitialPassword_Twice_SecondCallFails()
    {
        _router.Handle(MakeRequest(IpcRequestType.SetInitialPassword, new SetInitialPasswordRequestDto("hunter2")));

        var second = _router.Handle(MakeRequest(IpcRequestType.SetInitialPassword, new SetInitialPasswordRequestDto("different")));

        Assert.IsFalse(second.Ok);
    }

    [TestMethod]
    public void Handle_VerifyPassword_CorrectPassword_ReturnsSessionToken()
    {
        _router.Handle(MakeRequest(IpcRequestType.SetInitialPassword, new SetInitialPasswordRequestDto("hunter2")));

        var response = _router.Handle(MakeRequest(IpcRequestType.VerifyPassword, new VerifyPasswordRequestDto("hunter2")));

        var result = DeserializePayload<VerifyPasswordResponseDto>(response);
        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.SessionToken);
    }

    [TestMethod]
    public void Handle_VerifyPassword_WrongPassword_Fails()
    {
        _router.Handle(MakeRequest(IpcRequestType.SetInitialPassword, new SetInitialPasswordRequestDto("hunter2")));

        var response = _router.Handle(MakeRequest(IpcRequestType.VerifyPassword, new VerifyPasswordRequestDto("wrong")));

        var result = DeserializePayload<VerifyPasswordResponseDto>(response);
        Assert.IsFalse(result.Success);
        Assert.IsNull(result.SessionToken);
    }

    [TestMethod]
    public void Handle_UpdateRules_WithoutSessionToken_IsRejected()
    {
        var response = _router.Handle(MakeRequest(IpcRequestType.UpdateRules,
            new UpdateRulesRequestDto([new RuleUpsertDto(null, "Domain", "instagram.com", 30, true)])));

        Assert.IsFalse(response.Ok);
    }

    [TestMethod]
    public void Handle_UpdateRules_WithValidSessionToken_Succeeds()
    {
        _router.Handle(MakeRequest(IpcRequestType.SetInitialPassword, new SetInitialPasswordRequestDto("hunter2")));
        var verify = DeserializePayload<VerifyPasswordResponseDto>(
            _router.Handle(MakeRequest(IpcRequestType.VerifyPassword, new VerifyPasswordRequestDto("hunter2"))));

        var response = _router.Handle(MakeRequest(IpcRequestType.UpdateRules,
            new UpdateRulesRequestDto([new RuleUpsertDto(null, "Domain", "instagram.com", 30, true)]),
            sessionToken: verify.SessionToken));

        Assert.IsTrue(response.Ok);
        Assert.AreEqual(1, _configCache.Current.Rules.Count);
    }

    [TestMethod]
    public void Handle_RequestGrace_NoRule_FailsGracefully()
    {
        var response = _router.Handle(MakeRequest(IpcRequestType.RequestGrace, new RequestGraceRequestDto(Guid.NewGuid())));

        Assert.IsTrue(response.Ok); // transport-level success, business-level failure in the payload
        var result = DeserializePayload<RequestGraceResponseDto>(response);
        Assert.IsFalse(result.Success);
    }

    [TestMethod]
    public void Handle_RequestGrace_NoPasswordNeeded_Succeeds()
    {
        // Add a rule directly via the cache (bypassing auth) to isolate the grace behavior under test.
        var rule = LimitIO.Core.Models.LimitRule.Create(LimitIO.Core.Models.LimitTargetType.Domain, "instagram.com", TimeSpan.FromMinutes(30));
        _configCache.Update(c => c.Rules.Add(rule));

        var response = _router.Handle(MakeRequest(IpcRequestType.RequestGrace, new RequestGraceRequestDto(rule.Id)));

        var result = DeserializePayload<RequestGraceResponseDto>(response);
        Assert.IsTrue(result.Success);
    }

    [TestMethod]
    public void Handle_ChangePassword_WrongOldPassword_Fails()
    {
        _router.Handle(MakeRequest(IpcRequestType.SetInitialPassword, new SetInitialPasswordRequestDto("hunter2")));
        var verify = DeserializePayload<VerifyPasswordResponseDto>(
            _router.Handle(MakeRequest(IpcRequestType.VerifyPassword, new VerifyPasswordRequestDto("hunter2"))));

        var response = _router.Handle(MakeRequest(IpcRequestType.ChangePassword,
            new ChangePasswordRequestDto("wrong-old", "newpassword"), sessionToken: verify.SessionToken));

        Assert.IsFalse(response.Ok);
    }

    [TestMethod]
    public void Handle_ChangePassword_RevokesOtherSessions()
    {
        _router.Handle(MakeRequest(IpcRequestType.SetInitialPassword, new SetInitialPasswordRequestDto("hunter2")));
        var verify = DeserializePayload<VerifyPasswordResponseDto>(
            _router.Handle(MakeRequest(IpcRequestType.VerifyPassword, new VerifyPasswordRequestDto("hunter2"))));

        _router.Handle(MakeRequest(IpcRequestType.ChangePassword,
            new ChangePasswordRequestDto("hunter2", "newpassword"), sessionToken: verify.SessionToken));

        // The very session token that just changed the password should now be revoked too.
        var followUp = _router.Handle(MakeRequest(IpcRequestType.PauseMonitoring,
            new PauseMonitoringRequestDto(5), sessionToken: verify.SessionToken));

        Assert.IsFalse(followUp.Ok);
    }
}
