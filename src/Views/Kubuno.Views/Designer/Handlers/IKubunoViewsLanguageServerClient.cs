using System.Threading;
using System.Threading.Tasks;

namespace Kubuno.Views.Designer.Handlers
{
    /// <summary>
    /// The seam <see cref="HandlerCreationService"/> depends on instead of a raw
    /// <c>StreamJsonRpc.JsonRpc</c>, mirroring <see cref="Editing.IEditableTextBuffer"/>'s own reasoning:
    /// unit-testable with a fake (see
    /// tests/Kubuno.Desktop.Tests/Designer/Handlers/Fakes/FakeKubunoViewsLanguageServerClient.cs).
    /// <see cref="Infrastructure.JsonRpcKubunoViewsLanguageServerClient"/> is the real, JsonRpc-dependent
    /// implementation calling <c>kubuno/createHandler</c> (docs/DESIGNER.md §6/§8, DSG-10).
    /// </summary>
    public interface IKubunoViewsLanguageServerClient
    {
        /// <summary><see langword="null"/> when the call itself failed (server unreachable, a JSON-RPC error, an unparsable response) - never throws for that case, so a caller can treat "no usable response" uniformly regardless of which of those it was.</summary>
        Task<CreateHandlerResponse?> CreateHandlerAsync(CreateHandlerRequest request, CancellationToken cancellationToken);
    }
}
