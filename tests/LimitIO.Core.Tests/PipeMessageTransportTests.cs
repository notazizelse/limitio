using LimitIO.Core.Ipc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LimitIO.Core.Tests;

[TestClass]
public class PipeMessageTransportTests
{
    private sealed record SamplePayload(string Name, int Count);

    [TestMethod]
    public async Task WriteThenRead_RoundTripsMessage()
    {
        using var stream = new MemoryStream();

        await PipeMessageTransport.WriteMessageAsync(stream, new SamplePayload("hello", 3));
        stream.Position = 0;

        var result = await PipeMessageTransport.ReadMessageAsync<SamplePayload>(stream);

        Assert.IsNotNull(result);
        Assert.AreEqual("hello", result!.Name);
        Assert.AreEqual(3, result.Count);
    }

    [TestMethod]
    public async Task WriteTwoMessages_ReadsThemInOrder()
    {
        using var stream = new MemoryStream();

        await PipeMessageTransport.WriteMessageAsync(stream, new SamplePayload("first", 1));
        await PipeMessageTransport.WriteMessageAsync(stream, new SamplePayload("second", 2));
        stream.Position = 0;

        var first = await PipeMessageTransport.ReadMessageAsync<SamplePayload>(stream);
        var second = await PipeMessageTransport.ReadMessageAsync<SamplePayload>(stream);

        Assert.AreEqual("first", first!.Name);
        Assert.AreEqual("second", second!.Name);
    }

    [TestMethod]
    public async Task ReadMessage_EmptyStream_ReturnsNull()
    {
        using var stream = new MemoryStream();

        var result = await PipeMessageTransport.ReadMessageAsync<SamplePayload>(stream);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task IpcRequestResponse_RoundTripThroughStream()
    {
        using var stream = new MemoryStream();
        var request = new IpcRequest
        {
            RequestId = Guid.NewGuid(),
            ProtocolVersion = ProtocolVersion.Current,
            Type = IpcRequestType.GetStatus,
        };

        await PipeMessageTransport.WriteMessageAsync(stream, request);
        stream.Position = 0;

        var roundTripped = await PipeMessageTransport.ReadMessageAsync<IpcRequest>(stream);

        Assert.IsNotNull(roundTripped);
        Assert.AreEqual(request.RequestId, roundTripped!.RequestId);
        Assert.AreEqual(IpcRequestType.GetStatus, roundTripped.Type);
    }
}
