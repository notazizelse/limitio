using System.Text;

namespace LimitIO.Service.Capture;

/// <summary>Extracts the "Host:" header from the start of a plaintext HTTP request (port 80 traffic - no TLS involved).</summary>
public static class HttpHostHeaderParser
{
    private const int MaxHeaderBytesToScan = 4096;

    public static bool TryParseHost(ReadOnlySpan<byte> data, out string? host)
    {
        host = null;

        var scanLength = Math.Min(data.Length, MaxHeaderBytesToScan);
        var headerBytes = data[..scanLength];

        // Cheap pre-check: a request line looks like "GET /path HTTP/1.1\r\n" - bail fast on anything else
        // (in particular, this must never misfire on TLS ClientHello bytes, whose first byte is 0x16).
        if (headerBytes.Length < 4 || !LooksLikeHttpRequestLine(headerBytes))
        {
            return false;
        }

        var text = Encoding.ASCII.GetString(headerBytes);
        var headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var searchable = headerEnd >= 0 ? text[..headerEnd] : text;

        foreach (var line in searchable.Split("\r\n"))
        {
            if (line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))
            {
                var value = line["Host:".Length..].Trim();
                // Strip an explicit ":port" suffix, but not a bracketed IPv6 literal's own colons.
                if (!value.StartsWith('[') && value.Contains(':'))
                {
                    value = value[..value.IndexOf(':')];
                }
                host = value;
                return !string.IsNullOrWhiteSpace(host);
            }
        }

        return false;
    }

    private static bool LooksLikeHttpRequestLine(ReadOnlySpan<byte> data)
    {
        Span<string> methods = ["GET ", "POST", "HEAD", "PUT ", "DELE", "OPTI", "CONN", "PATC"];
        var prefix = Encoding.ASCII.GetString(data[..Math.Min(4, data.Length)]);
        foreach (var m in methods)
        {
            if (prefix.Equals(m, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }
}
