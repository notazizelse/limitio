using LimitIO.Service.Accounting;
using LimitIO.Service.Capture;
using LimitIO.Service.Enforcement;
using LimitIO.Service.Ipc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LimitIO.Service.Worker;

/// <summary>
/// The top-level orchestrator: wires packet capture into usage accounting into limit enforcement, and
/// runs the IPC server alongside it, for the lifetime of the Windows Service process.
/// </summary>
public sealed class EnforcementCoordinatorService : BackgroundService
{
    private readonly PacketCaptureEngine _captureEngine;
    private readonly UsageAccountant _usageAccountant;
    private readonly LimitEnforcer _limitEnforcer;
    private readonly NamedPipeServer _pipeServer;
    private readonly ILogger<EnforcementCoordinatorService> _logger;

    public EnforcementCoordinatorService(
        PacketCaptureEngine captureEngine,
        UsageAccountant usageAccountant,
        LimitEnforcer limitEnforcer,
        NamedPipeServer pipeServer,
        ILogger<EnforcementCoordinatorService> logger)
    {
        _captureEngine = captureEngine;
        _usageAccountant = usageAccountant;
        _limitEnforcer = limitEnforcer;
        _pipeServer = pipeServer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _captureEngine.BlockDecider = _limitEnforcer;
        _captureEngine.FlowObserved += _usageAccountant.OnFlowObserved;

        var tasks = new List<Task> { _pipeServer.RunAsync(stoppingToken) };

        try
        {
            _captureEngine.Open();
            tasks.Add(_captureEngine.RunAsync(stoppingToken));
            tasks.Add(RunAccountingTickLoopAsync(stoppingToken));
        }
        catch (Exception ex)
        {
            // The IPC server (and therefore the UI) still comes up even if capture can't start, so a
            // GetStatus call at least succeeds and diagnostics reach the log - a fully silent hang would
            // be far worse than "no enforcement, but the app tells you why".
            _logger.LogCritical(ex,
                "Failed to open the packet capture engine - no limits will be enforced until this is resolved " +
                "(check the service is running as SYSTEM/LocalSystem and WinDivert64.sys sits next to the service executable).");
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task RunAccountingTickLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(UsageAccountant.TickInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    _usageAccountant.Tick();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Usage accounting tick failed.");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public override void Dispose()
    {
        _captureEngine.FlowObserved -= _usageAccountant.OnFlowObserved;
        _captureEngine.Dispose();
        base.Dispose();
    }
}
