using System;
using System.ComponentModel;
using System.Drawing.Design;
using System.Linq;
using WinForms = System.Windows.Forms;

namespace Kubuno.VisualStudio.Designer.PropertyBrowser
{
    /// <summary>
    /// The text of the <c>Dock</c>/<c>Anchor</c> attributes ⇄ the WinForms enums the pickers edit. Pure.
    /// <c>Dock</c> is one of <c>None, Top, Bottom, Left, Right, Fill</c>; <c>Anchor</c> is a comma-separated
    /// set of <c>Top, Bottom, Left, Right</c> (<c>None</c> for no edge), exactly as <c>kubuno-views</c> reads them.
    /// </summary>
    public static class LayoutAttributeText
    {
        /// <summary>The Dock value of <paramref name="text"/> (<c>None</c> when empty or unknown).</summary>
        public static WinForms.DockStyle ParseDock(string? text) =>
            Enum.TryParse<WinForms.DockStyle>(text?.Trim(), ignoreCase: false, out var dock) && Enum.IsDefined(typeof(WinForms.DockStyle), dock)
                ? dock
                : WinForms.DockStyle.None;

        public static string FormatDock(WinForms.DockStyle dock) => dock.ToString();

        /// <summary>
        /// The Anchor value of <paramref name="text"/>. An absent/empty attribute is WinForms' (and
        /// <c>kubuno-views</c>') default, <c>Top, Left</c>.
        /// </summary>
        public static WinForms.AnchorStyles ParseAnchor(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return WinForms.AnchorStyles.Top | WinForms.AnchorStyles.Left;
            }

            var anchor = WinForms.AnchorStyles.None;
            foreach (var part in text!.Split(','))
            {
                if (Enum.TryParse<WinForms.AnchorStyles>(part.Trim(), ignoreCase: false, out var edge))
                {
                    anchor |= edge;
                }
            }

            return anchor;
        }

        /// <summary><c>Top, Left, Right</c> (edges in the order Top, Bottom, Left, Right), or <c>None</c>.</summary>
        public static string FormatAnchor(WinForms.AnchorStyles anchor)
        {
            var edges = new[] { WinForms.AnchorStyles.Top, WinForms.AnchorStyles.Bottom, WinForms.AnchorStyles.Left, WinForms.AnchorStyles.Right }
                .Where(edge => (anchor & edge) != 0)
                .Select(edge => edge.ToString())
                .ToArray();
            return edges.Length == 0 ? "None" : string.Join(", ", edges);
        }
    }

    /// <summary>
    /// The <c>Dock</c> row's drop-down: the Windows Forms designer's own Dock picker (the five clickable regions
    /// plus None, <see cref="System.Windows.Forms.Design.DockEditor"/>), editing the attribute's text.
    /// </summary>
    public sealed class KbviewDockEditor : UITypeEditor
    {
        private readonly System.Windows.Forms.Design.DockEditor _inner = new System.Windows.Forms.Design.DockEditor();

        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) => UITypeEditorEditStyle.DropDown;

        public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider? provider, object? value)
        {
            var current = LayoutAttributeText.ParseDock(value as string);
            return _inner.EditValue(context, provider, current) is WinForms.DockStyle picked && picked != current
                ? LayoutAttributeText.FormatDock(picked)
                : value;
        }
    }

    /// <summary>
    /// The <c>Anchor</c> row's drop-down: the Windows Forms designer's own Anchor picker (four toggle bars around a
    /// centre box, <see cref="System.Windows.Forms.Design.AnchorEditor"/>), writing e.g. <c>Top, Left, Right</c>.
    /// </summary>
    public sealed class KbviewAnchorEditor : UITypeEditor
    {
        private readonly System.Windows.Forms.Design.AnchorEditor _inner = new System.Windows.Forms.Design.AnchorEditor();

        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) => UITypeEditorEditStyle.DropDown;

        public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider? provider, object? value)
        {
            var current = LayoutAttributeText.ParseAnchor(value as string);
            return _inner.EditValue(context, provider, current) is WinForms.AnchorStyles picked && picked != current
                ? LayoutAttributeText.FormatAnchor(picked)
                : value;
        }
    }
}
