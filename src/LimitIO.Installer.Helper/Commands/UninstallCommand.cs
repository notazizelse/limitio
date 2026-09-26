using LimitIO.Core.Runtime;

namespace LimitIO.Installer.Helper.Commands;

/// <summary>Reverses everything <see cref="InstallCommand"/> applied, in the opposite order, before Inno Setup deletes the files.</summary>
public static class UninstallCommand
{
    public static int Run(string installDir, Logger log)
    {
        ProcessRunner.RunIgnoringFailure("schtasks.exe", $"/Delete /TN \"{InstallCommand.WatchdogTaskName}\" /F", log);

        ProcessRunner.RunIgnoringFailure("sc.exe", $"stop {InstallCommand.ServiceName}", log);

        // Relax ACLs back to a normal, inheritable state *before* Inno Setup tries to delete these
        // directories - otherwise the SYSTEM/Administrators-only ACL from install time can make file
        // removal fail partway through and leave orphaned, hard-to-clean-up files behind.
        RelaxAcl(installDir, log);
        RelaxAcl(Paths.DataDirectory, log);

        ProcessRunner.RunIgnoringFailure("sc.exe", $"delete {InstallCommand.ServiceName}", log);

        log.Write("Uninstall steps complete.");
        return 0;
    }

    private static void RelaxAcl(string dir, Logger log)
    {
        if (!Directory.Exists(dir))
        {
            return;
        }

        ProcessRunner.RunIgnoringFailure("icacls.exe", $"\"{dir}\" /reset /T /C", log);
    }
}
