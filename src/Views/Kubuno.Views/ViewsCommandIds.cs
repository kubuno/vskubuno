namespace Kubuno.Views
{
    /// <summary>
    /// IDs of the commands the views layer handles, inside <see cref="Kubuno.Shared.KubunoGuids.CommandSet"/>, matching
    /// <c>KubunoCommands.vsct</c> (values unchanged since they were part of the desktop layer's <c>PackageIds</c>).
    /// </summary>
    public static class ViewsCommandIds
    {
        /// <summary>Shows <see cref="Designer.ToolWindows.OutlineToolWindow"/> (Designer\INTEGRATION.md section 9).</summary>
        public const int ShowKubunoOutlineCommand = 0x0103;

        // docs/DESIGNER.md "Data bindings": the Properties window context menu of a bindable row (Designer/Bindings/BindingCommands.cs).
        public const int BindingCreateCommand = 0x0360;
        public const int BindingEditCommand = 0x0361;
        public const int BindingRemoveCommand = 0x0362;
        public const int BindingGoToDefinitionCommand = 0x0363;
    }
}
