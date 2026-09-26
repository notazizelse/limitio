using System.IO.Pipes;
using System.Text.Json;
using LimitIO.Core.Ipc;

namespace LimitIO.UI.Ipc;

/// <summary>
/// Thin async wrapper around the named-pipe connection to LimitIO.Service. One method per IPC request
/// type - see docs/ARCHITECTURE.md section 6 for the protocol. Reconnects transparently on the next call
/// if the pipe drops (e.g. the service restarted after a crash).
/// </summary>
public sealed class ServiceClient : IAsyncDisposable
{
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
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureConnectedAsync(ct).ConfigureAwait(false);

            var request = new IpcRequest
            {
                RequestId = Guid.NewGuid(),
                ProtocolVersion = ProtocolVersion.Current,
                Type = type,
                SessionToken = sessionToken,
                PayloadJson = payload is null ? null : JsonSerializer.Serialize(payload, IpcJson.Options),
            };

            await PipeMessageTransport.WriteMessageAsync(_pipe!, request, ct).ConfigureAwait(false);
            var response = await PipeMessageTransport.ReadMessageAsync<IpcResponse>(_pipe!, ct).ConfigureAwait(false);

            if (response is null)
            {
                DropConnection();
                return IpcResponse.Failure(request.RequestId, "The LimitIO service closed the connection unexpectedly.");
            }

            return response;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        {
            DropConnection();
            return IpcResponse.Failure(Guid.Empty, "Couldn't reach the LimitIO service. It may not be running - try reinstalling LimitIO.");
        }
        finally
        {
            _lock.Release();
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
