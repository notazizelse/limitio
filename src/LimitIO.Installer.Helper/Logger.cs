namespace LimitIO.Installer.Helper;

/// <summary>Appends timestamped lines to the install log, and echoes them to the console so Inno Setup's log capture sees them too.</summary>
public sealed class Logger : IDisposable
{
    private readonly StreamWriter _writer;

    public Logger(string logFilePath)
    {
        var directory = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _writer = new StreamWriter(logFilePath, append: true) { AutoFlush = true };
        Write($"--- LimitIO.Installer.Helper run started {DateTimeOffset.Now:O} ---");
    }

    public void Write(string message)
    {
        var line = $"[{DateTimeOffset.Now:HH:mm:ss}] {message}";
        Console.WriteLine(line);
        _writer.WriteLine(line);
    }

    public void Dispose()
    {
        Write("--- run finished ---");
        _writer.Dispose();
    }
}
