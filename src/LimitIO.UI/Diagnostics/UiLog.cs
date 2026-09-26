using System.IO;

namespace LimitIO.UI.Diagnostics;

/// <summary>
/// Minimal, crash-proof file logger for the UI process. Exists so an unexpected error has somewhere to
/// land besides silently killing the tray app - previously there was no global exception handling at
/// all, so any unhandled exception in an async event handler (a timer tick, a menu click) would
/// terminate the whole process with zero trace of what happened. Every method here swallows its own
/// failures, since a logging bug must never be the thing that crashes the app it's trying to protect.
///
/// Deliberately writes under %LocalAppData% (per-user, always writable by whoever is logged in) rather
/// than %ProgramData%\LimitIO - that directory is ACL'd to SYSTEM/Administrators only by the installer
/// (see Installer.Helper's hardening), so a standard-account UI process could never write there.
/// </summary>
public static class UiLog
{
    private static readonly object Lock = new();

    private static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LimitIO", "logs");

    public static void Error(string message, Exception ex)
    {
        Write($"ERROR: {message}{Environment.NewLine}{ex}");
    }

    public static void Warn(string message)
    {
        Write($"WARN: {message}");
    }

    private static void Write(string body)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(LogDirectory);
                var path = Path.Combine(LogDirectory, $"ui-{DateTime.Now:yyyy-MM-dd}.log");
                File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss}] {body}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never be the reason the app crashes further.
        }
    }
}
