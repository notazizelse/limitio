using LimitIO.Service.Ipc;
using LimitIO.Service.Tests.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LimitIO.Service.Tests.Ipc;

[TestClass]
public class AuthSessionManagerTests
{
    private FakeClock _clock = null!;
    private AuthSessionManager _manager = null!;

    [TestInitialize]
    public void Setup()
    {
        _clock = new FakeClock(new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero));
        _manager = new AuthSessionManager(_clock);
    }

    [TestMethod]
    public void IssueSession_ProducesValidToken()
    {
        var token = _manager.IssueSession();

        Assert.IsTrue(_manager.IsValid(token));
    }

    [TestMethod]
    public void IsValid_UnknownToken_ReturnsFalse()
    {
        Assert.IsFalse(_manager.IsValid("not-a-real-token"));
    }

    [TestMethod]
    public void IsValid_NullOrEmptyToken_ReturnsFalse()
    {
        Assert.IsFalse(_manager.IsValid(null));
        Assert.IsFalse(_manager.IsValid(""));
    }

    [TestMethod]
    public void IsValid_AfterExpiry_ReturnsFalse()
    {
        var token = _manager.IssueSession();

        _clock.Advance(TimeSpan.FromMinutes(6));

        Assert.IsFalse(_manager.IsValid(token));
    }

    [TestMethod]
    public void IsValid_SlidingExpiry_ExtendsOnUse()
    {
        var token = _manager.IssueSession();

        _clock.Advance(TimeSpan.FromMinutes(4));
        Assert.IsTrue(_manager.IsValid(token)); // touches/renews expiry

        _clock.Advance(TimeSpan.FromMinutes(4));
        Assert.IsTrue(_manager.IsValid(token)); // would have expired without the renewal above
    }

    [TestMethod]
    public void Revoke_MakesTokenInvalid()
    {
        var token = _manager.IssueSession();

        _manager.Revoke(token);

        Assert.IsFalse(_manager.IsValid(token));
    }

    [TestMethod]
    public void RevokeAll_InvalidatesEverySession()
    {
        var tokenA = _manager.IssueSession();
        var tokenB = _manager.IssueSession();

        _manager.RevokeAll();

        Assert.IsFalse(_manager.IsValid(tokenA));
        Assert.IsFalse(_manager.IsValid(tokenB));
    }

    [TestMethod]
    public void GetRequiredBackoff_NoFailuresYet_ReturnsNull()
    {
        Assert.IsNull(_manager.GetRequiredBackoff());
    }

    [TestMethod]
    public void GetRequiredBackoff_AfterFailure_RequiresWait()
    {
        _manager.RecordFailure();

        var backoff = _manager.GetRequiredBackoff();

        Assert.IsNotNull(backoff);
        Assert.IsTrue(backoff > TimeSpan.Zero);
    }

    [TestMethod]
    public void GetRequiredBackoff_EscalatesWithRepeatedFailures()
    {
        _manager.RecordFailure();
        var firstBackoff = _manager.GetRequiredBackoff()!.Value;

        _clock.Advance(firstBackoff + TimeSpan.FromMilliseconds(1));
        _manager.RecordFailure();
        var secondBackoff = _manager.GetRequiredBackoff()!.Value;

        Assert.IsTrue(secondBackoff > firstBackoff);
    }

    [TestMethod]
    public void GetRequiredBackoff_ExpiresOverTime()
    {
        _manager.RecordFailure();
        var backoff = _manager.GetRequiredBackoff()!.Value;

        _clock.Advance(backoff + TimeSpan.FromSeconds(1));

        Assert.IsNull(_manager.GetRequiredBackoff());
    }

    [TestMethod]
    public void IssueSession_ResetsFailureCount()
    {
        _manager.RecordFailure();
        _manager.RecordFailure();

        _manager.IssueSession();

        Assert.IsNull(_manager.GetRequiredBackoff());
    }
}
