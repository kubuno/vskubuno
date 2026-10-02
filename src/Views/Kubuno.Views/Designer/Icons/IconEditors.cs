using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Design;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.Design;
using Kubuno.Desktop.Designer.PropertyBrowser;
using Kubuno.Desktop.Designer.UI;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Desktop.Designer.Icons
{
    /// <summary>
    /// The editor of every icon property (<c>editor("icon")</c>: <c>Icon</c>, <c>SmallIcon</c>, <c>LargeIcon</c>, a window's
    /// <c>Icon</c>...): the icon itself drawn in the row, and "..." opens the <see cref="IconPickerDialog"/>. The edit goes
    /// through the row like any typed value - one undo unit, written <c>Icon="Save"</c> or <c>Icon="resources/save.svg"</c>.
    /// </summary>
    public sealed class KbviewIconEditor : UITypeEditor
    {
        private static readonly Dictionary<string, Bitmap?> Swatches = new Dictionary<string, Bitmap?>(StringComparer.Ordinal);

        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) => UITypeEditorEditStyle.Modal;

        public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider? provider, object? value)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var element = RichEditors.Elements(context).FirstOrDefault();
            var viewFile = (element?.Host as IKbviewDesignServices)?.ViewFilePath;
            var dialog = new IconPickerDialog(viewFile, value as string, element?.Host as IKbviewIconServices);
            return RichEditors.ShowDialog(provider, dialog) ? dialog.Result : value;
        }

        public override bool GetPaintValueSupported(ITypeDescriptorContext? context) => true;

        public override void PaintValue(PaintValueEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (e.Value is not string value || IconValue.Classify(value) is IconValueKind.None or IconValueKind.Binding)
            {
                return;
            }

            var services = RichEditors.Elements(e.Context).FirstOrDefault()?.Host as IKbviewIconServices;
            var ink = DropDownTheme.Text;
            var size = Math.Max(1, Math.Min(e.Bounds.Width, e.Bounds.Height));
            var key = $"{value}|{size}|{ink.ToArgb()}|{services?.GetHashCode()}";
            if (!Swatches.TryGetValue(key, out var bitmap))
            {
                bitmap = Render(value, size, ink, services);
                if (Swatches.Count > 256)
                {
                    Swatches.Clear();
                }

                Swatches[key] = bitmap;
            }

            if (bitmap is not null)
            {
                e.Graphics.DrawImage(bitmap, e.Bounds.X + ((e.Bounds.Width - size) / 2), e.Bounds.Y + ((e.Bounds.Height - size) / 2), size, size);
            }
        }

        private static Bitmap? Render(string value, int size, Color ink, IKbviewIconServices? services)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var wpfInk = System.Windows.Media.Color.FromArgb(ink.A, ink.R, ink.G, ink.B);
            System.Windows.Media.ImageSource? image = null;
            if (IconValue.Classify(value) == IconValueKind.Glyph && services?.GetIconCatalog().Find(value) is { } glyph)
            {
                image = IconDrawing.ToImage(glyph, wpfInk, System.Windows.Media.Color.FromRgb(0x1a, 0x73, 0xe8), size);
            }
            else if (services is not null)
            {
                image = services.RenderIcon(value, size, wpfInk);
            }

            return image is null ? null : IconDrawing.ToGdi(image, size, size);
        }
    }

    /// <summary>The <c>IconSize</c> row: the named sizes in its list (Small 16, Medium 20, Large 24, XLarge 32); a number or <c>width, height</c> can be typed.</summary>
    public sealed class IconSizeConverter : StringConverter
    {
        public static IReadOnlyList<string> Presets { get; } = new[] { "Small", "Medium", "Large", "XLarge" };

        public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;

        public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => false;

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context) => new StandardValuesCollection(Presets.ToArray());
    }

    /// <summary>
    /// The nine-cell alignment rows (<c>TextAlign</c>, <c>ImageAlign</c> - WinForms' <c>ContentAlignment</c>): a 3 x 3 grid of
    /// buttons in the drop-down, like WinForms' ContentAlignment editor, the current cell pressed.
    /// </summary>
    public sealed class KbviewContentAlignmentEditor : UITypeEditor
    {
        /// <summary>The nine values, row by row.</summary>
        public static IReadOnlyList<string> Values { get; } = new[] { "TopLeft", "TopCenter", "TopRight", "MiddleLeft", "MiddleCenter", "MiddleRight", "BottomLeft", "BottomCenter", "BottomRight" };

        /// <summary>Whether an enum of <paramref name="variants"/> is a <c>ContentAlignment</c>.</summary>
        public static bool Applies(IReadOnlyList<string>? variants) => variants is { Count: 9 } && Values.All(variants.Contains);

        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) => UITypeEditorEditStyle.DropDown;

        public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider? provider, object? value)
        {
            if (provider?.GetService(typeof(IWindowsFormsEditorService)) is not IWindowsFormsEditorService service)
            {
                return value;
            }

            using var grid = new ContentAlignmentControl(value as string, service);
            service.DropDownControl(grid);
            return grid.Result ?? value;
        }
    }

    /// <summary>The drop-down of <see cref="KbviewContentAlignmentEditor"/>: nine themed toggle cells, Enter/arrows/Escape like WinForms'.</summary>
    internal sealed class ContentAlignmentControl : UserControl
    {
        private readonly IWindowsFormsEditorService _service;
        private readonly CheckBox[] _cells = new CheckBox[9];

        public ContentAlignmentControl(string? current, IWindowsFormsEditorService service)
        {
            _service = service;
            var cell = DropDownTheme.Px(34);
            var gap = DropDownTheme.Px(3);
            Size = new Size((cell * 3) + (gap * 4), (cell * 3) + (gap * 4));
            BackColor = DropDownTheme.Background;
            for (var i = 0; i < 9; i++)
            {
                var name = KbviewContentAlignmentEditor.Values[i];
                var box = new CheckBox
                {
                    Appearance = Appearance.Button,
                    FlatStyle = FlatStyle.Flat,
                    Checked = string.Equals(name, current, StringComparison.Ordinal) || (string.IsNullOrEmpty(current) && name == "MiddleCenter"),
                    Bounds = new Rectangle(gap + ((i % 3) * (cell + gap)), gap + ((i / 3) * (cell + gap)), cell, cell),
                    Tag = name,
                    AccessibleName = IconText.AlignmentName(name),
                    BackColor = DropDownTheme.Background,
                };
                box.FlatAppearance.BorderColor = DropDownTheme.Border;
                box.FlatAppearance.CheckedBackColor = DropDownTheme.Accent;
                box.FlatAppearance.MouseOverBackColor = DropDownTheme.HotBackground;
                new ToolTip().SetToolTip(box, IconText.AlignmentName(name));
                box.Click += (_, _) => Choose(name);
                _cells[i] = box;
                Controls.Add(box);
            }
        }

        public string? Result { get; private set; }

        private void Choose(string name)
        {
            Result = name;
            _service.CloseDropDown();
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            var focused = Array.FindIndex(_cells, c => c.Focused);
            if (focused < 0)
            {
                focused = Array.FindIndex(_cells, c => c.Checked);
            }

            var next = keyData switch
            {
                Keys.Left => focused % 3 > 0 ? focused - 1 : focused,
                Keys.Right => focused % 3 < 2 ? focused + 1 : focused,
                Keys.Up => focused >= 3 ? focused - 3 : focused,
                Keys.Down => focused < 6 ? focused + 3 : focused,
                _ => -1,
            };
            if (next >= 0)
            {
                _cells[next].Focus();
                return true;
            }

            if (keyData == Keys.Enter && focused >= 0)
            {
                Choose((string)_cells[focused].Tag);
                return true;
            }

            return base.ProcessDialogKey(keyData);
        }
    }
}
