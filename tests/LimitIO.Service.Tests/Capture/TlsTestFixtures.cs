namespace LimitIO.Service.Tests.Capture;

/// <summary>Hand-rolled minimal TLS ClientHello byte builders, used only to exercise <c>TlsClientHelloParser</c>.</summary>
internal static class TlsTestFixtures
{
    public static byte[] BuildClientHelloWithSni(string hostname)
    {
        var hostBytes = System.Text.Encoding.ASCII.GetBytes(hostname);

        // server_name entry: name_type(1) + name_length(2) + name
        var sniEntry = new List<byte> { 0x00 };
        sniEntry.AddRange(U16(hostBytes.Length));
        sniEntry.AddRange(hostBytes);

        // server_name_list: length(2) + entries
        var sniList = new List<byte>();
        sniList.AddRange(U16(sniEntry.Count));
        sniList.AddRange(sniEntry);

        // extension: type(2)=0 (server_name) + length(2) + data
        var extension = new List<byte>();
        extension.AddRange(U16(0)); // server_name extension type
        extension.AddRange(U16(sniList.Count));
        extension.AddRange(sniList);

        return BuildClientHelloWithExtensions(extension);
    }

    public static byte[] BuildClientHelloWithNoExtensions()
    {
        return BuildHelloBodyAsRecord(BuildHelloBody(extensions: []));
    }

    public static byte[] BuildClientHelloWithExtensions(List<byte> rawExtensionsBytes)
    {
        return BuildHelloBodyAsRecord(BuildHelloBody(rawExtensionsBytes));
    }

    private static List<byte> BuildHelloBody(List<byte> extensions)
    {
        var body = new List<byte>();
        body.AddRange(U16(0x0303)); // client_version
        body.AddRange(new byte[32]); // random
        body.Add(0x00); // session_id length = 0
        body.AddRange(U16(2)); // cipher_suites length
        body.AddRange([0x00, 0x2f]); // one cipher suite
        body.Add(0x01); // compression_methods length
        body.Add(0x00); // null compression

        body.AddRange(U16(extensions.Count)); // extensions length
        body.AddRange(extensions);

        return body;
    }

    private static byte[] BuildHelloBodyAsRecord(List<byte> helloBody)
    {
        var handshake = new List<byte> { 0x01 }; // ClientHello
        handshake.AddRange(U24(helloBody.Count));
        handshake.AddRange(helloBody);

        var record = new List<byte> { 0x16, 0x03, 0x01 }; // handshake, version 3.1
        record.AddRange(U16(handshake.Count));
        record.AddRange(handshake);

        return record.ToArray();
    }

    private static IEnumerable<byte> U16(int value) => [(byte)(value >> 8), (byte)value];

    private static IEnumerable<byte> U24(int value) => [(byte)(value >> 16), (byte)(value >> 8), (byte)value];
}
