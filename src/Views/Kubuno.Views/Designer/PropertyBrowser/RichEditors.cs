using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Design;
using System.Globalization;
using System.Linq;
using System.Windows.Forms.Design;
using Kubuno.Views.Designer.Properties;
using Kubuno.Views.Designer.Registry;
using Kubuno.Views.Designer.Selection;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Views.Designer.PropertyBrowser
{
    /// <summary>
    /// Which editor and converter a registry property gets in the Properties window (its <c>editor</c> /
    /// <c>type_converter</c>), how a typed value is normalized for it, and the WinForms side effects of an edit.
    /// </summary>
    public static class RichEditors
    {
        public static UITypeEditor? EditorFor(PropertyMeta? meta) => meta?.Editor switch
        {
            "color" => new KbviewColorEditor(),
            "font" => new KbviewFontEditor(),
            "image" => new KbviewImageEditor(),
            "icon" => new Icons.KbviewIconEditor(),
            "cursor" => new KbviewCursorEditor(),
            // A menu item's ShortcutKeys (docs/MENUS.md section 5): Windows Forms' ShortcutKeys editor.
            "shortcut" => new Menus.KbviewShortcutKeysEditor(),
            // A Vec<String> property of a project control (WinForms' string[]): the String Collection Editor.
            "lines" => new KbviewLinesEditor(),
            // A <RegistryKey>'s Path (docs/STORAGE-COMPONENTS.md §5.2): the Registry key picker.
            "registry-key" => new KbviewRegistryKeyEditor(),
            _ when Icons.KbviewContentAlignmentEditor.Applies(meta?.Kind?.EnumVariants) => new Icons.KbviewContentAlignmentEditor(),
            _ => null,
        };

        public static TypeConverter? ConverterFor(PropertyMeta? meta)
        {
            if (meta?.Editor is { } editor && editor.StartsWith("reference:", StringComparison.Ordinal))
            {
                return new ReferenceNamesConverter(editor.Substring("reference:".Length));
            }

            if (meta?.Editor is { } classEditor && classEditor.StartsWith("class:", StringComparison.Ordinal))
            {
                return new ClassNamesConverter(classEditor.Substring("class:".Length));
            }

            if (meta?.Editor == "lines")
            {
                return new LinesConverter();
            }

            if (meta?.TypeConverter == "IconSize")
            {
                return new Icons.IconSizeConverter();
            }

            return meta?.TypeConverter == "Opacity" ? new OpacityConverter() : null;
        }

        /// <summary>The text written for <paramref name="text"/> typed in the row (empty = remove the attribute); throws <see cref="ArgumentException"/> when invalid.</summary>
        public static string Normalize(PropertyMeta? meta, PropKind kind, string text)
        {
            if (Bindings.BindingMarkup.IsMarkupExtension(text))
            {
                return text;
            }

            switch (meta?.Editor)
            {
                case "color":
                    return ColorText.Normalize(text);
                case "font":
                    return FontText.Normalize(text);
                case "image":
                    return text.Trim().Replace('\\', '/');
                case "icon":
                    return Icons.IconValue.Normalize(text);
                case "shortcut":
                    var shortcut = Menus.ShortcutText.Normalize(text);
                    return shortcut.Length > 0 || text.Trim().Length == 0 ? shortcut : throw new ArgumentException(DesignerText.InvalidShortcut(text));
            }

            if (meta?.TypeConverter == "Opacity")
            {
                return CompositeText.ParseOpacity(text) is { } percent ? CompositeText.Number(percent) : throw new ArgumentException(DesignerText.InvalidOpacity(text));
            }

            return AttributeValueRules.Normalize(kind, text);
        }

        /// <summary>
        /// What WinForms' designer does next to an edit: a <c>BackColor</c> set on a button (<c>ButtonBase</c>) also sets
        /// <c>UseVisualStyleBackColor</c> to false, otherwise the button keeps the theme's face and ignores the colour.
        /// </summary>
        public static void AfterSet(KbviewElementObject element, string attribute, string value)
        {
            if (attribute == "BackColor" && value.Length > 0 && element.Component.IsA("ButtonBase")
                && element.Component.Properties.Any(p => p.Name == "UseVisualStyleBackColor")
                && element.GetRawValue("UseVisualStyleBackColor") != "false")
            {
                element.SetAttribute("UseVisualStyleBackColor", "false");
            }
        }

        /// <summary>Shows a modal editor dialog (a themed Visual Studio dialog over the IDE); true on OK.</summary>
        internal static bool ShowDialog(IServiceProvider? provider, UI.ThemedEditorDialog dialog)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _ = provider; // The dialog is a themed Visual Studio window, parented by Visual Studio itself.
            return dialog.Ask();
        }

        /// <summary>The element(s) a grid context edits: one, or every one of a multi-selection.</summary>
        public static IReadOnlyList<KbviewElementObject> Elements(ITypeDescriptorContext? context) => context?.Instance switch
        {
            KbviewElementObject element => new[] { element },
            object[] many => many.OfType<KbviewElementObject>().ToArray(),
            KbviewCompositeValue { Owner: { } owner } => new[] { owner },
            KbviewBindingsValue { Owner: { } owner } => new[] { owner },
            _ => Array.Empty<KbviewElementObject>(),
        };
    }

    /// <summary>
    /// A reference to another element of the view by its <c>x:Name</c> (<c>ContextMenu</c>, <c>AcceptButton</c>...): the
    /// dropdown lists the matching names found in the current text - elements named <paramref name="kind"/> or whose class
    /// derives from it. Not exclusive (a name can be typed before its element exists).
    /// </summary>
    public sealed class ReferenceNamesConverter : StringConverter
    {
        public ReferenceNamesConverter(string kind)
        {
            Kind = kind;
        }

        public string Kind { get; }

        public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => RichEditors.Elements(context).Count > 0;

        public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => false;

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
        {
            var element = RichEditors.Elements(context).FirstOrDefault();
            return new StandardValuesCollection(element is null ? Array.Empty<string>() : Names(element.Host.GetCurrentText(), element.Host.Registry, Kind).ToArray());
        }

        /// <summary>The <c>x:Name</c>s of the elements of <paramref name="text"/> that are a <paramref name="kind"/>.</summary>
        public static IReadOnlyList<string> Names(string text, ComponentRegistry registry, string kind) =>
            ViewDocument.Parse(text)?.DescendantsAndSelf()
                .Where(n => n.Name == kind || registry.Find(n.Name)?.IsA(kind) == true)
                .Select(n => n.Attribute("x:Name"))
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .Distinct(StringComparer.Ordinal)
                .ToList()
            ?? (IReadOnlyList<string>)Array.Empty<string>();
    }

    /// <summary>
    /// A class of the project by its name (<c>ItemTemplate</c>: a user control): the dropdown lists the classes of the
    /// registry of that kind (<c>UserControl</c> → the <c>#[derive(UserControl)]</c> classes). Not exclusive (a class can be
    /// named before it is written).
    /// </summary>
    public sealed class ClassNamesConverter : StringConverter
    {
        public ClassNamesConverter(string kind)
        {
            Kind = kind;
        }

        public string Kind { get; }

        public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => RichEditors.Elements(context).Count > 0;

        public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => false;

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
        {
            var element = RichEditors.Elements(context).FirstOrDefault();
            return new StandardValuesCollection(element is null ? Array.Empty<string>() : Names(element.Host.Registry, Kind).ToArray());
        }

        /// <summary>The names of the registry's project classes of <paramref name="kind"/> (<c>UserControl</c>: <c>kind == "user_control"</c>).</summary>
        public static IReadOnlyList<string> Names(ComponentRegistry registry, string kind)
        {
            var wanted = kind == "UserControl" ? "user_control" : kind == "Component" ? "component" : "control";
            return registry.Components
                .Where(c => c.IsProject && string.Equals(c.Kind, wanted, StringComparison.Ordinal))
                .Select(c => c.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
        }
    }

    /// <summary>An opacity: the attribute holds a percentage (<c>80</c>), shown <c>80 %</c> like WinForms' <c>OpacityConverter</c>.</summary>
    public sealed class OpacityConverter : StringConverter
    {
        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) => destinationType == typeof(string);

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            var text = value as string ?? string.Empty;
            return destinationType == typeof(string) && !BindingExpressionParser.IsBindingExpression(text) && CompositeText.ParseNumber(text) is { } v
                ? CompositeText.FormatOpacity(v)
                : text;
        }

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            var text = (value as string ?? string.Empty).Trim();
            if (text.Length == 0 || BindingExpressionParser.IsBindingExpression(text))
            {
                return text;
            }

            return CompositeText.ParseOpacity(text) is { } percent ? CompositeText.Number(percent) : throw new ArgumentException(DesignerText.InvalidOpacity(text));
        }
    }

    /// <summary>The WCAG contrast the colour editor shows next to a colour (docs/EVENTS.md's colour policy). Pure.</summary>
    public static class ColorContrast
    {
        /// <summary>
        /// The colour <paramref name="attribute"/> (<c>ForeColor</c> or <c>BackColor</c>) is read against on <paramref name="element"/>:
        /// a ForeColor against the element's BackColor (else the Background theme colour), a BackColor against its ForeColor
        /// (else TextPrimary). Null for another attribute.
        /// </summary>
        public static ColorValue? Counterpart(string attribute, Func<string, string?> read)
        {
            string other, fallback;
            switch (attribute)
            {
                case "ForeColor": other = "BackColor"; fallback = "Background"; break;
                case "BackColor": other = "ForeColor"; fallback = "TextPrimary"; break;
                default: return null;
            }

            return ColorText.TryParse(read(other), out var value) && value.Kind != ColorValueKind.Empty ? value : ColorText.TryParse(fallback, out var token) ? token : null;
        }

        /// <summary>The contrast (light theme, dark theme) of <paramref name="chosen"/> set as <paramref name="attribute"/> against its counterpart; null when it does not apply.</summary>
        public static (double Light, double Dark)? Of(string attribute, ColorValue chosen, ColorValue? counterpart)
        {
            if (counterpart is null || chosen.Kind == ColorValueKind.Empty)
            {
                return null;
            }

            var (fg, bg) = attribute == "BackColor" ? (counterpart, chosen) : (chosen, counterpart);
            return (ColorText.ContrastRatio(fg.Light, bg.Light), ColorText.ContrastRatio(fg.Dark, bg.Dark));
        }
    }

    /// <summary>The colour row's editor: a drop-down like WinForms' colour editor, Theme tab first (see <see cref="UI.ColorPickerControl"/>), and a swatch in the cell.</summary>
    public sealed class KbviewColorEditor : UITypeEditor
    {
        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) => UITypeEditorEditStyle.DropDown;

        public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider? provider, object? value)
        {
            if (provider?.GetService(typeof(IWindowsFormsEditorService)) is not IWindowsFormsEditorService service)
            {
                return value;
            }

            var attribute = context?.PropertyDescriptor?.Name ?? string.Empty;
            var element = RichEditors.Elements(context).FirstOrDefault();
            var counterpart = element is null ? null : ColorContrast.Counterpart(attribute, name => element.GetRawValue(name));
            using var picker = new UI.ColorPickerControl(value as string, attribute, counterpart, service);
            service.DropDownControl(picker);
            return picker.Result ?? value;
        }

        public override bool GetPaintValueSupported(ITypeDescriptorContext? context) => true;

        public override void PaintValue(PaintValueEventArgs e)
        {
            if (e.Value is string text && ColorText.TryParse(text, out var color) && color.Kind != ColorValueKind.Empty)
            {
                UI.Swatches.Paint(e.Graphics, e.Bounds, color.Light, color.Kind == ColorValueKind.Token ? color.Dark : (Color?)null);
            }
        }
    }

    /// <summary>The font row's editor: Windows' font dialog, written like WinForms' font text (<c>Segoe UI, 12pt, style=Bold</c>).</summary>
    public sealed class KbviewFontEditor : UITypeEditor
    {
        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) => UITypeEditorEditStyle.Modal;

        public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider? provider, object? value)
        {
            using var dialog = new System.Windows.Forms.FontDialog { ShowEffects = true, FontMustExist = true, AllowVerticalFonts = false };
            if (FontText.ToDrawingFont(value as string) is { } font)
            {
                dialog.Font = font;
            }

            ThreadHelper.ThrowIfNotOnUIThread();
            return dialog.ShowDialog(UI.VsDialogOwner.Current()) == System.Windows.Forms.DialogResult.OK ? FontText.FromDrawingFont(dialog.Font) : value;
        }
    }

    /// <summary>The image row's editor: the project's images and a file picker (see <see cref="UI.ImagePickerDialog"/>).</summary>
    public sealed class KbviewImageEditor : UITypeEditor
    {
        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) => UITypeEditorEditStyle.Modal;

        public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider? provider, object? value)
        {
            var element = RichEditors.Elements(context).FirstOrDefault();
            var viewFile = (element?.Host as IKbviewDesignServices)?.ViewFilePath;
            ThreadHelper.ThrowIfNotOnUIThread();
            _ = provider;
            // The Select Resource dialog (docs/RESOURCES.md): a local image file or a project resource (`{Res key}`).
            var filter = context?.PropertyDescriptor?.Name == "Icon" ? Resources.ResourceKindFilter.Icons : Resources.ResourceKindFilter.Images;
            return Resources.ResourcePicker.Pick(viewFile, value as string, filter) ?? value;
        }

        public override bool GetPaintValueSupported(ITypeDescriptorContext? context) => true;

        public override void PaintValue(PaintValueEventArgs e)
        {
            var element = RichEditors.Elements(e.Context).FirstOrDefault();
            var viewFile = (element?.Host as IKbviewDesignServices)?.ViewFilePath;
            if (e.Value is string path && ImageResources.Resolve(viewFile, path) is { } full)
            {
                UI.Swatches.PaintImage(e.Graphics, e.Bounds, full);
            }
            else if (e.Value is string reference && viewFile is not null && Logic.Resources.ProjectResources.Find(Logic.Resources.ProjectResources.Items(viewFile), reference)?.FilePath is { } linked)
            {
                // A `{Res key}` of a linked resource: its file.
                UI.Swatches.PaintImage(e.Graphics, e.Bounds, linked);
            }
        }
    }

    /// <summary>
    /// The editor of a string-list property of a project control (<c>#[property] countries: Vec&lt;String&gt;</c>, editor
    /// <c>"lines"</c>): Windows Forms' String Collection Editor, one item per line, written in the attribute one item per
    /// line (<c>Countries="France&amp;#10;Belgique"</c>).
    /// </summary>
    /// <summary>
    /// The … button of a <c>&lt;RegistryKey&gt;</c>'s <c>Path</c> (docs/STORAGE-COMPONENTS.md §5.2): the Registry key picker,
    /// opened on the element's <c>Hive</c> and <c>View</c>; a key picked in another hive or view sets them too.
    /// </summary>
    public sealed class KbviewRegistryKeyEditor : UITypeEditor
    {
        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) => UITypeEditorEditStyle.Modal;

        public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider? provider, object? value)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var element = RichEditors.Elements(context).FirstOrDefault();
            var hive = element?.GetRawValue("Hive") is { Length: > 0 } h ? h : "CurrentUser";
            var view = element?.GetRawValue("View") is { Length: > 0 } v ? v : "Default";
            var dialog = new UI.RegistryKeyPickerDialog(hive, view, value as string ?? string.Empty);
            if (!RichEditors.ShowDialog(provider, dialog))
            {
                return value;
            }

            if (element is not null)
            {
                if (dialog.Hive != (Kubuno.Views.Logic.Settings.RegistryKeyPath.HiveName(hive) ?? "CurrentUser"))
                {
                    element.SetAttribute("Hive", dialog.Hive);
                }

                if (dialog.View != view)
                {
                    element.SetAttribute("View", dialog.View);
                }
            }

            return dialog.Path;
        }
    }

    public sealed class KbviewLinesEditor : UITypeEditor
    {
        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) => UITypeEditorEditStyle.Modal;

        public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider? provider, object? value)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dialog = new UI.StringListDialog(LinesText.Split(value as string));
            return RichEditors.ShowDialog(provider, dialog) ? LinesText.Join(dialog.Lines) : value;
        }
    }

    /// <summary>The attribute text of a string list (one item per line) and its one-line display in the grid.</summary>
    public static class LinesText
    {
        /// <summary>The items of an attribute value (empty lines dropped).</summary>
        public static IReadOnlyList<string> Split(string? text) =>
            (text ?? string.Empty).Replace("\r\n", "\n").Split('\n').Where(l => l.Trim().Length > 0).ToArray();

        /// <summary>The attribute value of <paramref name="lines"/> (empty lines dropped).</summary>
        public static string Join(IEnumerable<string> lines) => string.Join("\n", lines.Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0));

        /// <summary>The grid's one-line form: <c>France; Belgique</c>.</summary>
        public static string Display(string? text) => string.Join("; ", Split(text));

        /// <summary>A value typed in the grid: kept as is when it has line breaks, else split on <c>;</c>.</summary>
        public static string FromTyped(string text) => text.Contains("\n") ? Join(Split(text)) : Join(text.Split(';').Select(s => s.Trim()));
    }

    /// <summary>Shows a string list on one line in the grid and reads one typed there (<see cref="LinesText"/>).</summary>
    public sealed class LinesConverter : StringConverter
    {
        public override object? ConvertTo(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object? value, Type destinationType) =>
            destinationType == typeof(string) && value is string s ? LinesText.Display(s) : base.ConvertTo(context, culture, value, destinationType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object value) =>
            value is string s ? LinesText.FromTyped(s) : base.ConvertFrom(context, culture, value);
    }

    /// <summary>The cursor row's editor: the list of cursors, each drawn next to its name, like WinForms' cursor editor.</summary>
    public sealed class KbviewCursorEditor : UITypeEditor
    {
        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) => UITypeEditorEditStyle.DropDown;

        public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider? provider, object? value)
        {
            if (provider?.GetService(typeof(IWindowsFormsEditorService)) is not IWindowsFormsEditorService service)
            {
                return value;
            }

            var names = (context?.PropertyDescriptor as KbviewAttributePropertyDescriptor)?.Kind?.EnumVariants ?? CursorNames.All;
            using var list = new UI.CursorListControl(names, value as string, service);
            service.DropDownControl(list);
            return list.Result ?? value;
        }
    }

    /// <summary>The cursor names a view may use and the Windows cursor each one shows.</summary>
    public static class CursorNames
    {
        public static IReadOnlyList<string> All { get; } = new[]
        {
            "Default", "Arrow", "IBeam", "Hand", "Wait", "No", "SizeAll", "SizeNS", "SizeWE", "SizeNWSE", "SizeNESW", "Cross", "Help", "AppStarting", "UpArrow",
        };

        /// <summary>The Windows cursor drawn for <paramref name="name"/> (<c>Default</c> is the arrow).</summary>
        public static System.Windows.Forms.Cursor? CursorOf(string name) => name switch
        {
            "Default" or "Arrow" => System.Windows.Forms.Cursors.Arrow,
            "IBeam" => System.Windows.Forms.Cursors.IBeam,
            "Hand" => System.Windows.Forms.Cursors.Hand,
            "Wait" => System.Windows.Forms.Cursors.WaitCursor,
            "No" => System.Windows.Forms.Cursors.No,
            "SizeAll" => System.Windows.Forms.Cursors.SizeAll,
            "SizeNS" => System.Windows.Forms.Cursors.SizeNS,
            "SizeWE" => System.Windows.Forms.Cursors.SizeWE,
            "SizeNWSE" => System.Windows.Forms.Cursors.SizeNWSE,
            "SizeNESW" => System.Windows.Forms.Cursors.SizeNESW,
            "Cross" => System.Windows.Forms.Cursors.Cross,
            "Help" => System.Windows.Forms.Cursors.Help,
            "AppStarting" => System.Windows.Forms.Cursors.AppStarting,
            "UpArrow" => System.Windows.Forms.Cursors.UpArrow,
            _ => null,
        };
    }
}
