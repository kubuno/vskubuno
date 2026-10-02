using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Design;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.Design;
using Kubuno.Views.Designer.UI;

namespace Kubuno.Views.Designer.Menus
{
    /// <summary>
    /// The Properties window editor of a <c>ShortcutKeys</c> property (registry editor <c>"shortcut"</c>), like Windows
    /// Forms' ShortcutKeysEditor: a drop-down with the Ctrl, Shift and Alt check boxes, the key list and Reset. It writes
    /// the canonical text (<c>Ctrl+Shift+S</c>); Reset (or no key) removes the attribute.
    /// </summary>
    public sealed class KbviewShortcutKeysEditor : UITypeEditor
    {
        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) => UITypeEditorEditStyle.DropDown;

        public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider? provider, object? value)
        {
            if (provider?.GetService(typeof(IWindowsFormsEditorService)) is not IWindowsFormsEditorService service)
            {
                return value;
            }

            using var control = new ShortcutKeysControl(value as string, service);
            service.DropDownControl(control);
            return control.Result ?? value;
        }
    }

    /// <summary>The ShortcutKeys editor's drop-down, drawn in the Visual Studio theme's drop-down colours (<see cref="DropDownTheme"/>).</summary>
    internal sealed class ShortcutKeysControl : UserControl
    {
        private readonly IWindowsFormsEditorService _service;
        private readonly CheckBox _ctrl;
        private readonly CheckBox _shift;
        private readonly CheckBox _alt;
        private readonly ComboBox _key;
        private bool _cancelled;

        public ShortcutKeysControl(string? current, IWindowsFormsEditorService service)
        {
            _service = service;
            DropDownTheme.Apply(this);
            var parts = ShortcutText.Parse(current);
            var pad = DropDownTheme.Px(8);
            var row = DropDownTheme.Px(26);

            var modifiers = Label(DesignerText.ShortcutModifiers, new Point(pad, pad));
            _ctrl = Check("Ctrl", parts.Ctrl, new Point(pad, pad + row));
            _shift = Check("Shift", parts.Shift, new Point(pad + DropDownTheme.Px(70), pad + row));
            _alt = Check("Alt", parts.Alt, new Point(pad + DropDownTheme.Px(140), pad + row));
            var keyLabel = Label(DesignerText.ShortcutKey, new Point(pad, pad + (2 * row) + DropDownTheme.Px(4)));
            _key = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(pad, pad + (3 * row)),
                Width = DropDownTheme.Px(140),
                BackColor = DropDownTheme.Background,
                ForeColor = DropDownTheme.Text,
                MaxDropDownItems = 16,
            };
            _key.Items.Add(DesignerText.ShortcutNone);
            _key.Items.AddRange(ShortcutText.Keys.Cast<object>().ToArray());
            _key.SelectedIndex = parts.Key is { } k ? Math.Max(0, _key.Items.IndexOf(k)) : 0;

            var reset = new Button
            {
                Text = DesignerText.ShortcutReset,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(pad + DropDownTheme.Px(150), pad + (3 * row) - DropDownTheme.Px(1)),
                AutoSize = true,
                BackColor = DropDownTheme.Background,
                ForeColor = DropDownTheme.Text,
            };
            reset.FlatAppearance.BorderColor = DropDownTheme.Border;
            reset.FlatAppearance.MouseOverBackColor = DropDownTheme.HotBackground;
            reset.Click += (_, _) =>
            {
                Result = string.Empty;
                _service.CloseDropDown();
            };

            Controls.AddRange(new Control[] { modifiers, _ctrl, _shift, _alt, keyLabel, _key, reset });
            Size = new Size(pad + DropDownTheme.Px(240), pad + (4 * row) + pad);
        }

        /// <summary>The text chosen (empty: no shortcut), or null when nothing changed.</summary>
        public string? Result { get; private set; }

        /// <summary>The drop-down closes (a click outside, Enter): what the boxes say becomes the value.</summary>
        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (!_cancelled)
            {
                Result ??= Current();
            }

            base.OnHandleDestroyed(e);
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Enter)
            {
                Result = Current();
                _service.CloseDropDown();
                return true;
            }

            if (keyData == Keys.Escape)
            {
                Result = null;
                _cancelled = true;
                _service.CloseDropDown();
                return true;
            }

            return base.ProcessDialogKey(keyData);
        }

        private string Current() =>
            ShortcutText.Format(_ctrl.Checked, _shift.Checked, _alt.Checked, _key.SelectedIndex > 0 ? (string)_key.SelectedItem : null);

        private static Label Label(string text, Point at) => new Label
        {
            Text = text,
            AutoSize = true,
            Location = at,
            BackColor = DropDownTheme.Background,
            ForeColor = DropDownTheme.SecondaryText,
        };

        private static CheckBox Check(string text, bool on, Point at) => new CheckBox
        {
            Text = text,
            Checked = on,
            AutoSize = true,
            Location = at,
            FlatStyle = FlatStyle.Flat,
            BackColor = DropDownTheme.Background,
            ForeColor = DropDownTheme.Text,
        };
    }
}
