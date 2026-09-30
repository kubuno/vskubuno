using System.Collections.Generic;
using Kubuno.Desktop.Designer.Editing;
using Kubuno.Desktop.Designer.Handlers;
using Kubuno.Desktop.Tests.Designer.Editing.Fakes;

namespace Kubuno.Desktop.Tests.Designer.Handlers.Fakes
{
    /// <summary>An in-memory <see cref="IHandlerDocumentHost"/>: <see cref="Buffers"/> pre-seeds one <see cref="FakeEditableTextBuffer"/> per file URI, <see cref="Navigations"/> records every <see cref="NavigateTo"/> call in order.</summary>
    internal sealed class FakeHandlerDocumentHost : IHandlerDocumentHost
    {
        public Dictionary<string, FakeEditableTextBuffer> Buffers { get; } = new Dictionary<string, FakeEditableTextBuffer>();

        public List<(string Uri, LspPosition Position)> Navigations { get; } = new List<(string, LspPosition)>();

        public List<string> OpenedOrder { get; } = new List<string>();

        public IEditableTextBuffer OpenBuffer(string fileUri)
        {
            OpenedOrder.Add(fileUri);
            if (!Buffers.TryGetValue(fileUri, out var buffer))
            {
                buffer = new FakeEditableTextBuffer(string.Empty);
                Buffers[fileUri] = buffer;
            }

            return buffer;
        }

        public void NavigateTo(string fileUri, LspPosition position) => Navigations.Add((fileUri, position));
    }
}
