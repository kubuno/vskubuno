using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Shared.DevAssistant.Host.Providers;
using Kubuno.Shared.DevAssistant.Logic.Protocol;

namespace Kubuno.Shared.DevAssistant.Host
{
    /// <summary>
    /// The host side of the channel: answers the VSIX's requests (initialize, models/list, session/send, session/cancel,
    /// shutdown), runs each turn in an <see cref="AgentLoop"/>, and calls back into the VSIX for every tool. One session
    /// (conversation) runs one turn at a time; <see cref="DevAssistantMethods.SessionCancel"/> stops it.
    /// </summary>
    public sealed class HostServer : IDisposable
    {
        private readonly RpcConnection _connection;
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new ConcurrentDictionary<string, CancellationTokenSource>(StringComparer.Ordinal);
        private readonly Func<string, string, IModelProvider>? _providerFactory;
        private readonly TaskCompletionSource<bool> _shutdown = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private string _fixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");

        /// <param name="providerFactory">Builds a provider from (id, fixtures folder); null for the real ones (tests inject their own).</param>
        public HostServer(TextReader input, TextWriter output, Func<string, string, IModelProvider>? providerFactory = null)
        {
            _providerFactory = providerFactory;
            _connection = new RpcConnection(input, output, HandleRequestAsync, _ => { });
            _connection.Closed += (_, _) => _shutdown.TrySetResult(true);
        }

        /// <summary>Completes when the VSIX asks to shut down or closes the channel.</summary>
        public Task Completion => _shutdown.Task;

        public void Start() => _connection.Start();

        private IModelProvider CreateProvider(string id)
        {
            if (_providerFactory is not null)
            {
                return _providerFactory(id, _fixturesDirectory);
            }

            return id switch
            {
                ProviderIds.Anthropic => new AnthropicProvider(),
                ProviderIds.Fake => new FakeProvider(_fixturesDirectory),
                _ => throw new RpcException(RpcErrorCodes.InvalidParams, "Unknown provider '" + id + "'."),
            };
        }

        private async Task<object?> HandleRequestAsync(RpcMessage request, CancellationToken cancellationToken)
        {
            switch (request.Method)
            {
                case DevAssistantMethods.Initialize:
                    var initialize = request.ParamsAs<InitializeParams>();
                    if (!string.IsNullOrEmpty(initialize.FixturesDirectory) && Directory.Exists(initialize.FixturesDirectory))
                    {
                        _fixturesDirectory = initialize.FixturesDirectory!;
                    }

                    return new InitializeResult
                    {
                        ProtocolVersion = DevAssistantMethods.ProtocolVersion,
                        HostVersion = typeof(HostServer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0",
                    };

                case DevAssistantMethods.ModelsList:
                    return await CreateProvider(request.ParamsAs<ModelsListParams>().Provider).ListModelsAsync(cancellationToken).ConfigureAwait(false);

                case DevAssistantMethods.SessionSend:
                    return await SendAsync(request.ParamsAs<SessionSendParams>(), cancellationToken).ConfigureAwait(false);

                case DevAssistantMethods.SessionCancel:
                    var sessionId = request.ParamsAs<SessionCancelParams>().SessionId;
                    if (_running.TryGetValue(sessionId, out var running))
                    {
                        running.Cancel();
                    }

                    return new object();

                case DevAssistantMethods.Shutdown:
                    foreach (var source in _running.Values)
                    {
                        source.Cancel();
                    }

                    _ = Task.Delay(100).ContinueWith(_ => _shutdown.TrySetResult(true), TaskScheduler.Default);
                    return new object();

                default:
                    throw new RpcException(RpcErrorCodes.MethodNotFound, "Unknown method '" + request.Method + "'.");
            }
        }

        private async Task<SessionSendResult> SendAsync(SessionSendParams parameters, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(parameters.SessionId))
            {
                throw new RpcException(RpcErrorCodes.InvalidParams, "sessionId is required.");
            }

            using var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (!_running.TryAdd(parameters.SessionId, source))
            {
                throw new RpcException(RpcErrorCodes.InvalidParams, "This conversation is already answering.");
            }

            try
            {
                var loop = new AgentLoop(
                    CreateProvider(parameters.Provider),
                    async (invoke, token) => (await _connection.InvokeAsync(DevAssistantMethods.ToolInvoke, invoke, token).ConfigureAwait(false)).ResultAs<ToolInvokeResult>(),
                    (method, payload) => _connection.NotifyAsync(method, payload));
                return await loop.RunAsync(parameters, source.Token).ConfigureAwait(false);
            }
            finally
            {
                _running.TryRemove(parameters.SessionId, out _);
            }
        }

        public void Dispose() => _connection.Dispose();
    }
}
