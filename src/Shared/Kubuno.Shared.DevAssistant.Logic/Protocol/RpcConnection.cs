using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Kubuno.Shared.DevAssistant.Logic.Protocol
{
    /// <summary>
    /// A duplex JSON-RPC endpoint over a line reader and writer: both sides of the channel use it (the VSIX over the
    /// child's stdout/stdin, the host over its own stdin/stdout). Requests in both directions run concurrently; each
    /// incoming request is handled on the thread pool and answered when its handler completes. A request can be
    /// cancelled by its caller (<see cref="InvokeAsync"/>'s token): the pending call fails with
    /// <see cref="OperationCanceledException"/> and the <see cref="DevAssistantMethods.CancelRequest"/> notification
    /// tells the other side.
    /// </summary>
    public sealed class RpcConnection : IDisposable
    {
        private readonly TextReader _reader;
        private readonly TextWriter _writer;
        private readonly Func<RpcMessage, CancellationToken, Task<object?>> _requestHandler;
        private readonly Action<RpcMessage> _notificationHandler;
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<long, TaskCompletionSource<RpcMessage>> _pending = new ConcurrentDictionary<long, TaskCompletionSource<RpcMessage>>();
        private readonly ConcurrentDictionary<long, CancellationTokenSource> _incoming = new ConcurrentDictionary<long, CancellationTokenSource>();
        private readonly CancellationTokenSource _disposed = new CancellationTokenSource();
        private long _nextId;
        private Task? _readLoop;

        /// <param name="reader">The incoming lines.</param>
        /// <param name="writer">The outgoing lines.</param>
        /// <param name="requestHandler">Answers an incoming request: the returned object is the result; an exception becomes an error response.</param>
        /// <param name="notificationHandler">Receives incoming notifications (on the reading thread: keep it short).</param>
        public RpcConnection(TextReader reader, TextWriter writer, Func<RpcMessage, CancellationToken, Task<object?>> requestHandler, Action<RpcMessage> notificationHandler)
        {
            _reader = reader;
            _writer = writer;
            _requestHandler = requestHandler;
            _notificationHandler = notificationHandler;
        }

        /// <summary>Raised once when the other side closes the channel (end of stream or a read error).</summary>
        public event EventHandler? Closed;

        /// <summary>Called with each raw line sent or received (already redacted by the caller's choice), for diagnostics.</summary>
        public Action<string, string>? Trace { get; set; }

        /// <summary>The task completing when the reading loop ends.</summary>
        public Task Completion => _readLoop ?? Task.CompletedTask;

        public void Start() => _readLoop = Task.Run(ReadLoopAsync);

        /// <summary>Sends a request and waits for its response.</summary>
        public async Task<RpcMessage> InvokeAsync(string method, object? parameters, CancellationToken cancellationToken)
        {
            var id = Interlocked.Increment(ref _nextId);
            var completion = new TaskCompletionSource<RpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = completion;
            try
            {
                await WriteAsync(RpcMessage.Request(id, method, parameters)).ConfigureAwait(false);
                using (cancellationToken.Register(() =>
                {
                    if (_pending.TryRemove(id, out var pending))
                    {
                        pending.TrySetCanceled();
                        _ = NotifyAsync(DevAssistantMethods.CancelRequest, new CancelRequestParams { Id = id });
                    }
                }))
                using (_disposed.Token.Register(() => completion.TrySetException(new IOException("The Dev Assistant channel is closed."))))
                {
                    return await completion.Task.ConfigureAwait(false);
                }
            }
            finally
            {
                _pending.TryRemove(id, out _);
            }
        }

        /// <summary>Sends a notification (fire and forget; write errors are swallowed once the channel is closed).</summary>
        public async Task NotifyAsync(string method, object? parameters)
        {
            try
            {
                await WriteAsync(RpcMessage.Notification(method, parameters)).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
            }
        }

        private async Task WriteAsync(RpcMessage message)
        {
            var line = RpcCodec.Serialize(message);
            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await _writer.WriteLineAsync(line).ConfigureAwait(false);
                await _writer.FlushAsync().ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }

            Trace?.Invoke("send", line);
        }

        private async Task ReadLoopAsync()
        {
            try
            {
                while (!_disposed.IsCancellationRequested)
                {
                    var line = await _reader.ReadLineAsync().ConfigureAwait(false);
                    if (line is null)
                    {
                        break;
                    }

                    if (line.Length == 0)
                    {
                        continue;
                    }

                    Trace?.Invoke("receive", line);
                    RpcMessage message;
                    try
                    {
                        message = RpcCodec.Deserialize(line);
                    }
                    catch (JsonException)
                    {
                        continue;
                    }

                    Dispatch(message);
                }
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
            }
            finally
            {
                foreach (var pending in _pending.Values)
                {
                    pending.TrySetException(new IOException("The Dev Assistant channel was closed."));
                }

                Closed?.Invoke(this, EventArgs.Empty);
            }
        }

        private void Dispatch(RpcMessage message)
        {
            if (message.IsResponse)
            {
                if (_pending.TryRemove(message.Id!.Value, out var completion))
                {
                    completion.TrySetResult(message);
                }

                return;
            }

            if (message.IsNotification)
            {
                if (message.Method == DevAssistantMethods.CancelRequest)
                {
                    var id = message.ParamsAs<CancelRequestParams>().Id;
                    if (_incoming.TryGetValue(id, out var source))
                    {
                        source.Cancel();
                    }

                    return;
                }

                try
                {
                    _notificationHandler(message);
                }
                catch (Exception)
                {
                    // A faulty notification handler must not stop the channel.
                }

                return;
            }

            if (message.IsRequest)
            {
                _ = Task.Run(() => HandleRequestAsync(message));
            }
        }

        private async Task HandleRequestAsync(RpcMessage request)
        {
            var id = request.Id!.Value;
            using var source = CancellationTokenSource.CreateLinkedTokenSource(_disposed.Token);
            _incoming[id] = source;
            RpcMessage response;
            try
            {
                var result = await _requestHandler(request, source.Token).ConfigureAwait(false);
                response = RpcMessage.Response(id, result);
            }
            catch (OperationCanceledException)
            {
                response = RpcMessage.Failure(id, RpcErrorCodes.Cancelled, "Cancelled.");
            }
            catch (RpcException exception)
            {
                response = RpcMessage.Failure(id, exception.Code, exception.Message);
            }
            catch (Exception exception)
            {
                response = RpcMessage.Failure(id, RpcErrorCodes.InternalError, exception.Message);
            }
            finally
            {
                _incoming.TryRemove(id, out _);
            }

            try
            {
                await WriteAsync(response).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
            }
        }

        public void Dispose()
        {
            if (_disposed.IsCancellationRequested)
            {
                return;
            }

            _disposed.Cancel();
            foreach (var source in _incoming.Values)
            {
                try
                {
                    source.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }
    }
}
