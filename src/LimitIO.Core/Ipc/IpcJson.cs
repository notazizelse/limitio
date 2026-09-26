using System.Text.Json;

namespace LimitIO.Core.Ipc;

/// <summary>Shared JSON settings for both the pipe envelope and payload strings, so Service and UI never disagree on casing.</summary>
public static class IpcJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
