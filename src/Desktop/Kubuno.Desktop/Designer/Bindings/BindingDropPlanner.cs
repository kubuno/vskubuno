using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Kubuno.Desktop.Logic.DataSources;
using Kubuno.Desktop.Designer.Registry;

namespace Kubuno.Desktop.Designer.Bindings
{
    /// <summary>An attribute a drop sets on an existing element.</summary>
    public sealed class BindingDropEdit
    {
        public BindingDropEdit(string elementId, string attribute, string value)
        {
            ElementId = elementId;
            Attribute = attribute;
            Value = value;
        }

        public string ElementId { get; }

        public string Attribute { get; }

        public string Value { get; }
    }

    /// <summary>What a drop of a bindable member does: bind an existing control, or insert a label and a bound control.</summary>
    public sealed class BindingDropPlan
    {
        public BindingDropPlan(IReadOnlyList<BindingDropEdit> edits, IReadOnlyList<(string ParentId, int Index, string Xml)> insertions, string description, string? selectElementId)
        {
            Edits = edits;
            Insertions = insertions;
            Description = description;
            SelectElementId = selectElementId;
        }

        public IReadOnlyList<BindingDropEdit> Edits { get; }

        public IReadOnlyList<(string ParentId, int Index, string Xml)> Insertions { get; }

        public string Description { get; }

        public string? SelectElementId { get; }
    }

    /// <summary>
    /// Dropping a bindable member on a view (docs/DESIGNER.md, "Data bindings"; Windows Forms' Data Sources window): onto an
    /// existing control, the member binds the control's default binding property (<c>Text</c> of a text field, <c>Checked</c> of a
    /// check box, <c>ItemsSource</c> of a list for a list member…); onto empty space, a label and a control of the member's shape are
    /// inserted, bound (two-way when the member is writable), below what they would cover. Pure.
    /// </summary>
    public static class BindingDropPlanner
    {
        /// <summary>The default binding property of the controls that have a clear one (WinForms' <c>DefaultBindingProperty</c>).</summary>
        private static readonly Dictionary<string, string> DefaultProperty = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CheckBox"] = "Checked",
            ["Switch"] = "On",
            ["RadioButton"] = "SelectedValue",
            ["NumericField"] = "Value",
            ["Slider"] = "Value",
            ["ProgressBar"] = "Value",
            ["DatePicker"] = "Date",
            ["ComboBox"] = "SelectedValue",
            ["Dropdown"] = "SelectedValue",
            ["ListBox"] = "SelectedIndex",
            ["PictureBox"] = "Image",
            ["ColorField"] = "Color",
        };

        /// <summary>The child of <paramref name="parentId"/> under the drop point (its <c>X</c>/<c>Y</c>/<c>Width</c>/<c>Height</c>), skipping containers that took the drop themselves.</summary>
        public static KbviewElement? HitTarget(string documentText, string parentId, double? x, double? y)
        {
            if (x is not { } px || y is not { } py || KbviewOutline.TryParse(documentText) is not { } outline || outline.Find(parentId) is not { } parent)
            {
                return null;
            }

            return parent.Children.LastOrDefault(c =>
                c.Number("X") is { } cx && c.Number("Y") is { } cy && px >= cx && py >= cy && px < cx + (c.Number("Width") ?? 100) && py < cy + (c.Number("Height") ?? 30));
        }

        /// <summary>The attribute a member of shape <paramref name="shape"/> binds on <paramref name="component"/>, or null when it has none fitting.</summary>
        public static string? TargetProperty(ComponentMeta component, BindingShape shape)
        {
            bool Has(string name) => component.Properties.Any(p => p.Name == name);
            if (shape == BindingShape.List)
            {
                return Has("ItemsSource") ? "ItemsSource" : null;
            }

            if (DefaultProperty.TryGetValue(component.Name, out var known) && Has(known))
            {
                return known;
            }

            // A project control's `#[default_property("…")]` (WinForms' DefaultBindingProperty), when it fits the member.
            if (component.DefaultProperty is { Length: > 0 } declared && component.Properties.FirstOrDefault(p => p.Name == declared) is { } dp && BindingShapes.Fits(shape, BindingShapes.Of(dp)) != false)
            {
                return declared;
            }

            foreach (var name in new[] { "Text", "Value", "Checked", "SelectedValue", "Image" })
            {
                if (Has(name))
                {
                    return name;
                }
            }

            // A custom control (a user control's `Title`): its own properties first - those of the control class, not the
            // inherited `Control` ones - that fit the member, the bindable ones before the others; never a reference to
            // another element.
            bool Usable(PropertyMeta p) => p.Browsable && !(p.Editor is { } e && (e.StartsWith("reference:", StringComparison.Ordinal) || e.StartsWith("class:", StringComparison.Ordinal)));
            var own = component.Properties.Where(p => p.InheritedFrom is null && Usable(p) && BindingShapes.Fits(shape, BindingShapes.Of(p)) != false).ToList();
            return (own.FirstOrDefault(p => p.Bindable) ?? own.FirstOrDefault())?.Name;
        }

