using LimitIO.Core.Time;

namespace LimitIO.Service.Tests.TestSupport;

internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; }

    public DateTimeOffset LocalNow => UtcNow;

    public FakeClock(DateTimeOffset initial) => UtcNow = initial;

    public void Advance(TimeSpan by) => UtcNow += by;
}
