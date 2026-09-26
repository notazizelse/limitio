using System.Diagnostics;

namespace LimitIO.Installer.Helper;

/// <summary>Runs a native command-line tool (sc.exe, icacls.exe, schtasks.exe) and logs its exit code/output.</summary>
public static class ProcessRunner
{
    public sealed record Result(int ExitCode, string StdOut, string StdErr)
    {
        public bool Succeeded => ExitCode == 0;
    }

    public static Result Run(string fileName, string arguments, Logger log)
    {
        log.Write($"$ {fileName} {arguments}");

        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start process: {fileName}");

        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        var result = new Result(process.ExitCode, stdOut, stdErr);

        log.Write($"  exit code: {result.ExitCode}");
        if (!string.IsNullOrWhiteSpace(stdOut))
        {
            log.Write($"  stdout: {stdOut.Trim()}");
        }
        if (!string.IsNullOrWhiteSpace(stdErr))
        {
            log.Write($"  stderr: {stdErr.Trim()}");
        }

        return result;
    }

    /// <summary>Runs the command, treating any nonzero exit code as expected-and-ignorable (used for idempotent steps like "delete a service that may not exist yet").</summary>
    public static Result RunIgnoringFailure(string fileName, string arguments, Logger log)
    {
        try
        {
            return Run(fileName, arguments, log);
        }
        catch (Exception ex)
        {
            log.Write($"  (ignored) failed to run: {ex.Message}");
            return new Result(-1, "", ex.Message);
        }
    }
}
