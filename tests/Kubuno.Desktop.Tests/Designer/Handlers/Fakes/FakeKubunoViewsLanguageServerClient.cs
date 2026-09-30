using System;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Desktop.Designer.Handlers;

namespace Kubuno.Desktop.Tests.Designer.Handlers.Fakes
{
    /// <summary>A scripted <see cref="IKubunoViewsLanguageServerClient"/> - records the last request and returns whatever <see cref="Response"/> (or <see cref="ThrowOnCall"/>) was set up, mirroring the rest of this test project's fake-based strategy (e.g. <c>Editing/Fakes/FakeEditableTextBuffer.cs</c>).</summary>
    internal sealed class FakeKubunoViewsLanguageServerClient : IKubunoViewsLanguageServerClient
    {
        public CreateHandlerResponse? Response { get; set; }

        public Exception? ThrowOnCall { get; set; }

        public CreateHandlerRequest? LastRequest { get; private set; }

        public int CallCount { get; private set; }

        public Task<CreateHandlerResponse?> CreateHandlerAsync(CreateHandlerRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            CallCount++;
            if (ThrowOnCall is not null)
            {
                throw ThrowOnCall;
            }

            return Task.FromResult(Response);
        }
    }
}
