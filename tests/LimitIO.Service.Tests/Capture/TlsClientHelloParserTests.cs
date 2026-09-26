using LimitIO.Service.Capture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LimitIO.Service.Tests.Capture;

[TestClass]
public class TlsClientHelloParserTests
{
    [TestMethod]
    public void TryParseServerName_WellFormedClientHello_ExtractsHostname()
    {
        var data = TlsTestFixtures.BuildClientHelloWithSni("example.com");

        var result = TlsClientHelloParser.TryParseServerName(data, out var serverName);

        Assert.IsTrue(result);
        Assert.AreEqual("example.com", serverName);
    }

    [TestMethod]
    public void TryParseServerName_NoExtensions_ReturnsFalse()
    {
        var data = TlsTestFixtures.BuildClientHelloWithNoExtensions();

        var result = TlsClientHelloParser.TryParseServerName(data, out var serverName);

        Assert.IsFalse(result);
        Assert.IsNull(serverName);
    }

    [TestMethod]
    public void TryParseServerName_TruncatedRecord_ReturnsFalseRatherThanThrowing()
    {
        var full = TlsTestFixtures.BuildClientHelloWithSni("example.com");
        var truncated = full[..(full.Length / 2)];

        var result = TlsClientHelloParser.TryParseServerName(truncated, out var serverName);

        Assert.IsFalse(result);
        Assert.IsNull(serverName);
    }

    [TestMethod]
    public void TryParseServerName_NotAHandshakeRecord_ReturnsFalse()
    {
        // Content type 0x17 = application data, not 0x16 = handshake.
        byte[] data = [0x17, 0x03, 0x03, 0x00, 0x05, 1, 2, 3, 4, 5];

        var result = TlsClientHelloParser.TryParseServerName(data, out var serverName);

        Assert.IsFalse(result);
        Assert.IsNull(serverName);
    }

    [TestMethod]
    public void TryParseServerName_EmptyInput_ReturnsFalse()
    {
        var result = TlsClientHelloParser.TryParseServerName([], out var serverName);

        Assert.IsFalse(result);
        Assert.IsNull(serverName);
    }

    [TestMethod]
    public void TryParseServerName_GarbageInput_NeverThrows()
    {
        var random = new Random(42);
        var buffer = new byte[512];
        random.NextBytes(buffer);

        // Should not throw regardless of what garbage arrives - fail-safe-open is the whole point.
        var result = TlsClientHelloParser.TryParseServerName(buffer, out var serverName);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void TryParseServerName_LongHostname_ExtractsCorrectly()
    {
        var host = "a-very-long-subdomain-name.example-with-many-characters.co.uk";
        var data = TlsTestFixtures.BuildClientHelloWithSni(host);

        var result = TlsClientHelloParser.TryParseServerName(data, out var serverName);

        Assert.IsTrue(result);
        Assert.AreEqual(host, serverName);
    }
}
