using LimitIO.Core.Runtime;

namespace LimitIO.Installer.Helper.Commands;

/// <summary>
/// Applies all of the service-hardening steps described in docs/ARCHITECTURE.md: creates the Windows
/// Service, restricts who can stop/reconfigure/delete it, configures crash auto-restart, registers a
/// Scheduled-Task backstop, and locks down the on-disk config/usage directory. Every step is idempotent -
/// safe to re-run on an upgrade install without first tearing anything down.
/// </summary>
public static class InstallCommand
{
    public const string ServiceName = "LimitIOService";
    public const string WatchdogTaskName = "LimitIOWatchdog";

    public static int Run(string installDir, Logger log)
    {
        var serviceExePath = Path.Combine(installDir, "service", "LimitIO.Service.exe");
        if (!File.Exists(serviceExePath))
        {
            log.Write($"ERROR: expected service executable not found at '{serviceExePath}'.");
            return 1;
        }

        Directory.CreateDirectory(Paths.DataDirectory);

        CreateOrUpdateService(serviceExePath, log);
        HardenServiceAcl(log);
        ConfigureCrashRecovery(log);
        RegisterWatchdogTask(serviceExePath, log);
        LockDownDirectories(installDir, log);
        StartService(log);

        log.Write("Install steps complete.");
        return 0;
    }

    private static void CreateOrUpdateService(string serviceExePath, Logger log)
    {
        // "sc create" fails harmlessly with an already-exists error on an upgrade install - that's fine,
        // "sc config" below brings an existing service's binPath up to date regardless.
        ProcessRunner.RunIgnoringFailure("sc.exe",
            $"create {ServiceName} binPath= \"{serviceExePath}\" start= auto obj= LocalSystem " +
            "DisplayName= \"LimitIO Enforcement Service\"", log);

        ProcessRunner.Run("sc.exe", $"config {ServiceName} binPath= \"{serviceExePath}\" start= auto", log);

        ProcessRunner.Run("sc.exe",
            $"description {ServiceName} \"Monitors network usage and enforces the time limits configured in LimitIO. Do not stop.\"", log);
    }

    private static void HardenServiceAcl(Logger log)
    {
        // SDDL: SYSTEM (SY) and built-in Administrators (BA) keep full control (GA) - an admin can
        // always reconfigure this regardless, since Windows ACL ownership rules let an admin reassign
        // permissions on anything, so pretending to lock out admins here would be false security. What
        // this line actually, honestly buys: Interactive Users (IU) and Authenticated Users (AU) get only
        // query rights (CCLCSWLOCRRC = query status/config/enumerate-dependents), not start/stop/config/
        // delete - a standard, non-admin account genuinely cannot touch this service. See
        // docs/ARCHITECTURE.md for why this is the real, achievable security boundary.
        const string sddl = "D:(A;;GA;;;SY)(A;;GA;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;AU)";
        ProcessRunner.Run("sc.exe", $"sdset {ServiceName} \"{sddl}\"", log);
    }

    private static void ConfigureCrashRecovery(Logger log)
    {
        ProcessRunner.Run("sc.exe",
            $"failure {ServiceName} reset= 86400 actions= restart/5000/restart/30000/restart/60000", log);

        // Makes SCM apply the recovery actions above even when the process is killed out from under it
        // (e.g. via Task Manager "End task"), not only on an unhandled crash.
        ProcessRunner.Run("sc.exe", $"failureflag {ServiceName} 1", log);
    }

    private static void RegisterWatchdogTask(string serviceExePath, Logger log)
    {
        // Belt-and-suspenders backstop alongside "sc failure": a SYSTEM-run Scheduled Task needs no
        // elevated caller at trigger time, unlike anything the (possibly standard-user) UI process could
        // do on its own, and survives even if SCM's own recovery-action bookkeeping is ever bypassed.
        ProcessRunner.RunIgnoringFailure("schtasks.exe", $"/Delete /TN \"{WatchdogTaskName}\" /F", log);

        var command = $"cmd /c \"sc query {ServiceName} | find /I \\\"RUNNING\\\" > nul || sc start {ServiceName}\"";
        ProcessRunner.Run("schtasks.exe",
            $"/Create /TN \"{WatchdogTaskName}\" /TR \"{command}\" /SC MINUTE /MO 5 /RU SYSTEM /RL HIGHEST /F", log);
    }

    private static void LockDownDirectories(string installDir, Logger log)
    {
        // Standard users can read/execute (needed to run the UI) but not write or delete; only SYSTEM
        // and Administrators get full control. Applied to both the install directory (the binaries) and
        // %ProgramData%\LimitIO (config + usage history + password hash).
        foreach (var dir in new[] { installDir, Paths.DataDirectory })
        {
            ProcessRunner.Run("icacls.exe", $"\"{dir}\" /inheritance:r", log);
            ProcessRunner.Run("icacls.exe",
                $"\"{dir}\" /grant:r \"SYSTEM:(OI)(CI)F\" \"BUILTIN\\Administrators:(OI)(CI)F\" \"BUILTIN\\Users:(OI)(CI)RX\"", log);
        }
    }

    private static void StartService(Logger log)
    {
        ProcessRunner.RunIgnoringFailure("sc.exe", $"start {ServiceName}", log);
    }
}
