using System;
using System.Collections.Generic;
using System.Windows;
using Kubuno.Views.Designer.DesignSurface;

namespace Kubuno.Views.Tests.Designer.Selection.Fakes
{
    /// <summary>A scripted <see cref="IDesignSurfaceHost"/> - <see cref="RaiseSelectionChanged"/> drives <see cref="Selection.SelectionSyncService"/>'s "surface -&gt; other views" direction, mirroring this test project's fake-based strategy (e.g. <c>Handlers/Fakes/FakeKubunoViewsLanguageServerClient.cs</c>).</summary>
    internal sealed class FakeDesignSurfaceHost : IDesignSurfaceHost
    {
        public List<string> SetDocumentTextCalls { get; } = new List<string>();

        public FrameworkElement Content { get; } = new FrameworkElement();

        public event EventHandler<DesignSurfaceSelectionChangedEventArgs>? SelectionChanged;

        // IDesignSurfaceHost's own INTEGRATION.md §6/§8 addition (EditRequested/SetDesignMode, for the
        // VSIX-side DesignSurfaceEditingCoordinator) - not exercised by this fake's own
        // SelectionSyncService tests, so a plain no-op/never-raised member like PlaceholderDesignSurfaceHost's.
#pragma warning disable CS0067
        public event EventHandler<DesignSurfaceEditRequestedEventArgs>? EditRequested;
#pragma warning restore CS0067

        public List<bool> SetDesignModeCalls { get; } = new List<bool>();

        public void SetDesignMode(bool on) => SetDesignModeCalls.Add(on);

        public void SetDocumentText(string xmlText) => SetDocumentTextCalls.Add(xmlText);

        public void RaiseSelectionChanged(string? elementId) =>
            SelectionChanged?.Invoke(this, new DesignSurfaceSelectionChangedEventArgs(
                elementId is null ? Array.Empty<string>() : new[] { elementId }));

        /// <summary>A multi-selection (docs/DESIGNER.md §13): the primary first, as <c>DesignSurfaceProtocol.TryParseSelectionChanged</c> produces it.</summary>
        public void RaiseMultiSelectionChanged(params string[] elementIds) =>
            SelectionChanged?.Invoke(this, new DesignSurfaceSelectionChangedEventArgs(elementIds));

        public void Dispose()
        {
        }
    }
}
