using System.Text;
using LimitIO.Service.Capture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LimitIO.Service.Tests.Capture;

[TestClass]
public class HttpHostHeaderParserTests
{
    [TestMethod]
    public void TryParseHost_SimpleGetRequest_ExtractsHost()
    {
        var request = "GET /path HTTP/1.1\r\nHost: example.com\r\nUser-Agent: test\r\n\r\n";
        var data = Encoding.ASCII.GetBytes(request);

        var result = HttpHostHeaderParser.TryParseHost(data, out var host);

        Assert.IsTrue(result);
        Assert.AreEqual("example.com", host);
    }

    [TestMethod]
    public void TryParseHost_HostHeaderWithPort_StripsPort()
    {
        var request = "GET / HTTP/1.1\r\nHost: example.com:8080\r\n\r\n";
        var data = Encoding.ASCII.GetBytes(request);

        var result = HttpHostHeaderParser.TryParseHost(data, out var host);

        Assert.IsTrue(result);
        Assert.AreEqual("example.com", host);
    }

    [TestMethod]
    public void TryParseHost_NoHostHeader_ReturnsFalse()
    {
        var request = "GET / HTTP/1.0\r\nUser-Agent: test\r\n\r\n";
        var data = Encoding.ASCII.GetBytes(request);

        var result = HttpHostHeaderParser.TryParseHost(data, out var host);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void TryParseHost_TlsBytes_NeverMisfiresAsHttp()
    {
        var data = TlsTestFixtures.BuildClientHelloWithSni("example.com");

        var result = HttpHostHeaderParser.TryParseHost(data, out var host);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void TryParseHost_EmptyInput_ReturnsFalse()
    {
        var result = HttpHostHeaderParser.TryParseHost([], out var host);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void TryParseHost_CaseInsensitiveHeaderName_StillMatches()
    {
        var request = "GET / HTTP/1.1\r\nhost: example.com\r\n\r\n";
        var data = Encoding.ASCII.GetBytes(request);

        var result = HttpHostHeaderParser.TryParseHost(data, out var host);

        Assert.IsTrue(result);
        Assert.AreEqual("example.com", host);
    }
}
