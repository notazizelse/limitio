using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using LimitIO.Core.Ipc;
using Microsoft.Extensions.Logging;

namespace LimitIO.Service.Ipc;

/// <summary>
/// Accepts local named-pipe connections and dispatches each request to <see cref="RequestRouter"/>. The
/// pipe itself is left open to any authenticated local user - see docs/ARCHITECTURE.md: the actual
/// password gate lives in <see cref="RequestRouter"/>/<see cref="AuthSessionManager"/>, not at the pipe
/// ACL layer, because a standard user must still be able to reach the unauthenticated GetStatus/RequestGrace
/// messages without a password.
/// </summary>
public sealed class NamedPipeServer
{
    public const string PipeName = PipeNames.LimitIo;

    private readonly RequestRouter _router;
    private readonly ILogger<NamedPipeServer> _logger;

    public NamedPipeServer(RequestRouter router, ILogger<NamedPipeServer> logger)
    {
        _router = router;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = CreatePipeStream();
                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                // Handle this client to completion before accepting the next one - message volume on
                // this pipe is tiny (a handful of UI-driven requests) so a simple sequential accept loop
                // is plenty; no need for the complexity of a connection-per-task pool.
                await HandleClientAsync(pipe, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Named pipe server loop error; recreating pipe.");
            }
            finally
            {
                pipe?.Dispose();
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        try
        {
            while (pipe.IsConnected)
            {
                var request = await PipeMessageTransport.ReadMessageAsync<IpcRequest>(pipe, ct).ConfigureAwait(false);
                if (request is null)
                {
                    break;
                }

                var response = _router.Handle(request);
                await PipeMessageTransport.WriteMessageAsync(pipe, response, ct).ConfigureAwait(false);
            }
        }
        catch (IOException)
        {
            // Client disconnected mid-message - not an error, just end this session.
        }
    }

    private static NamedPipeServerStream CreatePipeStream()
    {
        var security = new PipeSecurity();
        // Any authenticated local user may connect (needed for the no-password GetStatus/RequestGrace
        // messages); Administrators get full control for diagnostics. Anonymous/guest access is not granted.
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 4096,
            outBufferSize: 4096,
            pipeSecurity: security);
    }
}
