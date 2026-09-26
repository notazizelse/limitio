using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LimitIO.Core.Ipc;
using LimitIO.UI.Diagnostics;

namespace LimitIO.UI.Ipc;

/// <summary>
/// Thin async wrapper around the named-pipe connection to LimitIO.Service. One method per IPC request
/// type - see docs/ARCHITECTURE.md section 6 for the protocol. Reconnects transparently on the next call
/// if the pipe drops (e.g. the service restarted after a crash).
/// </summary>
public sealed class ServiceClient : IAsyncDisposable
{
    // Every IPC round-trip is capped at this - without a ceiling, a single hung read (the Service is
    // stuck, or the pipe silently died in a way that doesn't throw) would wait forever while holding the
    // one-at-a-time lock below, which would then jam every future call from anywhere in the UI - the
    // tray menu, the periodic refresh timer, every button - permanently, with no way to recover short of
    // restarting the app. A firm timeout turns that into "this one call fails, everything else keeps working."
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(8);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private NamedPipeClientStream? _pipe;

    public async Task<StatusResponseDto?> GetStatusAsync(CancellationToken ct = default)
    {
        var response = await SendAsync(IpcRequestType.GetStatus, payload: null, sessionToken: null, ct).ConfigureAwait(false);
        return response.Ok ? Deserialize<StatusResponseDto>(response.PayloadJson) : null;
    }

    public async Task<VerifyPasswordResponseDto> VerifyPasswordAsync(string password, CancellationToken ct = default)
    {
        var response = await SendAsync(IpcRequestType.VerifyPassword, new VerifyPasswordRequestDto(password), null, ct).ConfigureAwait(false);
        return response.Ok
            ? Deserialize<VerifyPasswordResponseDto>(response.PayloadJson) ?? new VerifyPasswordResponseDto(false, null, null)
            : new VerifyPasswordResponseDto(false, null, null);
    }

    public async Task<bool> SetInitialPasswordAsync(string password, CancellationToken ct = default)
    {
        var response = await SendAsync(IpcRequestType.SetInitialPassword, new SetInitialPasswordRequestDto(password), null, ct).ConfigureAwait(false);
        return response.Ok;
    }

    public async Task<(bool Success, string? Error)> ChangePasswordAsync(string oldPassword, string newPassword, string sessionToken, CancellationToken ct = default)
    {
        var response = await SendAsync(IpcRequestType.ChangePassword, new ChangePasswordRequestDto(oldPassword, newPassword), sessionToken, ct).ConfigureAwait(false);
        return (response.Ok, response.Error);
    }

    public async Task<RequestGraceResponseDto> RequestGraceAsync(Guid ruleId, CancellationToken ct = default)
    {
        var response = await SendAsync(IpcRequestType.RequestGrace, new RequestGraceRequestDto(ruleId), null, ct).ConfigureAwait(false);
        return response.Ok
            ? Deserialize<RequestGraceResponseDto>(response.PayloadJson) ?? new RequestGraceResponseDto(false, response.Error)
            : new RequestGraceResponseDto(false, response.Error ?? "Request failed.");
    }

    public async Task<(bool Success, string? Error)> UpdateRulesAsync(IReadOnlyList<RuleUpsertDto> rules, string sessionToken, CancellationToken ct = default)
    {
        var response = await SendAsync(IpcRequestType.UpdateRules, new UpdateRulesRequestDto(rules), sessionToken, ct).ConfigureAwait(false);
        return (response.Ok, response.Error);
    }

    public async Task<bool> PauseMonitoringAsync(int minutes, string sessionToken, CancellationToken ct = default)
    {
        var response = await SendAsync(IpcRequestType.PauseMonitoring, new PauseMonitoringRequestDto(minutes), sessionToken, ct).ConfigureAwait(false);
        return response.Ok;
    }

    public async Task<bool> ResumeMonitoringAsync(string sessionToken, CancellationToken ct = default)
    {
        var response = await SendAsync(IpcRequestType.ResumeMonitoring, payload: null, sessionToken, ct).ConfigureAwait(false);
        return response.Ok;
    }

    private async Task<IpcResponse> SendAsync(IpcRequestType type, object? payload, string? sessionToken, CancellationToken ct)
    {
        using var timeoutCts = new CancellationTokenSource(CallTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var linkedCt = linkedCts.Token;

        var lockAcquired = false;
        try
        {
            await _lock.WaitAsync(linkedCt).ConfigureAwait(false);
            lockAcquired = true;

            await EnsureConnectedAsync(linkedCt).ConfigureAwait(false);

            var request = new IpcRequest
            {
                RequestId = Guid.NewGuid(),
                ProtocolVersion = ProtocolVersion.Current,
                Type = type,
                SessionToken = sessionToken,
                PayloadJson = payload is null ? null : JsonSerializer.Serialize(payload, IpcJson.Options),
            };

            await PipeMessageTransport.WriteMessageAsync(_pipe!, request, linkedCt).ConfigureAwait(false);
            var response = await PipeMessageTransport.ReadMessageAsync<IpcResponse>(_pipe!, linkedCt).ConfigureAwait(false);

            if (response is null)
            {
                DropConnection();
                return IpcResponse.Failure(request.RequestId, "The LimitIO service closed the connection unexpectedly.");
            }

            return response;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Our own timeout fired, not the caller's token - drop whatever connection state might be
            // mid-read so the *next* call starts clean instead of inheriting a half-read stream.
            DropConnection();
            return IpcResponse.Failure(Guid.Empty, "The LimitIO service didn't respond in time. It may be busy or restarting - try again in a moment.");
        }
        catch (Exception ex)
        {
            // Deliberately broad: anything unexpected here (a malformed response, a pipe torn down
            // mid-read, an ObjectDisposedException from a race with DisposeAsync) must never propagate
            // out of this method - it would otherwise surface as an unhandled exception in whatever
            // fire-and-forget UI event handler called in, which is exactly how a single bad round-trip
            // used to take down the whole app.
            UiLog.Error($"IPC call {type} failed", ex);
            DropConnection();
            return IpcResponse.Failure(Guid.Empty, "Couldn't reach the LimitIO service. It may not be running - try reinstalling LimitIO.");
        }
        finally
        {
            if (lockAcquired)
            {
                _lock.Release();
            }
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_pipe is { IsConnected: true })
        {
            return;
        }

        DropConnection();

        _pipe = new NamedPipeClientStream(".", PipeNames.LimitIo, PipeDirection.InOut, PipeOptions.Asynchronous);
        await _pipe.ConnectAsync(3000, ct).ConfigureAwait(false);
    }

    private void DropConnection()
    {
        _pipe?.Dispose();
        _pipe = null;
    }

    private static T? Deserialize<T>(string? json) => json is null ? default : JsonSerializer.Deserialize<T>(json, IpcJson.Options);

    public async ValueTask DisposeAsync()
    {
        DropConnection();
        _lock.Dispose();
        await Task.CompletedTask;
    }
}
