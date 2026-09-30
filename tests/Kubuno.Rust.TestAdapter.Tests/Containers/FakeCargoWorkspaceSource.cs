using System;
using System.Collections.Generic;
using Kubuno.TestAdapter.Containers;

namespace Kubuno.TestAdapter.Tests.Containers
{
    /// <summary>A settable <see cref="ICargoWorkspaceSource"/>, so container-discovery logic is testable without a running Visual Studio (see INTEGRATION.md).</summary>
    internal sealed class FakeCargoWorkspaceSource : ICargoWorkspaceSource
    {
        private IReadOnlyList<string> _manifestPaths;

        public FakeCargoWorkspaceSource(IReadOnlyList<string> manifestPaths)
        {
            _manifestPaths = manifestPaths;
        }

        public int ChangedRaisedCount { get; private set; }

        public IReadOnlyList<string> GetManifestPaths() => _manifestPaths;

        public event EventHandler? Changed;

        public void SetManifestPaths(IReadOnlyList<string> manifestPaths)
        {
            _manifestPaths = manifestPaths;
            ChangedRaisedCount++;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
