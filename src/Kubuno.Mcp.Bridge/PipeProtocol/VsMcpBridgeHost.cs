using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Mcp.Bridge.Contracts;
using Kubuno.Mcp.Bridge.Discovery;

namespace Kubuno.Mcp.Bridge.PipeProtocol
{
    /// <summary>
    /// The bridge's server side: accepts local named-pipe connections from Kubuno.Mcp, frames and
    /// dispatches each request via <see cref="BridgeDispatcher"/>, and writes/removes this VS
    /// instance's discovery file. Meant to be started once by the VSIX package at load and
    /// disposed at unload (see docs/MCP.md "Integration").
    /// </summary>
    /// <remarks>
    /// Accepts multiple sequential and concurrent client connections (each on its own accept-loop
    /// iteration), since more than one Claude Code session, or a reconnecting Kubuno.Mcp process,
    /// may talk to the same VS instance over its lifetime.
    /// </remarks>
    public sealed class VsMcpBridgeHost : IDisposable
    {
        private readonly BridgeDispatcher _dispatcher;
        private readonly int _pid;
        private readonly string _pipeName;
        private readonly CancellationTokenSource _stopCts = new CancellationTokenSource();
        private Task? _acceptLoop;
        private bool _discoveryFileWritten;

        public VsMcpBridgeHost(IVsContextProvider provider, int? pid = null, string? pipeName = null)
        {
            _dispatcher = new BridgeDispatcher(provider ?? throw new ArgumentNullException(nameof(provider)));
            _pid = pid ?? Process.GetCurrentProcess().Id;
            _pipeName = pipeName ?? $"KubunoVsMcp.{_pid}.{Guid.NewGuid():N}";
        }

        /// <summary>The local pipe name clients must connect to (no leading <c>\\.\pipe\</c>).</summary>
        public string PipeName => _pipeName;

        /// <summary>
        /// Writes the discovery file and starts accepting connections in the background. Safe to
        /// call once; call <see cref="Dispose"/> to stop.
        /// </summary>
        public void Start(string? visualStudioVersion = null, string? solutionOrFolderPath = null)
        {
            if (_acceptLoop is not null)
            {
                throw new InvalidOperationException("The bridge host is already started.");
            }

            BridgeDiscoveryFile.Write(new BridgeDiscoveryInfo
            {
                Pid = _pid,
                PipeName = _pipeName,
                ProcessName = "devenv",
                StartedAtUtc = DateTimeOffset.UtcNow,
                VisualStudioVersion = visualStudioVersion,
                SolutionOrFolderPath = solutionOrFolderPath,
            });
            _discoveryFileWritten = true;

            _acceptLoop = Task.Run(() => AcceptLoopAsync(_stopCts.Token));
        }

        private async Task AcceptLoopAsync(CancellationToken stopToken)
        {
            while (!stopToken.IsCancellationRequested)
            {
                NamedPipeServerStream pipe;
                try
                {
                    pipe = new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);
                }
                catch (IOException)
                {
                    // All instances busy for a moment; retry rather than let the accept loop die.
                    await Task.Delay(50, stopToken).ConfigureAwait(false);
                    continue;
                }

                try
                {
                    await pipe.WaitForConnectionAsync(stopToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    pipe.Dispose();
                    break;
                }
                catch (IOException)
                {
                    pipe.Dispose();
                    continue;
                }

                // Fire-and-forget: each client gets its own request/response loop so one slow or
                // misbehaving client never blocks new connections. Exceptions are handled inside
                // HandleClientAsync; nothing here can observe a faulted task, which is fine since
                // failures are per-connection and already logged/reported to that client.
                _ = HandleClientAsync(pipe, stopToken);
            }
        }

        private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken stopToken)
        {
            try
            {
                while (!stopToken.IsCancellationRequested)
                {
                    string? requestJson = await PipeMessageFraming.ReadMessageAsync(pipe, stopToken).ConfigureAwait(false);
                    if (requestJson is null)
                    {
                        break; // Client disconnected cleanly.
                    }

                    BridgeResponse response;
                    try
                    {
                        BridgeRequest? request = JsonSerializer.Deserialize<BridgeRequest>(requestJson);
                        response = request is null
                            ? BridgeResponse.Fail(string.Empty, "bad_request", "Request body did not parse as a bridge request.")
                            : await _dispatcher.DispatchAsync(request, stopToken).ConfigureAwait(false);
                    }
                    catch (JsonException ex)
                    {
                        response = BridgeResponse.Fail(string.Empty, "bad_request", $"Malformed request JSON: {ex.Message}");
                    }

                    string responseJson = JsonSerializer.Serialize(response);
                    await PipeMessageFraming.WriteMessageAsync(pipe, responseJson, stopToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
                // Client vanished mid-message (VS or Kubuno.Mcp process died) - not the host's problem.
            }
            finally
            {
                pipe.Dispose();
            }
        }

        public void Dispose()
        {
            _stopCts.Cancel();
            try
            {
                _acceptLoop?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
            }

            if (_discoveryFileWritten)
            {
                BridgeDiscoveryFile.Delete(_pid);
            }

            _stopCts.Dispose();
        }
    }
}
