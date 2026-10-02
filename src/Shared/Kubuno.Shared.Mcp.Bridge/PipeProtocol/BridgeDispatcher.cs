using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Core.Mcp.Bridge.Contracts;

namespace Kubuno.Core.Mcp.Bridge.PipeProtocol
{
    /// <summary>
    /// Routes a decoded <see cref="BridgeRequest"/> to the matching <see cref="IVsContextProvider"/>
    /// method and serializes the result (or failure) back into a <see cref="BridgeResponse"/>.
    /// </summary>
    /// <remarks>
    /// Pure request-in/response-out logic with no I/O of its own, so it is unit-testable directly
    /// against a fake <see cref="IVsContextProvider"/> without a pipe (see
    /// tests/Kubuno.Core.Mcp.Tests/BridgeDispatcherTests.cs) - the pipe transport itself is exercised
    /// separately by <see cref="VsMcpBridgeHost"/>/<see cref="VsMcpBridgeClient"/> end-to-end tests.
    /// </remarks>
    public sealed class BridgeDispatcher
    {
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        private readonly IVsContextProvider _provider;

        public BridgeDispatcher(IVsContextProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public async Task<BridgeResponse> DispatchAsync(BridgeRequest request, CancellationToken cancellationToken)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));

            try
            {
                switch (request.Method)
                {
                    case BridgeMethods.ActiveDocument:
                        return await InvokeAsync(
                            request,
                            ReadParams<ActiveDocumentParams>(request) ?? new ActiveDocumentParams(),
                            (p, ct) => _provider.GetActiveDocumentAsync(p, ct),
                            cancellationToken).ConfigureAwait(false);

                    case BridgeMethods.Selection:
                        return await InvokeAsync(
                            request,
                            (object?)null,
                            (_, ct) => _provider.GetSelectionAsync(ct),
                            cancellationToken).ConfigureAwait(false);

                    case BridgeMethods.ErrorList:
                        return await InvokeAsync(
                            request,
                            ReadParams<ErrorListParams>(request) ?? new ErrorListParams(),
                            (p, ct) => _provider.GetErrorListAsync(p, ct),
                            cancellationToken).ConfigureAwait(false);

                    case BridgeMethods.OpenDocuments:
                        return await InvokeAsync(
                            request,
                            (object?)null,
                            (_, ct) => _provider.GetOpenDocumentsAsync(ct),
                            cancellationToken).ConfigureAwait(false);

                    case BridgeMethods.SolutionOrFolder:
                        return await InvokeAsync(
                            request,
                            (object?)null,
                            (_, ct) => _provider.GetSolutionOrFolderAsync(ct),
                            cancellationToken).ConfigureAwait(false);

                    case BridgeMethods.DebuggerState:
                        return await InvokeAsync(
                            request,
                            (object?)null,
                            (_, ct) => _provider.GetDebuggerStateAsync(ct),
                            cancellationToken).ConfigureAwait(false);

                    case BridgeMethods.OutputPane:
                        return await InvokeAsync(
                            request,
                            ReadParams<OutputPaneParams>(request) ?? new OutputPaneParams(),
                            (p, ct) => _provider.GetOutputPaneAsync(p, ct),
                            cancellationToken).ConfigureAwait(false);

                    default:
                        return BridgeResponse.Fail(request.Id, "unknown_method", $"Unknown bridge method '{request.Method}'.");
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                // A provider failure (e.g. "no active document", a COM exception from DTE) must
                // never take down the pipe loop - it becomes a normal error response instead.
                return BridgeResponse.Fail(request.Id, "provider_error", ex.Message);
            }
        }

        private static TParams? ReadParams<TParams>(BridgeRequest request) where TParams : class =>
            request.Params is JsonElement element && element.ValueKind != JsonValueKind.Undefined && element.ValueKind != JsonValueKind.Null
                ? element.Deserialize<TParams>(SerializerOptions)
                : null;

        private static async Task<BridgeResponse> InvokeAsync<TParams, TResult>(
            BridgeRequest request,
            TParams parameters,
            Func<TParams, CancellationToken, Task<TResult>> call,
            CancellationToken cancellationToken)
        {
            TResult result = await call(parameters, cancellationToken).ConfigureAwait(false);
            JsonElement resultElement = JsonSerializer.SerializeToElement(result, SerializerOptions);
            return BridgeResponse.Ok(request.Id, resultElement);
        }
    }
}
