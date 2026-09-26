using System.Threading.Tasks;

namespace LimitIO.UI.Diagnostics;

/// <summary>
/// Runs a fire-and-forget async action from an event handler with a catch-all around it, so a single bad
/// interaction (a hung IPC call, an unexpected null) can never propagate out of a WinForms/WPF event
/// handler and crash the whole app - previously nothing caught these, and any one of them taking down the
/// process was indistinguishable from Windows deciding the app had stopped responding.
/// </summary>
public static class UiSafe
{
    public static async void Run(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            UiLog.Error("UI action failed", ex);
        }
    }
}
