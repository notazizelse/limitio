namespace LimitIO.Service.Capture;

/// <summary>
/// Extracts the Server Name Indication (SNI) hostname from a TLS ClientHello, without decrypting or
/// terminating the connection — we only need to read a few plaintext bytes at the very start of the
/// handshake. This is what lets per-website time limits work for HTTPS without installing a root
/// certificate or breaking certificate pinning in apps like Telegram or banking apps.
///
/// Known, accepted limitation (see docs/ARCHITECTURE.md): TLS 1.3 Encrypted Client Hello (ECH) encrypts
/// this extension. When ECH is in use, <see cref="TryParseServerName"/> returns false — callers must fall
/// back to coarser (IP-based or whole-process) enforcement for that connection, not treat this as a bug.
/// Every failure path below returns false rather than throwing, since malformed/truncated/unexpected
/// input from the network is the normal case, not an exceptional one.
/// </summary>
public static class TlsClientHelloParser
{
    private const byte ContentTypeHandshake = 22;
    private const byte HandshakeTypeClientHello = 1;
    private const ushort ExtensionTypeServerName = 0;
    private const byte ServerNameTypeHostName = 0;

    /// <summary>
    /// Attempts to parse a Server Name Indication hostname out of the given bytes, which should be the
    /// start of a TCP flow's payload (possibly spanning more than one TLS record if this is only a
    /// fragment — callers should keep accumulating and retrying via <see cref="FlowReassemblyTracker"/>
    /// until this returns true or the flow's data cap is reached).
    /// </summary>
    public static bool TryParseServerName(ReadOnlySpan<byte> data, out string? serverName)
    {
        serverName = null;

        // TLS record header: type(1) + version(2) + length(2) = 5 bytes.
        if (data.Length < 5)
        {
            return false;
        }

        if (data[0] != ContentTypeHandshake)
        {
            return false;
        }

        int recordLength = (data[3] << 8) | data[4];
        int recordEnd = 5 + recordLength;
        if (data.Length < recordEnd)
        {
            // Not all of the first record has arrived yet (or handshake spans multiple records) -
            // caller should feed more bytes once available.
            return false;
        }

        var handshake = data[5..recordEnd];

        // Handshake header: msg_type(1) + length(3) = 4 bytes.
        if (handshake.Length < 4 || handshake[0] != HandshakeTypeClientHello)
        {
            return false;
        }

        int helloLength = (handshake[1] << 16) | (handshake[2] << 8) | handshake[3];
        if (handshake.Length < 4 + helloLength)
        {
            return false;
        }

        var hello = handshake.Slice(4, helloLength);
        return TryParseClientHelloBody(hello, out serverName);
    }

    private static bool TryParseClientHelloBody(ReadOnlySpan<byte> hello, out string? serverName)
    {
        serverName = null;
        int pos = 0;

        // client_version(2) + random(32)
        if (hello.Length < 34)
        {
            return false;
        }
        pos += 34;

        // session_id: length(1) + bytes
        if (!TryReadU8LengthPrefixed(hello, ref pos, out _))
        {
            return false;
        }

        // cipher_suites: length(2) + bytes
        if (!TryReadU16LengthPrefixed(hello, ref pos, out _))
        {
            return false;
        }

        // compression_methods: length(1) + bytes
        if (!TryReadU8LengthPrefixed(hello, ref pos, out _))
        {
            return false;
        }

        // extensions are optional - if there's no more data, there's no SNI.
        if (pos >= hello.Length)
        {
            return false;
        }

        if (!TryReadU16(hello, ref pos, out int extensionsLength))
        {
            return false;
        }

        int extensionsEnd = pos + extensionsLength;
        if (extensionsEnd > hello.Length)
        {
            return false;
        }

        while (pos + 4 <= extensionsEnd)
        {
            if (!TryReadU16(hello, ref pos, out int extType))
            {
                return false;
            }
            if (!TryReadU16(hello, ref pos, out int extLength))
            {
                return false;
            }
            if (pos + extLength > extensionsEnd)
            {
                return false;
            }

            var extData = hello.Slice(pos, extLength);
            if (extType == ExtensionTypeServerName && TryParseServerNameExtension(extData, out serverName))
            {
                return true;
            }

            pos += extLength;
        }

        return false;
    }

    private static bool TryParseServerNameExtension(ReadOnlySpan<byte> extData, out string? serverName)
    {
        serverName = null;
        int pos = 0;

        // server_name_list: length(2) + entries
        if (!TryReadU16(extData, ref pos, out int listLength))
        {
            return false;
        }
        if (pos + listLength > extData.Length)
        {
            return false;
        }

        int listEnd = pos + listLength;
        while (pos + 3 <= listEnd)
        {
            byte nameType = extData[pos];
            pos += 1;
            if (!TryReadU16(extData, ref pos, out int nameLength))
            {
                return false;
            }
            if (pos + nameLength > listEnd)
            {
                return false;
            }

            if (nameType == ServerNameTypeHostName)
            {
                var nameBytes = extData.Slice(pos, nameLength);
                serverName = System.Text.Encoding.ASCII.GetString(nameBytes);
                return !string.IsNullOrWhiteSpace(serverName);
            }

            pos += nameLength;
        }

        return false;
    }

    private static bool TryReadU8LengthPrefixed(ReadOnlySpan<byte> data, ref int pos, out ReadOnlySpan<byte> value)
    {
        value = default;
        if (pos >= data.Length)
        {
            return false;
        }
        int length = data[pos];
        pos += 1;
        if (pos + length > data.Length)
        {
            return false;
        }
        value = data.Slice(pos, length);
        pos += length;
        return true;
    }

    private static bool TryReadU16LengthPrefixed(ReadOnlySpan<byte> data, ref int pos, out ReadOnlySpan<byte> value)
    {
        value = default;
        if (!TryReadU16(data, ref pos, out int length))
        {
            return false;
        }
        if (pos + length > data.Length)
        {
            return false;
        }
        value = data.Slice(pos, length);
        pos += length;
        return true;
    }

    private static bool TryReadU16(ReadOnlySpan<byte> data, ref int pos, out int value)
    {
        value = 0;
        if (pos + 2 > data.Length)
        {
            return false;
        }
        value = (data[pos] << 8) | data[pos + 1];
        pos += 2;
        return true;
    }
}
