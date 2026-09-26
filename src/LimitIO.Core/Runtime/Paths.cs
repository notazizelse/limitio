namespace LimitIO.Core.Runtime;

/// <summary>
/// Central definition of where LimitIO stores its state, so the Service, the Installer.Helper (which
/// applies ACL hardening to these exact paths) and the UI's error messages never drift apart. Everything
/// lives under %ProgramData% (machine-wide, not a per-user profile) since enforcement must apply
/// regardless of which of possibly-several Windows accounts is logged in - see docs/ARCHITECTURE.md.
/// </summary>
public static class Paths
{
    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LimitIO");

    public static string ConfigFilePath => Path.Combine(DataDirectory, "config.json");

    public static string UsageDatabasePath => Path.Combine(DataDirectory, "usage.db");

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    public static string InstallLogPath => Path.Combine(DataDirectory, "install.log");
}
