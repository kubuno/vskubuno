using System;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Core.Mcp.Bridge.Contracts;

namespace Kubuno.Core.Mcp.Bridge.PipeProtocol
{
    /// <summary>
    /// The pipe-client half used by Kubuno.Core.Mcp to reach a running <see cref="VsMcpBridgeHost"/>.
    /// One call, one short-lived connection: simple and robust to the bridge restarting between
    /// calls (e.g. the developer reloads the VS extension), at the cost of a pipe handshake per
    /// tool call. A future optimization could keep a persistent connection per discovered pid and
    /// only reconnect on failure - not needed for this skeleton (see docs/MCP.md "Known limits").
    /// </summary>
    public static class VsMcpBridgeClient
    {
        public static async Task<BridgeResponse> SendRequestAsync(
            string pipeName,
            BridgeRequest request,
            TimeSpan connectTimeout,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(pipeName)) throw new ArgumentException("Pipe name is required.", nameof(pipeName));
            if (request is null) throw new ArgumentNullException(nameof(request));

            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            try
            {
                await pipe.ConnectAsync((int)connectTimeout.TotalMilliseconds, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                throw new BridgeUnavailableException(
                    $"Timed out connecting to the Kubuno Visual Studio bridge on pipe '{pipeName}' after {connectTimeout}.", ex);
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                throw new BridgeUnavailableException(
                    $"Could not connect to the Kubuno Visual Studio bridge on pipe '{pipeName}': {ex.Message}", ex);
            }

            string requestJson = JsonSerializer.Serialize(request);
            await PipeMessageFraming.WriteMessageAsync(pipe, requestJson, cancellationToken).ConfigureAwait(false);

            string? responseJson = await PipeMessageFraming.ReadMessageAsync(pipe, cancellationToken).ConfigureAwait(false);
            if (responseJson is null)
            {
                throw new BridgeUnavailableException(
                    $"The Kubuno Visual Studio bridge on pipe '{pipeName}' closed the connection without a response.");
            }

            BridgeResponse? response = JsonSerializer.Deserialize<BridgeResponse>(responseJson);
            if (response is null)
            {
                throw new BridgeUnavailableException(
                    $"The Kubuno Visual Studio bridge on pipe '{pipeName}' returned an unparseable response.");
            }

            return response;
        }
    }
}
