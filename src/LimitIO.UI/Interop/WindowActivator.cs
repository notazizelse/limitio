using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LimitIO.UI.Interop;

/// <summary>
/// Forces a WPF window to become the real OS foreground window, not just WPF's own logical "focus".
///
/// This exists because of a genuine bug: dialogs in this app are shown either after an `await` (breaking
/// the synchronous chain from the original user-input event) or right after a *previous* window in the
/// same flow closes (PasswordPromptWindow closing, then SettingsWindow opening). Both are situations
/// Windows' foreground-lock rules specifically target: for a brief instant between the old window closing
/// and the new one opening, no window in this process is "the foreground window", and plain
/// `SetForegroundWindow` is then silently vetoed by the OS - it returns without effect, leaving the new
/// window visible and clickable but never actually focused. `PasswordBox.Focus()`/`TextBox.Focus()` alone
/// only sets *logical* focus inside WPF, which has no effect if the window itself never got real OS-level
/// activation. The visible symptom is exactly "the window is there but I can't type anything" until the
/// user manually clicks into it - a v0.1.2 fix using bare `SetForegroundWindow` did not actually resolve
/// this for `SettingsWindow`, which is the specific case (window-switch, not just an await) that call is
/// documented to fail for.
///
/// The reliable workaround is `AttachThreadInput`: temporarily joining this thread's input queue to
/// whichever thread currently owns the real foreground window grants this thread the same
/// foreground-activation rights Windows would otherwise deny it. This is the standard, widely-documented
/// technique for this exact problem (there is no better-supported one on Win32).
/// </summary>
internal static class WindowActivator
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    /// <summary>Call from a window's Loaded handler (by which point its HWND definitely exists).</summary>
    public static void ForceToForeground(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                window.Activate();
                return;
            }

            var foregroundWindow = GetForegroundWindow();
            var foregroundThreadId = foregroundWindow == IntPtr.Zero
                ? 0u
                : GetWindowThreadProcessId(foregroundWindow, IntPtr.Zero);
            var currentThreadId = GetCurrentThreadId();

            var attached = false;
            if (foregroundThreadId != 0 && foregroundThreadId != currentThreadId)
            {
                // Joining input queues with whatever currently owns the foreground grants this thread the
                // same right to call SetForegroundWindow that the OS would otherwise reserve for it alone -
                // this is what actually makes the call below succeed instead of silently no-op'ing.
                attached = AttachThreadInput(currentThreadId, foregroundThreadId, true);
            }

            try
            {
                BringWindowToTop(handle);
                SetForegroundWindow(handle);
                window.Activate();

                // Belt-and-braces: toggling Topmost forces a Z-order/activation pass even in the rare case
                // SetForegroundWindow itself still didn't stick.
                window.Topmost = true;
                window.Topmost = false;
            }
            finally
            {
                if (attached)
                {
                    AttachThreadInput(currentThreadId, foregroundThreadId, false);
                }
            }
        }
        catch
        {
            // Best-effort - a focus nicety must never be the reason a window fails to open.
        }
    }
}
