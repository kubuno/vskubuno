using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Design;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms.Design;
using Kubuno.Desktop.Designer.PropertyBrowser;
using Kubuno.Desktop.Designer.Registry;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Desktop.Designer.Bindings
{
    /// <summary>
    /// The editor of every bindable row of the Properties window (docs/DESIGNER.md, "Data bindings"): the value cell gets a
    /// drop-down arrow that opens the binding picker (<see cref="BindingPickerView"/>) - with the property's literal values
    /// and its own editor (« Modifier la valeur… ») one click away -, and a binding marker painted in front of a bound
    /// value (gold, like WPF's; amber with « ! » when the language server reports a problem; teal for a <c>{Res …}</c>).
    /// The row's own editor (colour, image, icon…) keeps painting its preview of a literal value.
    /// </summary>
    public sealed class KbviewBindableEditor : UITypeEditor
    {
        public KbviewBindableEditor(UITypeEditor? inner, PropKind? kind)
        {
            Inner = inner;
            Kind = kind;
        }

        /// <summary>The row's own editor (the colour picker…), reached through « Modifier la valeur… ».</summary>
        public UITypeEditor? Inner { get; }

        public PropKind? Kind { get; }

        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) => UITypeEditorEditStyle.DropDown;

        public override bool IsDropDownResizable => true;

        public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider? provider, object? value)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var element = RichEditors.Elements(context).FirstOrDefault();
            var service = provider?.GetService(typeof(IWindowsFormsEditorService)) as IWindowsFormsEditorService;
            var attribute = AttributeOf(context, element);
            if (element is null || service is null || attribute is null)
            {
                return Inner?.EditValue(context, provider, value) ?? value;
            }

            var current = value as string;
            var schema = BindingActions.Schema(element);
            var literals = Literals(context);
            var view = new BindingPickerControl(schema, BindingActions.Want(element, attribute, Kind), current, literals, schema.IssuesOf(attribute), Inner is not null);
            object? result = value;
            Action? then = null;
            view.Picked += v =>
            {
                result = v;
                service.CloseDropDown();
            };
            view.LiteralPicked += v =>
            {
                result = v;
                service.CloseDropDown();
            };
            view.Cancelled += service.CloseDropDown;
            view.AdvancedRequested += () =>
            {
                then = () =>
                {
                    if (BindingActions.ShowDialog(element, attribute, Kind))
                    {
                        result = element.GetRawValue(attribute) ?? string.Empty;
                    }
                };
                service.CloseDropDown();
            };
            view.EditValueRequested += () =>
            {
                then = () => result = Inner?.EditValue(context, provider, BindingMarkup.IsMarkupExtension(current) ? string.Empty : value) ?? value;
                service.CloseDropDown();
            };
            view.GoToDefinitionRequested += () =>
            {
                then = () => BindingActions.GoToDefinition(element, attribute);
                service.CloseDropDown();
            };
            view.RemoveRequested += () =>
            {
                then = () =>
                {
                    BindingActions.RemoveBinding(element, attribute);
                    result = element.GetRawValue(attribute) ?? string.Empty;
                };
                service.CloseDropDown();
            };

            // Not disposed here: the grid's drop-down holder still parents it until its own cleanup (a handle destroyed under
            // another DPI context than the holder's brings Windows Forms' parking window down).
            try
            {
                service.DropDownControl(view);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or ObjectDisposedException or System.ComponentModel.Win32Exception)
            {
                Kubuno.Core.Logging.KubunoLog.WriteException("Kubuno: the binding picker failed", ex);
                return value;
            }

            then?.Invoke();
            return result;
        }

        /// <summary>The attribute the row edits on <paramref name="element"/>.</summary>
        private static string? AttributeOf(ITypeDescriptorContext? context, KbviewElementObject? element) => context?.PropertyDescriptor switch
        {
            KbviewAttributePropertyDescriptor row when element is not null => row.AttributeOf(element),
            PropertyDescriptor other => other.Name,
            _ => null,
        };

        /// <summary>The row's literal values (true/false, an enumeration's variants), when it has some.</summary>
        private static IReadOnlyList<string> Literals(ITypeDescriptorContext? context)
        {
            var converter = context?.PropertyDescriptor?.Converter;
            if (converter is null || !converter.GetStandardValuesSupported(context))
            {
                return Array.Empty<string>();
            }

            return converter.GetStandardValues(context)?.Cast<object>().Select(v => Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty).Where(v => v.Length > 0).ToList()
                ?? (IReadOnlyList<string>)Array.Empty<string>();
        }

        public override bool GetPaintValueSupported(ITypeDescriptorContext? context)
        {
            if (context?.PropertyDescriptor is { } row && context.Instance is { } instance && BindingMarkup.IsMarkupExtension(row.GetValue(instance) as string))
            {
                return true;
            }

            return Inner?.GetPaintValueSupported(context) ?? false;
        }

        public override void PaintValue(PaintValueEventArgs e)
        {
            var raw = e.Value as string;
            if (!BindingMarkup.IsMarkupExtension(raw))
            {
                if (Inner?.GetPaintValueSupported(e.Context) == true)
                {
                    Inner.PaintValue(e);
                }

                return;
            }

            var issue = HasIssue(e.Context);
            var resource = BindingMarkup.IsResource(raw);
            PaintMarker(e.Graphics, e.Bounds, issue ? Marker.Issue : resource ? Marker.Resource : Marker.Binding);
        }

        private static bool HasIssue(ITypeDescriptorContext? context)
        {
            var element = RichEditors.Elements(context).FirstOrDefault();
            var attribute = AttributeOf(context, element);
            // The schema is cached per buffer version: painting asks the server once per edit.
            return element is not null && attribute is not null && BindingActions.Services(element)?.GetBindingSources(element.ElementId).IssuesOf(attribute).Any(i => i.Severity != "information") == true;
        }

        /// <summary>The marker's kinds.</summary>
        public enum Marker
        {
            Binding,
            Resource,
            Issue,
        }

        /// <summary>Paints the marker of a bound value in the grid's value box.</summary>
        public static void PaintMarker(Graphics g, Rectangle bounds, Marker marker)
        {
            var fill = marker switch
            {
                Marker.Issue => Color.FromArgb(232, 120, 24),
                Marker.Resource => Color.FromArgb(0, 140, 140),
                _ => Color.FromArgb(214, 168, 0),
            };
            var r = Rectangle.Inflate(bounds, -1, -1);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(fill))
            {
                g.FillRectangle(brush, r);
            }

            using var ink = new Pen(Color.White, 1.4f);
            var cy = r.Top + r.Height / 2f;
            switch (marker)
            {
                case Marker.Issue:
                    using (var bold = new Font(FontFamily.GenericSansSerif, Math.Max(6f, r.Height - 4), FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var white = new SolidBrush(Color.White))
                    {
                        var size = g.MeasureString("!", bold);
                        g.DrawString("!", bold, white, r.Left + (r.Width - size.Width) / 2f, r.Top + (r.Height - size.Height) / 2f);
                    }

                    break;
                case Marker.Resource:
                    using (var font = new Font(FontFamily.GenericSansSerif, Math.Max(6f, r.Height - 4), FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var white = new SolidBrush(Color.White))
                    {
                        var size = g.MeasureString("R", font);
                        g.DrawString("R", font, white, r.Left + (r.Width - size.Width) / 2f, r.Top + (r.Height - size.Height) / 2f);
                    }

                    break;
                default:
                    // Two chain links.
                    var w = Math.Max(4f, r.Width * 0.36f);
                    var h = Math.Max(3f, r.Height * 0.42f);
                    var cx = r.Left + r.Width / 2f;
                    g.DrawEllipse(ink, cx - w + 1, cy - h / 2f, w, h);
                    g.DrawEllipse(ink, cx - 1, cy - h / 2f, w, h);
                    break;
            }
        }
    }

    /// <summary>
    /// The text of a bindable row (docs/DESIGNER.md, "Data bindings"): a bound value shows its expression, then its
    /// design-time value (<c>d:</c>) and a short note of its problem - <c>{Binding Title} · Stockage · ⚠ chemin inconnu</c>;
    /// what is typed back is read up to the expression's closing brace. Literal values go through the row's own converter.
    /// </summary>
    public sealed class KbviewBindingDisplayConverter : TypeConverter
    {
        private const string Separator = "  ·  ";

        public KbviewBindingDisplayConverter(TypeConverter inner)
        {
            Inner = inner;
        }

        public TypeConverter Inner { get; }

        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string) || Inner.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) => destinationType == typeof(string) || Inner.CanConvertTo(context, destinationType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            if (value is string text && Strip(text) is { } expression)
            {
                return expression;
            }

            return Inner.ConvertFrom(context, culture, value);
        }

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            if (destinationType == typeof(string) && value is string raw && BindingMarkup.IsMarkupExtension(raw))
            {
                return Display(context, raw);
            }

            return Inner.ConvertTo(context, culture, value, destinationType);
        }

        /// <summary>The expression of a displayed text (<c>{…}</c> up to its brace), null when it is not one.</summary>
        public static string? Strip(string text)
        {
            var t = text.Trim();
            if (!t.StartsWith("{", StringComparison.Ordinal))
            {
                return null;
            }

            var at = t.IndexOf("}" + Separator.TrimEnd(), StringComparison.Ordinal);
            return at > 0 ? t.Substring(0, at + 1) : null;
        }

        private static string Display(ITypeDescriptorContext? context, string raw)
        {
            var element = RichEditors.Elements(context).FirstOrDefault();
            if (element is null || context?.PropertyDescriptor is not KbviewAttributePropertyDescriptor row)
            {
                return raw;
            }

            var attribute = row.AttributeOf(element);
            var parts = new List<string> { raw };
            if (element.GetRawValue("d:" + attribute) is { Length: > 0 } design)
            {
                parts.Add(design);
            }

            if (BindingActions.Services(element)?.GetBindingSources(element.ElementId).IssuesOf(attribute).FirstOrDefault(i => i.Severity != "information") is { } issue)
            {
                parts.Add("⚠ " + BindingStrings.IssueShort(issue.Code));
            }

            return string.Join(Separator, parts);
        }

        public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => Inner.GetStandardValuesSupported(context);

        public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => false;

        public override StandardValuesCollection? GetStandardValues(ITypeDescriptorContext? context) => Inner.GetStandardValues(context);

        public override bool GetPropertiesSupported(ITypeDescriptorContext? context) => Inner.GetPropertiesSupported(context);

        public override PropertyDescriptorCollection? GetProperties(ITypeDescriptorContext? context, object value, Attribute[]? attributes) => Inner.GetProperties(context, value, attributes);
    }
}
