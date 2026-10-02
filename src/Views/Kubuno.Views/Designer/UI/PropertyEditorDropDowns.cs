using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.Design;
using Kubuno.Views.Designer.PropertyBrowser;
using Kubuno.Views.Designer.Registry;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Kubuno.Views.Designer.UI
{
    /// <summary>
    /// The Visual Studio theme colours of the Properties window's drop-down editors (the colour and cursor lists): the
    /// same colours as a combo box's drop-down in the current theme (dark, light, blue, high contrast). Outside Visual
    /// Studio (tests) the Windows system colours stand in.
    /// </summary>
    internal static class DropDownTheme
    {
        /// <summary>A length in pixels at 96 DPI, scaled to the screen's DPI (the drop-downs are sized in pixels).</summary>
        public static int Px(int pixels) => (int)Math.Round(pixels * Scale);

        private static float Scale
        {
            get
            {
                if (_scale is null)
                {
                    using var g = Graphics.FromHwnd(IntPtr.Zero);
                    _scale = g.DpiX / 96f;
                }

                return _scale.Value;
            }
        }

        private static float? _scale;

        public static Color Background => Themed(EnvironmentColors.ComboBoxPopupBackgroundBeginColorKey, SystemColors.Window);

        public static Color Text => Themed(EnvironmentColors.ComboBoxItemTextColorKey, SystemColors.WindowText);

        public static Color SecondaryText => Themed(EnvironmentColors.ComboBoxItemTextInactiveColorKey, SystemColors.GrayText);

        public static Color HotBackground => Themed(EnvironmentColors.ComboBoxItemMouseOverBackgroundColorKey, SystemColors.Highlight);

        public static Color HotText => Themed(EnvironmentColors.ComboBoxItemMouseOverTextColorKey, SystemColors.HighlightText);

        public static Color HotBorder => Themed(EnvironmentColors.ComboBoxItemMouseOverBorderColorKey, SystemColors.Highlight);

        public static Color Border => Themed(EnvironmentColors.ComboBoxPopupBorderColorKey, SystemColors.ControlDark);

        public static Color Accent => Themed(EnvironmentColors.SystemHighlightColorKey, SystemColors.Highlight);

        public static Color Warning => Themed(ThemedDialogColors.ValidationErrorTextColorKey, Color.DarkGoldenrod);

        /// <summary>Applies the drop-down colours to <paramref name="control"/>.</summary>
        public static void Apply(Control control)
        {
            control.BackColor = Background;
            control.ForeColor = Text;
        }

        /// <summary>An owner-drawn list row: the hot (selected or hovered) colours when <paramref name="hot"/>.</summary>
        public static (Color Back, Color Fore) Row(bool hot) => hot ? (HotBackground, HotText) : (Background, Text);

        private static Color Themed(ThemeResourceKey key, Color fallback)
        {
            try
            {
                var color = VSColorTheme.GetThemedColor(key);
                return color.A == 0 ? fallback : color;
            }
            catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException or ArgumentException or System.Runtime.InteropServices.COMException or TypeInitializationException or FileNotFoundException)
            {
                return fallback;
            }
        }
    }

    /// <summary>Paints the small swatches of the colour editor and the grid cells.</summary>
    internal static class Swatches
    {
        /// <summary>A colour swatch in <paramref name="bounds"/>: split light | dark when <paramref name="dark"/> is given (a theme colour), checkered under a translucent colour.</summary>
        public static void Paint(Graphics g, Rectangle bounds, Color light, Color? dark)
        {
            using (var checker = new System.Drawing.Drawing2D.HatchBrush(System.Drawing.Drawing2D.HatchStyle.SmallCheckerBoard, Color.LightGray, Color.White))
            {
                g.FillRectangle(checker, bounds);
            }

            if (dark is { } d)
            {
                var half = bounds.Width / 2;
                using var l = new SolidBrush(light);
                using var r = new SolidBrush(d);
                g.FillRectangle(l, bounds.X, bounds.Y, half, bounds.Height);
                g.FillRectangle(r, bounds.X + half, bounds.Y, bounds.Width - half, bounds.Height);
            }
            else
            {
                using var b = new SolidBrush(light);
                g.FillRectangle(b, bounds);
            }

            using var border = new Pen(DropDownTheme.Border);
            g.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        }

        /// <summary>A thumbnail of image file <paramref name="path"/> (nothing when it cannot be read).</summary>
        public static void PaintImage(Graphics g, Rectangle bounds, string path)
        {
            try
            {
                using var image = Image.FromFile(path);
                g.DrawImage(image, bounds);
            }
            catch (Exception ex) when (ex is IOException or OutOfMemoryException or ArgumentException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// A list drawn in the drop-down colours of the current Visual Studio theme: the row under the pointer (else the
    /// selected one) is hot, like the rows of a Visual Studio combo box.
    /// </summary>
    internal class ThemedDropDownList : ListBox
    {
        private int _hot = -1;

        public ThemedDropDownList(int itemHeight)
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = itemHeight;
            BorderStyle = BorderStyle.None;
            IntegralHeight = false;
            DropDownTheme.Apply(this);
        }

        /// <summary>Paints one row's content (its background is already painted) in <paramref name="fore"/>.</summary>
        protected virtual void PaintRow(Graphics g, Rectangle bounds, string name, Color fore)
        {
            TextRenderer.DrawText(g, name, Font, new Point(bounds.X + 4, bounds.Y + ((bounds.Height - Font.Height) / 2)), fore);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0)
            {
                return;
            }

            var hot = _hot >= 0 ? e.Index == _hot : (e.State & DrawItemState.Selected) != 0;
            var (back, fore) = DropDownTheme.Row(hot);
            using (var brush = new SolidBrush(back))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }

            if (hot)
            {
                using var border = new Pen(DropDownTheme.HotBorder);
                e.Graphics.DrawRectangle(border, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
            }

            PaintRow(e.Graphics, e.Bounds, (string)Items[e.Index], fore);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var i = IndexFromPoint(e.Location);
            if (i != _hot)
            {
                _hot = i;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hot = -1;
            Invalidate();
        }
    }

    /// <summary>
    /// The colour editor's drop-down, like WinForms' colour editor but with the Kubuno theme colours first: tabs Theme
    /// (each token with its light and dark colours), Custom (a palette and Windows' colour dialog), Web and System. A
    /// line under the tabs shows the WCAG contrast of the colour under the pointer against its counterpart (ForeColor
    /// vs BackColor) in the light and dark themes - a hint, never a refusal. Drawn in the Visual Studio theme's
    /// drop-down colours.
    /// </summary>
    internal sealed class ColorPickerControl : UserControl
    {
        private static readonly string[] Palette =
        {
            "#FFFFFF", "#FFC0C0", "#FFE0C0", "#FFFFC0", "#C0FFC0", "#C0FFFF", "#C0C0FF", "#FFC0FF",
            "#E0E0E0", "#FF8080", "#FFC080", "#FFFF80", "#80FF80", "#80FFFF", "#8080FF", "#FF80FF",
            "#C0C0C0", "#FF0000", "#FF8000", "#FFFF00", "#00FF00", "#00FFFF", "#0000FF", "#FF00FF",
            "#808080", "#C00000", "#C04000", "#C0C000", "#00C000", "#00C0C0", "#0000C0", "#C000C0",
            "#404040", "#800000", "#804000", "#808000", "#008000", "#008080", "#000080", "#800080",
            "#000000", "#400000", "#804040", "#404000", "#004000", "#004040", "#000040", "#400040",
        };

        private readonly string _attribute;
        private readonly ColorValue? _counterpart;
        private readonly IWindowsFormsEditorService _service;
        private readonly Label _contrast;
        private readonly ThemedTabStrip _tabs;
        private readonly List<Control> _pages = new List<Control>();

        public ColorPickerControl(string? current, string attribute, ColorValue? counterpart, IWindowsFormsEditorService service)
        {
            _attribute = attribute;
            _counterpart = counterpart;
            _service = service;
            Size = new Size(DropDownTheme.Px(300), DropDownTheme.Px(320));
            DropDownTheme.Apply(this);

            var body = new Panel { Dock = DockStyle.Fill };
            _pages.Add(Page(ThemeTokens.All.Select(t => t.Name), token: true, current));
            _pages.Add(CustomPage(current));
            _pages.Add(Page(ColorText.WebColorNames, token: false, current));
            _pages.Add(Page(ColorText.SystemColorNames, token: false, current));
            foreach (var page in _pages)
            {
                page.Dock = DockStyle.Fill;
                page.Visible = false;
                body.Controls.Add(page);
            }

            _tabs = new ThemedTabStrip(new[] { DesignerText.ColorTabTheme, DesignerText.ColorTabCustom, DesignerText.ColorTabWeb, DesignerText.ColorTabSystem }) { Dock = DockStyle.Top };
            _tabs.SelectedChanged += (_, _) => ShowPage(_tabs.Selected);

            _contrast = new Label { Dock = DockStyle.Bottom, Height = DropDownTheme.Px(36), AutoEllipsis = true, Padding = new Padding(DropDownTheme.Px(4), DropDownTheme.Px(2), DropDownTheme.Px(4), DropDownTheme.Px(2)) };
            DropDownTheme.Apply(_contrast);
            Controls.Add(body);
            Controls.Add(_tabs);
            Controls.Add(_contrast);

            var selected = 0;
            if (ColorText.TryParse(current, out var parsed))
            {
                selected = parsed.Kind switch
                {
                    ColorValueKind.Hex => 1,
                    ColorValueKind.Web => 2,
                    ColorValueKind.System => 3,
                    _ => 0,
                };
            }

            _tabs.Selected = selected;
            ShowPage(selected);
            ShowContrast(current);
        }

        /// <summary>The picked colour's attribute text, or null when nothing was picked.</summary>
        public string? Result { get; private set; }

        private void ShowPage(int index)
        {
            for (var i = 0; i < _pages.Count; i++)
            {
                _pages[i].Visible = i == index;
            }
        }

        private Control Page(IEnumerable<string> names, bool token, string? current)
        {
            var list = new SwatchList(token);
            list.Items.AddRange(names.Cast<object>().ToArray());
            var index = list.Items.IndexOf(ColorText.TryParse(current, out var v) ? v.Text : string.Empty);
            if (index >= 0)
            {
                list.SelectedIndex = index;
            }

            var tip = new ToolTip();
            list.MouseMove += (_, e) =>
            {
                var i = list.IndexFromPoint(e.Location);
                if (i >= 0)
                {
                    var name = (string)list.Items[i];
                    ShowContrast(name);
                    if (token && ThemeTokens.Find(name) is { } t && tip.GetToolTip(list) != t.LocalizedDoc)
                    {
                        tip.SetToolTip(list, t.LocalizedDoc + " (" + DesignerText.ColorLightDark + " : " + t.Light + " / " + t.Dark + ")");
                    }
                }
            };
            list.MouseClick += (_, e) =>
            {
                var i = list.IndexFromPoint(e.Location);
                if (i >= 0)
                {
                    Pick((string)list.Items[i]);
                }
            };
            list.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter && list.SelectedItem is string name)
                {
                    Pick(name);
                }
            };
            return list;
        }

        private Control CustomPage(string? current)
        {
            var page = new Panel();
            DropDownTheme.Apply(page);
            var grid = new Panel { Dock = DockStyle.Fill };
            var cell = DropDownTheme.Px(26);
            for (var i = 0; i < Palette.Length; i++)
            {
                var hex = Palette[i];
                var swatch = new Panel { Bounds = new Rectangle(DropDownTheme.Px(6) + (i % 8 * (cell + DropDownTheme.Px(4))), DropDownTheme.Px(6) + (i / 8 * (cell + DropDownTheme.Px(4))), cell, cell), BackColor = ColorText.ParseHex(hex) ?? Color.Empty, Cursor = Cursors.Hand };
                swatch.Paint += (_, e) =>
                {
                    using var border = new Pen(DropDownTheme.Border);
                    e.Graphics.DrawRectangle(border, 0, 0, swatch.Width - 1, swatch.Height - 1);
                };
                swatch.MouseEnter += (_, _) => ShowContrast(hex);
                swatch.Click += (_, _) => Pick(hex);
                grid.Controls.Add(swatch);
            }

            var define = new Button { Text = DesignerText.ColorDefine, Dock = DockStyle.Bottom, Height = DropDownTheme.Px(28), FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false };
            DropDownTheme.Apply(define);
            define.FlatAppearance.BorderColor = DropDownTheme.Border;
            define.FlatAppearance.MouseOverBackColor = DropDownTheme.HotBackground;
            define.FlatAppearance.MouseDownBackColor = DropDownTheme.HotBackground;
            define.Click += (_, _) =>
            {
                using var dialog = new ColorDialog { FullOpen = true, AnyColor = true };
                if (ColorText.TryParse(current, out var v) && v.Kind != ColorValueKind.Empty)
                {
                    dialog.Color = v.Light;
                }

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    Pick(ColorText.FormatHex(Color.FromArgb(255, dialog.Color)));
                }
            };
            page.Controls.Add(grid);
            page.Controls.Add(define);
            return page;
        }

        private void ShowContrast(string? text)
        {
            if (!ColorText.TryParse(text, out var value) || ColorContrast.Of(_attribute, value, _counterpart) is not { } ratio)
            {
                _contrast.Text = string.Empty;
                return;
            }

            var warn = ratio.Light < ColorText.MinimumContrast || ratio.Dark < ColorText.MinimumContrast;
            _contrast.Text = DesignerText.ContrastLine(ratio.Light, ratio.Dark) + (warn ? Environment.NewLine + DesignerText.ContrastWarning : string.Empty);
            _contrast.ForeColor = warn ? DropDownTheme.Warning : DropDownTheme.SecondaryText;
        }

        private void Pick(string text)
        {
            Result = text;
            _service.CloseDropDown();
        }

        /// <summary>A list of colour names, each with its swatch (split light | dark for a theme colour).</summary>
        private sealed class SwatchList : ThemedDropDownList
        {
            private readonly bool _token;

            public SwatchList(bool token)
                : base(DropDownTheme.Px(20))
            {
                _token = token;
            }

            protected override void PaintRow(Graphics g, Rectangle bounds, string name, Color fore)
            {
                if (ColorText.TryParse(name, out var color))
                {
                    Swatches.Paint(g, new Rectangle(bounds.X + DropDownTheme.Px(2), bounds.Y + DropDownTheme.Px(2), DropDownTheme.Px(_token ? 28 : 20), bounds.Height - DropDownTheme.Px(4)), color.Light, _token ? color.Dark : (Color?)null);
                }

                TextRenderer.DrawText(g, name, Font, new Point(bounds.X + DropDownTheme.Px(_token ? 34 : 26), bounds.Y + ((bounds.Height - Font.Height) / 2)), fore);
            }
        }
    }

    /// <summary>
    /// A row of tabs drawn in the drop-down colours (a WinForms <see cref="TabControl"/> keeps the Windows colours whatever
    /// the Visual Studio theme): the selected tab is underlined in the theme's accent colour.
    /// </summary>
    internal sealed class ThemedTabStrip : Control
    {
        private readonly string[] _titles;
        private int _selected;
        private int _hot = -1;

        public ThemedTabStrip(string[] titles)
        {
            _titles = titles;
            Height = DropDownTheme.Px(28);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            DropDownTheme.Apply(this);
            TabStop = true;
        }

        public event EventHandler? SelectedChanged;

        public int Selected
        {
            get => _selected;
            set
            {
                var clamped = Math.Max(0, Math.Min(_titles.Length - 1, value));
                if (clamped != _selected)
                {
                    _selected = clamped;
                    Invalidate();
                    SelectedChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private Rectangle TabBounds(int index)
        {
            var width = Width / Math.Max(1, _titles.Length);
            return new Rectangle(index * width, 0, index == _titles.Length - 1 ? Width - (index * width) : width, Height);
        }

        private int HitTab(Point point)
        {
            for (var i = 0; i < _titles.Length; i++)
            {
                if (TabBounds(i).Contains(point))
                {
                    return i;
                }
            }

            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var back = new SolidBrush(DropDownTheme.Background))
            {
                e.Graphics.FillRectangle(back, ClientRectangle);
            }

            for (var i = 0; i < _titles.Length; i++)
            {
                var r = TabBounds(i);
                if (i == _hot && i != _selected)
                {
                    using var hot = new SolidBrush(DropDownTheme.HotBackground);
                    e.Graphics.FillRectangle(hot, r);
                }

                var fore = i == _hot && i != _selected ? DropDownTheme.HotText : i == _selected ? DropDownTheme.Text : DropDownTheme.SecondaryText;
                TextRenderer.DrawText(e.Graphics, _titles[i], Font, r, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                if (i == _selected)
                {
                    using var accent = new SolidBrush(DropDownTheme.Accent);
                    e.Graphics.FillRectangle(accent, r.X + 4, r.Bottom - 2, r.Width - 8, 2);
                }
            }

            using var line = new Pen(DropDownTheme.Border);
            e.Graphics.DrawLine(line, 0, Height - 1, Width, Height - 1);
            if (Focused)
            {
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(TabBounds(_selected), -2, -2), DropDownTheme.Text, DropDownTheme.Background);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var hot = HitTab(e.Location);
            if (hot != _hot)
            {
                _hot = hot;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hot = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var hit = HitTab(e.Location);
            if (hit >= 0)
            {
                Selected = hit;
            }
        }

        protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left)
            {
                Selected = _selected - 1;
            }
            else if (e.KeyCode == Keys.Right)
            {
                Selected = _selected + 1;
            }
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }
    }

    /// <summary>The cursor editor's drop-down: each cursor drawn next to its name, in the drop-down colours of the theme.</summary>
    internal sealed class CursorListControl : ThemedDropDownList
    {
        private readonly IWindowsFormsEditorService _service;

        public CursorListControl(IReadOnlyList<string> names, string? current, IWindowsFormsEditorService service)
            : base(DropDownTheme.Px(26))
        {
            _service = service;
            Height = Math.Min(names.Count, 12) * ItemHeight + 4;
            Items.AddRange(names.Cast<object>().ToArray());
            SelectedIndex = Math.Max(0, Items.IndexOf(current ?? "Default"));
        }

        public string? Result { get; private set; }

        protected override void PaintRow(Graphics g, Rectangle bounds, string name, Color fore)
        {
            if (CursorNames.CursorOf(name) is { } cursor)
            {
                // A cursor image is black and white: on a dark theme it sits on a light tile to stay visible.
                var tile = new Rectangle(bounds.X + DropDownTheme.Px(2), bounds.Y + DropDownTheme.Px(1), DropDownTheme.Px(24), DropDownTheme.Px(24));
                if (DropDownTheme.Background.GetBrightness() < 0.5f)
                {
                    using var light = new SolidBrush(Color.FromArgb(0xF0, 0xF0, 0xF0));
                    g.FillRectangle(light, tile);
                }

                cursor.Draw(g, tile);
            }

            TextRenderer.DrawText(g, name, Font, new Point(bounds.X + DropDownTheme.Px(30), bounds.Y + ((bounds.Height - Font.Height) / 2)), fore);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            var i = IndexFromPoint(e.Location);
            if (i >= 0)
            {
                Result = (string)Items[i];
                _service.CloseDropDown();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Enter && SelectedItem is string name)
            {
                Result = name;
                _service.CloseDropDown();
            }
        }
    }

    /// <summary>The window Windows' own dialogs (font, colour) are owned by: Visual Studio's main window.</summary>
    internal sealed class VsDialogOwner : IWin32Window
    {
        private VsDialogOwner(IntPtr handle)
        {
            Handle = handle;
        }

        public IntPtr Handle { get; }

        /// <summary>Visual Studio's dialog owner, or null outside Visual Studio.</summary>
        public static IWin32Window? Current()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Package.GetGlobalService(typeof(SVsUIShell)) is IVsUIShell shell && shell.GetDialogOwnerHwnd(out var hwnd) == 0 && hwnd != IntPtr.Zero)
            {
                return new VsDialogOwner(hwnd);
            }

            return null;
        }
    }
}
