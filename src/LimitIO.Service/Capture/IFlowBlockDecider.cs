using LimitIO.Core.Models;

namespace LimitIO.Service.Capture;

/// <summary>
/// Asked once per observed packet whether its flow should be dropped instead of reinjected. Implemented
/// by <see cref="LimitIO.Service.Enforcement.LimitEnforcer"/> - kept as an interface here so the capture
/// engine doesn't need to depend on the enforcement/accounting layer, only on this narrow question.
/// </summary>
public interface IFlowBlockDecider
{
    bool ShouldBlock(FlowKey key, int processId, string? processName, string? domain);
}