        /// <summary>
        /// The plan of a drop of <paramref name="member"/> (its expression) at (<paramref name="x"/>, <paramref name="y"/>) of
        /// <paramref name="parentId"/>; null with an <paramref name="error"/> when nothing can be done.
        /// </summary>
        public static BindingDropPlan? Plan(string documentText, ComponentRegistry registry, BindingMember member, string parentId, double? x, double? y, out string? error)
        {
            error = null;
            var outline = KbviewOutline.TryParse(documentText);
            if (outline?.Find(parentId) is not { } parent)
            {
                error = DesignerText.IsFrench ? "La vue ne se lit pas : corrigez-la puis recommencez." : "The view does not parse: fix it, then try again.";
                return null;
            }

            // Onto a control: bind it.
            if (HitTarget(documentText, parentId, x, y) is { } target && registry.Find(target.Name) is { } component)
            {
                if (TargetProperty(component, member.Shape) is not { } attribute)
                {
                    error = DesignerText.IsFrench ? $"{target.Name} n'a pas de propriété à lier à {member.Name}." : $"{target.Name} has no property to bind to {member.Name}.";
                    return null;
                }

                var expression = Expression(member, twoWay: member.Writable && WritesBack(attribute));
                var label = target.XName ?? target.Name;
                return new BindingDropPlan(new[] { new BindingDropEdit(target.Id, attribute, expression) }, Array.Empty<(string, int, string)>(), BindingStrings.DroppedOn(member.Path, attribute, label), target.Id);
            }

            // Onto empty space: a label and a bound control.
            var (left, top) = x is { } px && y is { } py ? ((int)Math.Round(px), (int)Math.Round(py)) : DataSourceDropPlanner.DefaultDropPoint(outline, parent);
            top = DataSourceDropPlanner.FreeTop(parent, left, top, 340, 30);
            var (tag, property) = member.Shape switch
            {
                BindingShape.Bool => ("CheckBox", "Checked"),
                BindingShape.Number => ("NumericField", "Value"),
                BindingShape.List => ("ListBox", "ItemsSource"),
                BindingShape.Text when member.Writable => ("TextField", "Text"),
                _ => ("Label", "Text"),
            };
            var names = outline.Names();
            var baseName = Snake(member.Name) + "_" + Snake(tag);
            var name = baseName;
            for (var i = 1; names.Contains(name); i++)
            {
                name = baseName + i.ToString(CultureInfo.InvariantCulture);
            }

            var bound = Expression(member, twoWay: member.Writable && tag != "Label" && tag != "ListBox");
            var index = parent.Children.Count;
            var caption = Humanize(member.Name);
            var insertions = new List<(string ParentId, int Index, string Xml)>();
            var controlX = left;
            if (tag != "CheckBox")
            {
                insertions.Add((parentId, index++, $"<Label Text=\"{Escape(caption)} :\" X=\"{left}\" Y=\"{top + 4}\" Width=\"120\" Height=\"22\"/>"));
                controlX = left + 128;
            }

            var height = tag == "ListBox" ? 120 : 28;
            var extra = tag == "CheckBox" ? $" Text=\"{Escape(caption)}\"" : string.Empty;
            insertions.Add((parentId, index, $"<{tag} x:Name=\"{name}\"{extra} {property}=\"{Escape(bound)}\" X=\"{controlX}\" Y=\"{top}\" Width=\"200\" Height=\"{height}\"/>"));
            var id = (parentId.Length == 0 ? string.Empty : parentId + ".") + index.ToString(CultureInfo.InvariantCulture);
            return new BindingDropPlan(Array.Empty<BindingDropEdit>(), insertions, (DesignerText.IsFrench ? "Ajouter " : "Add ") + member.Path, id);
        }

        /// <summary>Whether a control writes <paramref name="attribute"/> back when the user changes it (a two-way binding is useful).</summary>
        private static bool WritesBack(string attribute) => attribute is "Text" or "Checked" or "On" or "Value" or "SelectedValue" or "SelectedIndex" or "Date" or "Color";

        /// <summary>The member's expression, two-way when asked.</summary>
        private static string Expression(BindingMember member, bool twoWay)
        {
            if (!twoWay || !BindingMarkup.TryParse(member.Expression, out var markup))
            {
                return member.Expression;
            }

            return markup!.Set("Mode", "TwoWay").ToString();
        }

        private static string Snake(string name)
        {
            var chars = new List<char>();
            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (char.IsUpper(c) && i > 0 && name[i - 1] != '_' && !char.IsUpper(name[i - 1]))
                {
                    chars.Add('_');
                }

                chars.Add(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_');
            }

            return new string(chars.ToArray()).Trim('_');
        }

        /// <summary><c>StatusText</c> → <c>Status text</c>.</summary>
        public static string Humanize(string name)
        {
            var words = Snake(name).Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
            var text = string.Join(" ", words);
            return text.Length == 0 ? name : char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        private static string Escape(string value) => value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;");
    }
}
