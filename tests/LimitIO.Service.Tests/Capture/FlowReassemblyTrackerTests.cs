using System.Net;
using LimitIO.Core.Models;
using LimitIO.Service.Capture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LimitIO.Service.Tests.Capture;

[TestClass]
public class FlowReassemblyTrackerTests
{
    private static FlowKey SampleKey => new(
        IsTcp: true,
        LocalAddress: IPAddress.Parse("192.168.1.5"),
        LocalPort: 51000,
        RemoteAddress: IPAddress.Parse("93.184.216.34"),
        RemotePort: 443);

    [TestMethod]
    public void Append_AccumulatesAcrossCalls()
    {
        var tracker = new FlowReassemblyTracker();
        var now = DateTimeOffset.UtcNow;

        tracker.Append(SampleKey, [1, 2, 3], now);
        var result = tracker.Append(SampleKey, [4, 5, 6], now);

        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5, 6 }, result.ToArray());
    }

    [TestMethod]
    public void Append_DifferentFlows_DoNotShareBuffers()
    {
        var tracker = new FlowReassemblyTracker();
        var now = DateTimeOffset.UtcNow;
        var otherKey = SampleKey with { LocalPort = 51001 };

        tracker.Append(SampleKey, [1, 2, 3], now);
        var otherResult = tracker.Append(otherKey, [9, 9], now);

        CollectionAssert.AreEqual(new byte[] { 9, 9 }, otherResult.ToArray());
    }

    [TestMethod]
    public void Forget_RemovesFlow_SoNextAppendStartsFresh()
    {
        var tracker = new FlowReassemblyTracker();
        var now = DateTimeOffset.UtcNow;

        tracker.Append(SampleKey, [1, 2, 3], now);
        tracker.Forget(SampleKey);
        var result = tracker.Append(SampleKey, [9], now);

        CollectionAssert.AreEqual(new byte[] { 9 }, result.ToArray());
    }

    [TestMethod]
    public void Append_BeyondCap_StopsGrowing()
    {
        var tracker = new FlowReassemblyTracker();
        var now = DateTimeOffset.UtcNow;
        var chunk = new byte[4096];

        tracker.Append(SampleKey, chunk, now);
        tracker.Append(SampleKey, chunk, now);
        var result = tracker.Append(SampleKey, chunk, now); // would be 12288 bytes uncapped

        Assert.IsTrue(result.Length <= 8192);
        Assert.IsTrue(tracker.IsCapped(SampleKey));
    }

    [TestMethod]
    public void Sweep_RemovesStaleEntries()
    {
        var tracker = new FlowReassemblyTracker();
        var start = DateTimeOffset.UtcNow;

        tracker.Append(SampleKey, [1], start);
        tracker.Sweep(start + TimeSpan.FromSeconds(30));

        Assert.AreEqual(0, tracker.TrackedFlowCount);
    }

    [TestMethod]
    public void Sweep_KeepsRecentEntries()
    {
        var tracker = new FlowReassemblyTracker();
        var start = DateTimeOffset.UtcNow;

        tracker.Append(SampleKey, [1], start);
        tracker.Sweep(start + TimeSpan.FromSeconds(2));

        Assert.AreEqual(1, tracker.TrackedFlowCount);
    }
}
