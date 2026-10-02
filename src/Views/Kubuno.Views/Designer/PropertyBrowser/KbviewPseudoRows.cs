using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing.Design;
using System.Globalization;
using System.Linq;
using Kubuno.Desktop.Designer.Properties;
using Kubuno.Desktop.Designer.Registry;
using Kubuno.Desktop.Designer.Selection;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Desktop.Designer.PropertyBrowser
{
    /// <summary>The <c>{Binding ...}</c> text a binding editor writes. Pure.</summary>
    public static class BindingText
    {
        /// <summary>The binding modes, the default (<c>OneWay</c>, not written) first.</summary>
        public static IReadOnlyList<string> Modes { get; } = Bindings.BindingMarkup.Modes;

        /// <summary><c>{Binding Path}</c>, or <c>{Binding Path, Mode=TwoWay}</c>; empty for an empty path.</summary>
        public static string Build(string? path, string? mode)
        {
            var p = (path ?? string.Empty).Trim();
            if (p.Length == 0)
            {
                return string.Empty;
            }

            var m = string.IsNullOrWhiteSpace(mode) || string.Equals(mode!.Trim(), "OneWay", StringComparison.OrdinalIgnoreCase) ? null : Modes.FirstOrDefault(x => string.Equals(x, mode!.Trim(), StringComparison.OrdinalIgnoreCase)) ?? mode!.Trim();
            return BindingExpressionParser.Format(new BindingExpression(p, m));
        }

        /// <summary>A binding typed in a row: a full <c>{Binding ...}</c>, or a bare path (<c>Status</c>) that is wrapped; empty stays empty.</summary>
        public static string FromTyped(string? text)
        {
            var t = (text ?? string.Empty).Trim();
            if (t.Length == 0 || Bindings.BindingMarkup.IsMarkupExtension(t))
            {
                return t;
            }

            if (t.IndexOfAny(new[] { '{', '}', ',', '"', '<' }) >= 0)
            {
                throw new ArgumentException(DesignerText.InvalidComposite(t, "{Binding Status}"));
            }

            return Build(t, null);
        }

        /// <summary>The <c>paths</c> of a <c>kubuno/bindingPaths</c> answer (<c>{"paths": ["Status", ...]}</c>), distinct, in order; empty for anything else.</summary>
        public static IReadOnlyList<string> ParsePaths(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return Array.Empty<string>();
            }

            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(json!);
                if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("paths", out var paths)
                    || paths.ValueKind != System.Text.Json.JsonValueKind.Array)
                {
                    return Array.Empty<string>();
                }

                return paths.EnumerateArray()
                    .Where(p => p.ValueKind == System.Text.Json.JsonValueKind.String)
                    .Select(p => p.GetString() ?? string.Empty)
                    .Where(p => p.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
            }
            catch (System.Text.Json.JsonException)
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>The path and mode of <paramref name="text"/> (<c>(null, OneWay)</c> when it is not a binding).</summary>
        public static (string? Path, string Mode) Parse(string? text) =>
            BindingExpressionParser.TryParse(text, out var expression) && expression is not null
                ? (expression.Path, expression.Mode ?? "OneWay")
                : (null, "OneWay");
    }

    /// <summary>The value of the "(DataBindings)" row: the element whose bindings its sub-rows edit.</summary>
    public sealed class KbviewBindingsValue
    {
        public KbviewBindingsValue(KbviewElementObject? owner)
        {
            Owner = owner;
        }

        public KbviewElementObject? Owner { get; }

        public override bool Equals(object? obj) => obj is KbviewBindingsValue;

        public override int GetHashCode() => 1;

        public override string ToString() => string.Empty;
    }

    /// <summary>
    /// The "(DataBindings)" row in Data, like WinForms': expanded, one sub-row per bindable property (and <c>Text</c>, <c>Tag</c>)
    /// showing its binding, plus "(Advanced)" for every other property.
    /// </summary>
    public sealed class KbviewBindingsPropertyDescriptor : PropertyDescriptor
    {
        public const string RowName = "(DataBindings)";

        public KbviewBindingsPropertyDescriptor(IReadOnlyList<PropertyMeta> bindable, IReadOnlyList<PropertyMeta> all)
            : base(RowName, new Attribute[]
            {
                new CategoryAttribute(PropertyCategoryMap.DisplayName(PropertyCategoryMap.Category.Data)),
                new DescriptionAttribute(DesignerText.DataBindingsDoc),
                new DisplayNameAttribute("DataBindings"),
                new ParenthesizePropertyNameAttribute(true),
                new RefreshPropertiesAttribute(RefreshProperties.Repaint),
            })
        {
            Bindable = bindable;
            All = all;
        }

        public IReadOnlyList<PropertyMeta> Bindable { get; }

        public IReadOnlyList<PropertyMeta> All { get; }

        public override Type ComponentType => typeof(KbviewElementObject);

        public override bool IsReadOnly => false;

        public override Type PropertyType => typeof(KbviewBindingsValue);

        public override TypeConverter Converter => new KbviewBindingsConverter(this);

        public override object GetValue(object? component) => new KbviewBindingsValue(component as KbviewElementObject);

        public override void SetValue(object? component, object? value)
        {
        }

        public override bool CanResetValue(object component) => false;

        public override void ResetValue(object component)
        {
        }

        public override bool ShouldSerializeValue(object component) =>
            component is KbviewElementObject element && Bindable.Any(p => BindingExpressionParser.IsBindingExpression(element.GetRawValue(KbviewBindingPartDescriptor.AttributeOf(p, element))));

        /// <summary>The properties "(DataBindings)" lists directly: the registry's bindable ones, plus <c>Text</c> and <c>Tag</c>.</summary>
        public static IReadOnlyList<PropertyMeta> BindableOf(IEnumerable<PropertyMeta> properties) =>
            properties.Where(p => p.Browsable && (p.Bindable || p.Name == "Text" || p.Name == "Tag")).ToList();
    }

    /// <summary>The "(DataBindings)" row's sub-rows.</summary>
    public sealed class KbviewBindingsConverter : TypeConverter
    {
        private readonly KbviewBindingsPropertyDescriptor _row;

        public KbviewBindingsConverter(KbviewBindingsPropertyDescriptor row)
        {
            _row = row;
        }

        public override bool GetPropertiesSupported(ITypeDescriptorContext? context) => true;

        public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext? context, object value, Attribute[]? attributes) =>
            new PropertyDescriptorCollection(
                Listed(value as KbviewBindingsValue).Select(p => (PropertyDescriptor)new KbviewBindingPartDescriptor(p)).Concat(new[] { new KbviewAdvancedBindingsDescriptor(_row.All) }).ToArray(),
                readOnly: true);

        /// <summary>The properties listed: the bindable ones (and Text, Tag), then every other property bound now - a custom control's
        /// <c>{Binding …}</c> properties show here even when they are not declared bindable.</summary>
        public IReadOnlyList<PropertyMeta> Listed(KbviewBindingsValue? value)
        {
            if (value?.Owner is not { } owner)
            {
                return _row.Bindable;
            }

            var bound = _row.All.Where(p => !_row.Bindable.Contains(p) && Bindings.BindingMarkup.IsMarkupExtension(owner.GetRawValue(KbviewBindingPartDescriptor.AttributeOf(p, owner))));
            return _row.Bindable.Concat(bound).ToList();
        }

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) => destinationType == typeof(string);

        /// <summary>WinForms shows nothing; here, how many properties are bound (« 3 liaisons »), the quick summary.</summary>
        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            if (value is not KbviewBindingsValue { Owner: { } owner })
            {
                return string.Empty;
            }

            var count = _row.All.Count(p => Bindings.BindingMarkup.IsMarkupExtension(owner.GetRawValue(KbviewBindingPartDescriptor.AttributeOf(p, owner))));
            return count == 0 ? string.Empty : DesignerText.IsFrench ? (count == 1 ? "1 liaison" : count + " liaisons") : (count == 1 ? "1 binding" : count + " bindings");
        }
    }

    /// <summary>One property's binding under "(DataBindings)": its <c>{Binding ...}</c>, or empty when the property has a plain value.</summary>
    public sealed class KbviewBindingPartDescriptor : PropertyDescriptor
    {
        private readonly PropertyMeta _property;

        public KbviewBindingPartDescriptor(PropertyMeta property)
            : base(property.Name, new Attribute[]
            {
                new DescriptionAttribute(DesignerText.BindingPartDoc(property.Name)),
                new RefreshPropertiesAttribute(RefreshProperties.Repaint),
            })
        {
            _property = property;
        }

        public override TypeConverter Converter => new Bindings.KbviewBindingDisplayConverter(new StringConverter());

        public override Type ComponentType => typeof(KbviewBindingsValue);

        public override bool IsReadOnly => false;

        public override Type PropertyType => typeof(string);

        /// <summary>The binding picker (docs/DESIGNER.md, "Data bindings"), like every bindable row.</summary>
        public override object? GetEditor(Type editorBaseType) => editorBaseType == typeof(UITypeEditor) ? new Bindings.KbviewBindableEditor(null, _property.Kind) : base.GetEditor(editorBaseType);

        /// <summary>The attribute holding <paramref name="property"/> on <paramref name="element"/> (its canonical name or an older one in use).</summary>
        public static string AttributeOf(PropertyMeta property, KbviewElementObject element) =>
            property.AttributeNames.FirstOrDefault(a => element.GetRawValue(a) is not null) ?? property.Name;

        public override object GetValue(object? component)
        {
            if (component is not KbviewBindingsValue { Owner: { } owner })
            {
                return string.Empty;
            }

            var raw = owner.GetRawValue(AttributeOf(_property, owner));
            return Bindings.BindingMarkup.IsMarkupExtension(raw) ? raw! : string.Empty;
        }

        public override void SetValue(object? component, object? value)
        {
            if (component is not KbviewBindingsValue { Owner: { } owner })
            {
                return;
            }

            var attribute = AttributeOf(_property, owner);
            var current = owner.GetRawValue(attribute);
            // The picker already wrote it (« Supprimer la liaison » restores a literal value).
            if (string.Equals(value as string, current, StringComparison.Ordinal))
            {
                return;
            }

            var text = BindingText.FromTyped(value as string);
            if (text.Length == 0)
            {
                if (Bindings.BindingMarkup.IsMarkupExtension(current))
                {
                    owner.RemoveAttribute(attribute);
                }

                return;
            }

            if (!string.Equals(text, current, StringComparison.Ordinal))
            {
                owner.SetAttribute(attribute, text);
            }
        }

        public override bool CanResetValue(object component) => ShouldSerializeValue(component);

        public override void ResetValue(object component) => SetValue(component, string.Empty);

        public override bool ShouldSerializeValue(object component) => GetValue(component) is string { Length: > 0 };
    }

    /// <summary>"(Advanced)" under "(DataBindings)": opens the list of every property to bind any of them.</summary>
    public sealed class KbviewAdvancedBindingsDescriptor : PropertyDescriptor
    {
        public KbviewAdvancedBindingsDescriptor(IReadOnlyList<PropertyMeta> all)
            : base("(Advanced)", new Attribute[]
            {
                new DisplayNameAttribute(DesignerText.AdvancedBindingsName),
                new DescriptionAttribute(DesignerText.AdvancedBindingsDoc),
                new RefreshPropertiesAttribute(RefreshProperties.Repaint),
            })
        {
            All = all;
        }

        public IReadOnlyList<PropertyMeta> All { get; }

        public override Type ComponentType => typeof(KbviewBindingsValue);

        public override bool IsReadOnly => false;

        public override Type PropertyType => typeof(string);

        public override object? GetEditor(Type editorBaseType) => editorBaseType == typeof(UITypeEditor) ? new KbviewAdvancedBindingsEditor() : base.GetEditor(editorBaseType);

        public override object GetValue(object? component) => string.Empty;

        public override void SetValue(object? component, object? value)
        {
        }

        public override bool CanResetValue(object component) => false;

        public override void ResetValue(object component)
        {
        }

        public override bool ShouldSerializeValue(object component) => false;
    }

    /// <summary>
    /// "(Advanced)"'s editor (WinForms' "Formatting and Advanced Binding"): the element's properties with their bindings; the
    /// chosen one opens « Liaison de données » (docs/DESIGNER.md, "Data bindings").
    /// </summary>
    public sealed class KbviewAdvancedBindingsEditor : UITypeEditor
    {
        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) => UITypeEditorEditStyle.Modal;

        public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider? provider, object? value)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var element = RichEditors.Elements(context).FirstOrDefault();
            var all = (context?.PropertyDescriptor as KbviewAdvancedBindingsDescriptor)?.All;
            if (element is null || all is null)
            {
                return value;
            }

            var rows = all.Select(p => (p, KbviewBindingPartDescriptor.AttributeOf(p, element))).Select(x => (x.Item2, element.GetRawValue(x.Item2))).ToList();
            var chooser = new Bindings.BindingPropertyChooser(rows);
            if (chooser.Ask() && chooser.Chosen is { } attribute)
            {
                Bindings.BindingActions.ShowDialog(element, attribute, all.FirstOrDefault(p => p.Name == attribute || p.AttributeNames.Contains(attribute))?.Kind);
            }

            return value;
        }
    }

    /// <summary>
    /// A container's children of one kind as a Properties-window row (<c>Columns</c>, <c>TabPages</c>, <c>Items</c>...),
    /// edited with the collection editor or - for the plain item lists of <c>ListBox</c>/<c>ComboBox</c>... - the string
    /// list editor.
    /// </summary>
    public sealed class KbviewChildrenPropertyDescriptor : PropertyDescriptor
    {
        public KbviewChildrenPropertyDescriptor(string rowName, string childTag, bool stringList, string category)
            : base(rowName, new Attribute[]
            {
                new CategoryAttribute(category),
                new DescriptionAttribute(stringList ? DesignerText.StringListDoc : DesignerText.CollectionDoc(rowName, childTag)),
                new DisplayNameAttribute(rowName),
                new RefreshPropertiesAttribute(RefreshProperties.Repaint),
            })
        {
            ChildTag = childTag;
            ChildTags = new[] { childTag };
            StringList = stringList;
        }

        /// <summary>A POLYMORPHIC collection row (a ribbon group's <c>Items</c>, docs/RIBBON.md section 9): its members are the children of any of <paramref name="childTags"/>.</summary>
        public KbviewChildrenPropertyDescriptor(string rowName, IReadOnlyList<string> childTags, string category)
            : this(rowName, childTags[0], stringList: false, category)
        {
            ChildTags = childTags;
        }

        /// <summary>The children's tag (<c>Column</c>, <c>Option</c>...); the first one of a polymorphic row.</summary>
        public string ChildTag { get; }

        /// <summary>Every tag the row's members may have (one, but for a polymorphic row).</summary>
        public IReadOnlyList<string> ChildTags { get; private set; }

        /// <summary>Edited as lines of text (<c>Option</c>/<c>Item</c> of a list control) rather than with the collection editor.</summary>
        public bool StringList { get; }

        public override Type ComponentType => typeof(KbviewElementObject);

        public override bool IsReadOnly => false;

        public override Type PropertyType => typeof(string);

        public override object? GetEditor(Type editorBaseType) =>
            editorBaseType == typeof(UITypeEditor) ? (StringList ? new KbviewStringListEditor() : new KbviewCollectionEditor()) : base.GetEditor(editorBaseType);

        public override object GetValue(object? component) => DesignerText.CollectionValue;

        public override void SetValue(object? component, object? value)
        {
        }

        public override bool CanResetValue(object component) => false;

        public override void ResetValue(object component)
        {
        }

        public override bool ShouldSerializeValue(object component) =>
            component is KbviewElementObject element && ViewDocument.Find(ViewDocument.Parse(element.Host.GetCurrentText()), element.ElementId)?.Children.Any(c => ChildTags.Contains(c.Name)) == true;

        /// <summary>The children rows of <paramref name="component"/>: a string list for a list control's items, a collection row per other kind of gated child.</summary>
        public static IEnumerable<KbviewChildrenPropertyDescriptor> RowsFor(ComponentMeta component)
        {
            var allowed = component.AllowedChildren ?? new List<string>();
            var category = PropertyCategoryMap.DisplayName(PropertyCategoryMap.Category.Behavior);
            if (Ribbon.RibbonDesignerTasks.IsRibbon(component))
            {
                // A ribbon element: ONE polymorphic row (a group's Items mix buttons, toggles, galleries...).
                if (Ribbon.RibbonDesignerTasks.CollectionOf(component) is { } collection)
                {
                    yield return new KbviewChildrenPropertyDescriptor(collection.Row, collection.Tags, category);
                }
                else if (allowed.Count == 1 && allowed[0] == "Option")
                {
                    yield return new KbviewChildrenPropertyDescriptor("Items", allowed[0], stringList: true, category);
                }

                yield break;
            }

            if (allowed.Count == 1 && (allowed[0] == "Option" || allowed[0] == "Item") && component.Name != "TreeView")
            {
                yield return new KbviewChildrenPropertyDescriptor("Items", allowed[0], stringList: true, category);
                yield break;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var child in allowed)
            {
                var row = RowName(child);
                if (seen.Add(row))
                {
                    yield return new KbviewChildrenPropertyDescriptor(row, child, stringList: false, category);
                }
            }
        }

        /// <summary>The row name of a kind of child, WinForms-style.</summary>
        public static string RowName(string childTag) => childTag switch
        {
            "Column" => "Columns",
            "TabItem" => "TabPages",
            "Step" => "Steps",
            "AccordionSection" => "Sections",
            "Item" or "Option" or "MenuItem" or "ToolbarItem" or "BreadcrumbItem" => "Items",
            _ => childTag + "s",
        };
    }

    /// <summary>The collection editor of a children row (see <see cref="UI.CollectionEditorDialog"/>).</summary>
    public sealed class KbviewCollectionEditor : UITypeEditor
    {
        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) =>
            RichEditors.Elements(context).Count == 1 ? UITypeEditorEditStyle.Modal : UITypeEditorEditStyle.None;

        public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider? provider, object? value)
        {
            var elements = RichEditors.Elements(context);
            if (elements.Count != 1 || context?.PropertyDescriptor is not KbviewChildrenPropertyDescriptor row || elements[0].Host is not IKbviewDesignServices services)
            {
                return value;
            }

            ThreadHelper.ThrowIfNotOnUIThread();
            Edit(elements[0].Host, services, elements[0].ElementId, row.Name, row.ChildTags, provider);
            return value;
        }

        /// <summary>
        /// Opens the collection editor on the <paramref name="tags"/> children of <paramref name="elementId"/> and applies the
        /// result as ONE undo unit - the Properties window's row, and a ribbon element's "Edit Items..." task.
        /// </summary>
        public static void Edit(IKbviewElementHost host, IKbviewDesignServices services, string elementId, string rowName, IReadOnlyList<string> tags, IServiceProvider? provider = null)
        {
            var version = host.CurrentVersion;
            var text = host.GetCurrentText();
            var parent = ViewDocument.Find(ViewDocument.Parse(text), elementId);
            if (parent is null || tags.Count == 0)
            {
                return;
            }

            var kinds = tags.Select(t => (t, host.Registry.Find(t))).ToList();
            var originals = parent.Children.Where(c => tags.Contains(c.Name)).ToList();
            // The members' bindings resolve like a member's (a DataTable's columns name fields of its rows): the schema of the
            // first member, else of the container.
            var schemaId = originals.FirstOrDefault()?.Id ?? elementId;
            var dialog = new UI.CollectionEditorDialog(rowName, kinds, originals)
            {
                IconViewFile = services.ViewFilePath,
                IconServices = services as Icons.IKbviewIconServices,
                BindingSchema = () => services.GetBindingSources(schemaId),
                BindingPreview = services.PreviewBinding,
            };
            ThreadHelper.ThrowIfNotOnUIThread();
            if (RichEditors.ShowDialog(provider, dialog))
            {
                var edits = ChildCollectionPlanner.Plan(text, elementId, tags, dialog.Members);
                if (edits.Count > 0)
                {
                    services.ApplyTextEdits(version, edits, "Edit " + rowName);
                }
            }
        }
    }

    /// <summary>The string list editor of a list control's items (see <see cref="UI.StringListDialog"/>).</summary>
    public sealed class KbviewStringListEditor : UITypeEditor
    {
        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) =>
            RichEditors.Elements(context).Count == 1 ? UITypeEditorEditStyle.Modal : UITypeEditorEditStyle.None;

        public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider? provider, object? value)
        {
            var elements = RichEditors.Elements(context);
            if (elements.Count != 1 || context?.PropertyDescriptor is not KbviewChildrenPropertyDescriptor row || elements[0].Host is not IKbviewDesignServices services)
            {
                return value;
            }

            var element = elements[0];
            var version = element.Host.CurrentVersion;
            var text = element.Host.GetCurrentText();
            var childMeta = element.Host.Registry.Find(row.ChildTag);
            var lines = StringListPlanner.Lines(text, element.ElementId, row.ChildTag, StringListPlanner.LabelAttribute(childMeta), StringListPlanner.ValueAttribute(childMeta));
            var dialog = new UI.StringListDialog(lines);
            ThreadHelper.ThrowIfNotOnUIThread();
            if (RichEditors.ShowDialog(provider, dialog))
            {
                var edits = StringListPlanner.Plan(text, element.ElementId, row.ChildTag, childMeta, dialog.Lines);
                if (edits.Count > 0)
                {
                    services.ApplyTextEdits(version, edits, "Edit Items");
                }
            }

            return value;
        }
    }
}
