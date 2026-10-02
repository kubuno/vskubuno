using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Shared.DevAssistant.Logic.Protocol;
using Kubuno.Shared.Logging;

namespace Kubuno.Shared.DevAssistant.Host
{
    /// <summary>
    /// Starts <c>kubuno-dev-assistant.exe</c> on first use and talks to it (docs/AI-ASSISTANT.md section 9.1): JSON-RPC
    /// lines on the child's stdio. Restarted after a crash on the next request (the conversation lives in the VSIX and is
    /// resent whole, so nothing is lost). The child gets no argument and no ANTHROPIC_* variable: it reads the key itself
    /// from the Credential Manager, and an environment key is never used.
    /// </summary>
    public sealed class DevAssistantHostClient : IDisposable
    {
        /// <summary>Overrides the host executable (development builds).</summary>
        public const string HostPathVariable = "KUBUNO_DEV_ASSISTANT_HOST";

        private readonly SemaphoreSlim _startLock = new SemaphoreSlim(1, 1);
        private readonly Func<RpcMessage, CancellationToken, Task<object?>> _requestHandler;
        private readonly Action<RpcMessage> _notificationHandler;
        private Process? _process;
        private RpcConnection? _connection;
        private bool _disposed;

        public DevAssistantHostClient(Func<RpcMessage, CancellationToken, Task<object?>> requestHandler, Action<RpcMessage> notificationHandler)
        {
            _requestHandler = requestHandler;
            _notificationHandler = notificationHandler;
        }

        /// <summary>The fake provider's fixtures folder to announce at initialize (null: the bundled ones).</summary>
        public string? FixturesDirectory { get; set; }

        /// <summary>The host executable: the override variable, else the VSIX's <c>tools\kubuno-dev-assistant\</c>.</summary>
        public static string? LocateHost()
        {
            var overridePath = Environment.GetEnvironmentVariable(HostPathVariable);
            if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath))
            {
                return overridePath;
            }

            return KubunoExtension.FindBundledTool(Path.Combine("kubuno-dev-assistant", "kubuno-dev-assistant.exe"));
        }

        public async Task<RpcMessage> InvokeAsync(string method, object? parameters, CancellationToken cancellationToken)
        {
            var connection = await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
            return await connection.InvokeAsync(method, parameters, cancellationToken).ConfigureAwait(false);
        }

        private async Task<RpcConnection> EnsureStartedAsync(CancellationToken cancellationToken)
        {
            await _startLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(DevAssistantHostClient));
                }

                if (_connection is not null && _process is { HasExited: false })
                {
                    return _connection;
                }

                var path = LocateHost() ?? throw new FileNotFoundException("kubuno-dev-assistant.exe was not found in the extension's tools\\kubuno-dev-assistant folder.");
                var startInfo = new ProcessStartInfo
                {
                    FileName = path,
                    WorkingDirectory = Path.GetDirectoryName(path),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = new UTF8Encoding(false),
                    StandardErrorEncoding = new UTF8Encoding(false),
                };

                // Never let a key or an endpoint leak in through the environment (docs/AI-ASSISTANT.md section 8.1).
                foreach (var name in new[] { "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "ANTHROPIC_BASE_URL", "ANTHROPIC_PROFILE", "ANTHROPIC_IDENTITY_TOKEN", "ANTHROPIC_IDENTITY_TOKEN_FILE", "ANTHROPIC_FEDERATION_RULE_ID" })
                {
                    startInfo.EnvironmentVariables.Remove(name);
                }

                var process = Process.Start(startInfo) ?? throw new IOException("kubuno-dev-assistant.exe did not start.");
                process.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        KubunoLog.WriteLine("[dev-assistant host] " + e.Data);
                    }
                };
                process.BeginErrorReadLine();

                var input = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = false };
                var connection = new RpcConnection(process.StandardOutput, input, _requestHandler, _notificationHandler);
                connection.Closed += (_, _) => KubunoLog.WriteLine("Kubuno Dev Assistant: host process channel closed.");
                connection.Start();

                var initialize = await connection.InvokeAsync(DevAssistantMethods.Initialize, new InitializeParams { FixturesDirectory = FixturesDirectory }, cancellationToken).ConfigureAwait(false);
                var result = initialize.ResultAs<InitializeResult>();
                if (result.ProtocolVersion != DevAssistantMethods.ProtocolVersion)
                {
                    connection.Dispose();
                    TryKill(process);
                    throw new InvalidOperationException($"kubuno-dev-assistant.exe speaks protocol {result.ProtocolVersion}, this extension {DevAssistantMethods.ProtocolVersion}.");
                }

                KubunoLog.WriteLine($"Kubuno Dev Assistant: host started (PID {process.Id}, version {result.HostVersion}).");
                _connection?.Dispose();
                _process = process;
                _connection = connection;
                return connection;
            }
            finally
            {
                _startLock.Release();
            }
        }

        public void Dispose()
        {
            _disposed = true;
            var connection = _connection;
            var process = _process;
            _connection = null;
            _process = null;
            // Closing the host's stdin ends it (its reading loop sees the end of the stream).
            connection?.Dispose();

            if (process is not null)
            {
                try
                {
                    process.StandardInput.Close();
                }
                catch (Exception exception) when (exception is IOException or InvalidOperationException)
                {
                }

                if (!process.WaitForExit(1000))
                {
                    TryKill(process);
                }

                process.Dispose();
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
        }
    }
}
