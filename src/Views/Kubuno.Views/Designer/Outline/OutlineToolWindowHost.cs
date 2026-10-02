namespace Kubuno.Views.Designer.Outline
{
    /// <summary>
    /// The single, shared <see cref="OutlineViewModel"/> instance for the whole VS session - the Outline
    /// counterpart of <see cref="Properties.PropertiesToolWindowHost"/> (see that class's own doc for why
    /// a lazy singleton, not a VSIX-injected field, is what makes "whichever of the designer pane or the
    /// Outline tool window is created first" not matter). <see cref="Selection.SelectionSyncService"/>'s
    /// OPTIONAL <c>outline</c> constructor argument (INTEGRATION.md §9 point 6) is always this instance -
    /// harmless even if the Outline tool window's own content is never shown in a given session, since an
    /// unshown view-model simply has no listener for its <c>PropertyChanged</c>/highlight updates.
    /// </summary>
    public static class OutlineToolWindowHost
    {
        private static OutlineViewModel? _current;

        public static OutlineViewModel Current => _current ??= new OutlineViewModel();
    }
}
