using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LimitIO.UI.Interop;

/// <summary>
/// Forces a WPF window to become the real OS foreground window, not just WPF's own logical "focus".
///
/// This exists because of a genuine bug: every dialog in this app (FirstRunSetupWizard,
/// PasswordPromptWindow especially) is opened after an `await` - either the app's own startup status
/// check, or a tray-icon click handler's `await _client.GetStatusAsync()` before showing anything. That
/// await breaks the synchronous chain from the original user-input event (the tray click, or the process
/// launch), and Windows' foreground-activation rules can then leave the newly shown window visible but
/// without real keyboard focus - `PasswordBox.Focus()` alone only sets *logical* focus inside WPF, which
/// has no effect if the window itself was never given the OS-level foreground/activation the user
/// actually needs to type into it. The visible symptom is exactly "the window is there but I can't type
/// anything" until the user thinks to click into it manually.
///
/// `SetForegroundWindow` called on a window this same process just created is reliably permitted (the
/// restriction mainly stops *other* processes stealing focus), so this is safe to call unconditionally.
/// </summary>
internal static class WindowActivator
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    /// <summary>Call from a window's Loaded handler (by which point its HWND definitely exists).</summary>
    public static void ForceToForeground(Window window)
    {
        try
        {
            window.Activate();

            var handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero)
            {
                BringWindowToTop(handle);
                SetForegroundWindow(handle);
            }
        }
        catch
        {
            // Best-effort - a focus nicety must never be the reason a window fails to open.
        }
    }
}
