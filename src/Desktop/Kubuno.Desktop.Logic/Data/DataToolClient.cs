using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Kubuno.Desktop.Logic.Data
{
    /// <summary>The raw request/response API of <c>kubuno-data-tool</c> (what <see cref="DataToolService"/> is built on; tests fake it).</summary>
    public interface IDataToolClient
    {
        /// <summary>
        /// Sends <paramref name="method"/> and returns its <c>result</c>. Throws <see cref="DataToolException"/> for an error
        /// answer (or when the helper is unavailable / exits meanwhile), <see cref="OperationCanceledException"/> when
        /// <paramref name="cancellationToken"/> fires (a <c>cancel</c> request is sent to the helper).
        /// </summary>
        Task<JsonElement> SendAsync(string method, JsonObject? parameters, CancellationToken cancellationToken);
    }

    /// <summary>
    /// The client of the long-lived <c>kubuno-data-tool --stdio</c> helper (JSON lines, docs/DATA.md §9): starts it lazily
    /// on the first request, numbers the requests, matches the responses by id (requests run concurrently in the helper),
    /// turns a <see cref="CancellationToken"/> into a <c>cancel</c> request, and restarts the helper on the next request
    /// after it crashed (the requests in flight fail with <see cref="DataToolErrorKinds.Closed"/>). Thread-safe; no UI
    /// thread affinity. Logging goes through <see cref="Log"/>: the helper's stderr always, the requests (method and
    /// redacted parameters, <see cref="DataToolRedaction"/>) only when <see cref="TraceRequests"/> says so.
    /// </summary>
    public sealed class DataToolClient : IDataToolClient, IDisposable
    {
        private readonly IDataToolLauncher _launcher;
        private readonly object _sync = new object();
        private readonly Dictionary<long, Pending> _pending = new Dictionary<long, Pending>();
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private IDataToolConnection? _connection;
        private long _nextId;
        private bool _disposed;
        private int _starts;

        public DataToolClient(IDataToolLauncher launcher, Action<string>? log = null)
        {
            _launcher = launcher;
            Log = log;
        }

        /// <summary>Where diagnostics go (the "Kubuno" Output pane in Visual Studio). Never receives a secret.</summary>
        public Action<string>? Log { get; set; }

        /// <summary>Whether each request and response is logged (debug level); read on every request.</summary>
        public Func<bool>? TraceRequests { get; set; }

        /// <summary>How many times the helper process has been started (tests, diagnostics).</summary>
        public int StartCount
        {
            get
            {
                lock (_sync)
                {
                    return _starts;
                }
            }
        }

        /// <summary>Whether a helper process is currently running.</summary>
        public bool IsRunning
        {
            get
            {
                lock (_sync)
                {
                    return _connection != null;
                }
            }
        }

        public async Task<JsonElement> SendAsync(string method, JsonObject? parameters, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(method))
            {
                throw new ArgumentException("A method is required.", nameof(method));
            }

            cancellationToken.ThrowIfCancellationRequested();
            bool trace = TraceRequests?.Invoke() == true;

            // A write that fails because the helper died between two requests is retried once on a fresh process.
            for (int attempt = 0; ; attempt++)
            {
                IDataToolConnection connection = EnsureStarted();
                long id = Interlocked.Increment(ref _nextId);
                var pending = new Pending(method, connection);
                lock (_sync)
                {
                    _pending[id] = pending;
                }

                if (trace)
                {
                    Log?.Invoke($"kubuno-data-tool → #{id} {method} {DataToolRedaction.ForLog(method, parameters)}");
                }

                var request = new JsonObject
                {
                    ["id"] = id,
                    ["method"] = method,
                    ["params"] = parameters?.DeepClone() ?? new JsonObject(),
                };

                try
                {
                    await WriteAsync(connection, request.ToJsonString()).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException || exception is ObjectDisposedException || exception is InvalidOperationException)
                {
                    lock (_sync)
                    {
                        _pending.Remove(id);
                    }

                    OnConnectionLost(connection);
                    if (attempt == 0 && !_disposed)
                    {
                        continue;
                    }

                    throw new DataToolException(DataToolErrorKinds.Closed, "kubuno-data-tool stopped responding.", exception);
                }

                using (cancellationToken.Register(() => Cancel(id, cancellationToken)))
                {
                    JsonElement result = await pending.Completion.Task.ConfigureAwait(false);
                    if (trace)
                    {
                        Log?.Invoke($"kubuno-data-tool ← #{id} {method} ok ({pending.Elapsed.ElapsedMilliseconds} ms)");
                    }

                    return result;
                }
            }
        }

        /// <summary>Stops the helper (EOF on its stdin) and fails the requests in flight. The next request starts a new one unless disposed.</summary>
        public void Stop()
        {
            IDataToolConnection? connection;
            lock (_sync)
            {
                connection = _connection;
            }

            if (connection != null)
            {
                OnConnectionLost(connection);
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            Stop();
        }

        private IDataToolConnection EnsureStarted()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(DataToolClient));
                }

                if (_connection != null)
                {
                    return _connection;
                }

                IDataToolConnection connection;
                try
                {
                    connection = _launcher.Start();
                }
                catch (DataToolException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new DataToolException(DataToolErrorKinds.Unavailable, "kubuno-data-tool could not be started: " + exception.Message, exception);
                }

                _connection = connection;
                _starts++;
                if (_starts > 1)
                {
                    Log?.Invoke($"kubuno-data-tool: restarted (start #{_starts}).");
                }

                _ = Task.Run(() => ReadLoopAsync(connection));
                return connection;
            }
        }

        private async Task WriteAsync(IDataToolConnection connection, string line)
        {
            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await connection.WriteLineAsync(line).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private async Task ReadLoopAsync(IDataToolConnection connection)
        {
            try
            {
                while (true)
                {
                    string? line = await connection.ReadLineAsync().ConfigureAwait(false);
                    if (line is null)
                    {
                        break;
                    }

                    if (line.Length > 0)
                    {
                        Dispatch(line);
                    }
                }
            }
            catch (Exception exception)
            {
                if (IsCurrent(connection))
                {
                    Log?.Invoke("kubuno-data-tool: reading its output failed: " + exception.Message);
                }
            }

            if (IsCurrent(connection))
            {
                Log?.Invoke("kubuno-data-tool: the helper exited; it will be restarted by the next request.");
            }

            OnConnectionLost(connection);
        }

        private bool IsCurrent(IDataToolConnection connection)
        {
            lock (_sync)
            {
                return !_disposed && ReferenceEquals(_connection, connection);
            }
        }

        private void Dispatch(string line)
        {
            long id;
            JsonElement? result = null;
            DataToolException? error = null;
            try
            {
                using var document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out id))
                {
                    Log?.Invoke("kubuno-data-tool: ignored an output line without a request id.");
                    return;
                }

                if (root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.Object)
                {
                    string kind = errorElement.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString()! : DataToolErrorKinds.Protocol;
                    string message = errorElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString()! : kind;
                    error = new DataToolException(kind, message);
                }
                else if (root.TryGetProperty("result", out var resultElement))
                {
                    result = resultElement.Clone();
                }
                else
                {
                    error = new DataToolException(DataToolErrorKinds.Protocol, "kubuno-data-tool answered without a result.");
                }
            }
            catch (JsonException)
            {
                Log?.Invoke("kubuno-data-tool: ignored an output line that is not JSON.");
                return;
            }

            Pending? pending;
            lock (_sync)
            {
                if (_pending.TryGetValue(id, out pending))
                {
                    _pending.Remove(id);
                }
            }

            if (pending is null)
            {
                // A request cancelled meanwhile, or the answer to a `cancel` request.
                return;
            }

            if (error != null)
            {
                if (TraceRequests?.Invoke() == true)
                {
                    Log?.Invoke($"kubuno-data-tool ← #{id} {pending.Method} error {error.Kind} ({pending.Elapsed.ElapsedMilliseconds} ms)");
                }

                if (error.Kind == DataToolErrorKinds.Cancelled)
                {
                    pending.Completion.TrySetCanceled();
                }
                else
                {
                    pending.Completion.TrySetException(error);
                }
            }
            else
            {
                pending.Completion.TrySetResult(result!.Value);
            }
        }

        private void Cancel(long id, CancellationToken token)
        {
            Pending? pending;
            IDataToolConnection? connection;
            lock (_sync)
            {
                if (!_pending.TryGetValue(id, out pending))
                {
                    return;
                }

                _pending.Remove(id);
                connection = _connection;
            }

            pending.Completion.TrySetCanceled(token);
            if (connection is null || !ReferenceEquals(connection, pending.Connection))
            {
                return;
            }

            long cancelId = Interlocked.Increment(ref _nextId);
            var request = new JsonObject
            {
                ["id"] = cancelId,
                ["method"] = "cancel",
                ["params"] = new JsonObject { ["id"] = id },
            };
            _ = SendCancelAsync(connection, request.ToJsonString(), id);
        }

        private async Task SendCancelAsync(IDataToolConnection connection, string line, long id)
        {
            try
            {
                await WriteAsync(connection, line).ConfigureAwait(false);
                if (TraceRequests?.Invoke() == true)
                {
                    Log?.Invoke($"kubuno-data-tool → cancel #{id}");
                }
            }
            catch (Exception)
            {
                // The helper is gone: nothing left to cancel.
            }
        }

        private void OnConnectionLost(IDataToolConnection connection)
        {
            List<Pending> failed = new List<Pending>();
            bool current;
            lock (_sync)
            {
                current = ReferenceEquals(_connection, connection);
                if (current)
                {
                    _connection = null;
                }

                foreach (var entry in new List<KeyValuePair<long, Pending>>(_pending))
                {
                    if (ReferenceEquals(entry.Value.Connection, connection))
                    {
                        failed.Add(entry.Value);
                        _pending.Remove(entry.Key);
                    }
                }
            }

            foreach (var pending in failed)
            {
                pending.Completion.TrySetException(new DataToolException(DataToolErrorKinds.Closed, "kubuno-data-tool exited before answering."));
            }

            if (current)
            {
                try
                {
                    connection.Shutdown();
                }
                catch (Exception)
                {
                    // Already gone.
                }

                connection.Dispose();
            }
        }

        private sealed class Pending
        {
            public Pending(string method, IDataToolConnection connection)
            {
                Method = method;
                Connection = connection;
            }

            public string Method { get; }

            public IDataToolConnection Connection { get; }

            public Stopwatch Elapsed { get; } = Stopwatch.StartNew();

            public TaskCompletionSource<JsonElement> Completion { get; } = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
