using System.Text;
using System.Text.Json;

namespace LimitIO.Core.Ipc;

/// <summary>
/// Newline-delimited JSON framing over any <see cref="Stream"/> (a named pipe in practice). Kept
/// stream-based rather than pipe-type-specific so it's usable from both the server (NamedPipeServerStream)
/// and client (NamedPipeClientStream) sides, and unit-testable against a plain MemoryStream/pipe pair.
/// </summary>
public static class PipeMessageTransport
{
    public static async Task WriteMessageAsync<T>(Stream stream, T message, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(message, IpcJson.Options);
        var bytes = Encoding.UTF8.GetBytes(json + "\n");
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Reads one newline-terminated JSON message, or null if the stream ended before a full line arrived.</summary>
    public static async Task<T?> ReadMessageAsync<T>(Stream stream, CancellationToken ct = default)
    {
        var line = await ReadLineAsync(stream, ct).ConfigureAwait(false);
        if (line is null)
        {
            return default;
        }
        return JsonSerializer.Deserialize<T>(line, IpcJson.Options);
    }

    private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var singleByte = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(singleByte.AsMemory(0, 1), ct).ConfigureAwait(false);
            if (read == 0)
            {
                return buffer.Length == 0 ? null : Encoding.UTF8.GetString(buffer.ToArray());
            }
            if (singleByte[0] == (byte)'\n')
            {
                return Encoding.UTF8.GetString(buffer.ToArray());
            }
            buffer.WriteByte(singleByte[0]);
        }
    }
}
