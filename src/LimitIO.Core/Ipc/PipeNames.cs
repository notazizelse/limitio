namespace LimitIO.Core.Ipc;

/// <summary>Shared so the Service (which owns the pipe) and the UI (which connects to it) never disagree on the name.</summary>
public static class PipeNames
{
    public const string LimitIo = "LimitIO";
}
