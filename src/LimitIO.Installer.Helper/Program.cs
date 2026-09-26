using LimitIO.Core.Runtime;
using LimitIO.Installer.Helper;
using LimitIO.Installer.Helper.Commands;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: LimitIO.Installer.Helper.exe <install|uninstall> <installDir>");
    return 2;
}

var command = args[0].ToLowerInvariant();
var installDir = args[1];

using var log = new Logger(Paths.InstallLogPath);

try
{
    return command switch
    {
        "install" => InstallCommand.Run(installDir, log),
        "uninstall" => UninstallCommand.Run(installDir, log),
        _ => Unknown(command, log),
    };
}
catch (Exception ex)
{
    log.Write($"FATAL: unhandled exception: {ex}");
    return 1;
}

static int Unknown(string command, Logger log)
{
    log.Write($"Unknown command '{command}'. Expected 'install' or 'uninstall'.");
    return 2;
}
