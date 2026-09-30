using System;
using System.Collections.Generic;
using Kubuno.Desktop.Designer.Registry;

namespace Kubuno.Desktop.Designer.PropertyBrowser
{
    /// <summary>
    /// Which Properties-window category a <c>.kbview</c> attribute is listed under, WinForms-style
    /// ("Appearance", "Behavior", "Layout", "Data", "Design", "Misc"). The component registry carries no
    /// category of its own (<c>kubuno/registry</c>'s <c>properties[]</c> is <c>{name, kind, default,
    /// doc}</c> only), so this is a small, explicit name table - mirroring the categories the WinForms
    /// designer uses for the equivalent <c>Control</c> properties (<c>Text</c>/<c>Font</c>/<c>Icon</c> in
    /// Appearance, <c>Enabled</c>/<c>Checked</c> in Behavior, <c>Dock</c>/<c>Size</c> in Layout, bound
    /// data members in Data) - with a kind-based fallback (a <c>Bool</c> is a behavior switch) and
    /// "Misc" for anything else, exactly where WinForms puts uncategorized properties.
    /// </summary>
    public static class PropertyCategoryMap
    {
        public enum Category
        {
            Design,
            Layout,
            Appearance,
            Behavior,
            Data,
            Misc,
            Accessibility,
            Focus,
            WindowStyle,
        }

        private static readonly Dictionary<string, Category> ByName = new Dictionary<string, Category>(StringComparer.Ordinal)
        {
            // Layout: the common attributes every element accepts (read by the parent layout engine)
            // plus the container-level sizing/spacing properties.
            ["Dock"] = Category.Layout,
            ["Anchor"] = Category.Layout,
            ["X"] = Category.Layout,
            ["Y"] = Category.Layout,
            ["Width"] = Category.Layout,
            ["Height"] = Category.Layout,
            ["MaxWidth"] = Category.Layout,
            ["Padding"] = Category.Layout,
            ["Gap"] = Category.Layout,
            ["Direction"] = Category.Layout,
            ["Orientation"] = Category.Layout,
            ["Align"] = Category.Layout,
            ["Distance"] = Category.Layout,
            ["Band"] = Category.Layout,
            ["Overflow"] = Category.Layout,
            ["Dense"] = Category.Layout,
            ["Compact"] = Category.Layout,
            ["Flush"] = Category.Layout,

            // Appearance: what the control shows and how it looks.
            ["Text"] = Category.Appearance,
            ["Label"] = Category.Appearance,
            ["Title"] = Category.Appearance,
            ["Subtitle"] = Category.Appearance,
            ["Header"] = Category.Appearance,
            ["Description"] = Category.Appearance,
            ["Placeholder"] = Category.Appearance,
            ["Body"] = Category.Appearance,
            ["Icon"] = Category.Appearance,
            ["Glyph"] = Category.Appearance,
            ["Variant"] = Category.Appearance,
            ["Size"] = Category.Appearance,
            ["Surface"] = Category.Appearance,
            ["Color"] = Category.Appearance,
            ["Corner"] = Category.Appearance,
            ["Diameter"] = Category.Appearance,
            ["Filled"] = Category.Appearance,
            ["Dot"] = Category.Appearance,
            ["Status"] = Category.Appearance,
            ["Role"] = Category.Appearance,
            ["ShowIcon"] = Category.Appearance,
            ["ShowValue"] = Category.Appearance,
            ["ActionLabel"] = Category.Appearance,
            ["SecondaryActionLabel"] = Category.Appearance,
            ["Format"] = Category.Appearance,
            ["Mask"] = Category.Appearance,

            // Data: bound values and item sources.
            ["ItemsSource"] = Category.Data,
            ["DisplayMember"] = Category.Data,
            ["ValueMember"] = Category.Data,
            ["Value"] = Category.Data,
            ["SelectedIndex"] = Category.Data,
            ["SelectedValue"] = Category.Data,
            ["SelectedPath"] = Category.Data,
            ["SelectedDate"] = Category.Data,
            ["Date"] = Category.Data,
            ["CurrentIndex"] = Category.Data,
            ["Binding"] = Category.Data,
            ["Min"] = Category.Data,
            ["Max"] = Category.Data,
            ["Step"] = Category.Data,
            ["LargeStep"] = Category.Data,
        };

        /// <summary>The category for attribute <paramref name="name"/> (of kind <paramref name="kind"/>, may be null for the free-form <c>x:Name</c>).</summary>
        public static Category For(string name, PropKind? kind)
        {
            if (string.Equals(name, "x:Name", StringComparison.Ordinal))
            {
                return Category.Design;
            }

            if (name is not null && ByName.TryGetValue(name, out var category))
            {
                return category;
            }

            return kind?.Tag == PropKindTag.Bool ? Category.Behavior : Category.Misc;
        }

        /// <summary>
        /// The display name of a category a project control names (<c>#[category("Appearance")]</c>, EVT-7b): a standard
        /// one is localized like WinForms' <c>CategoryAttribute</c> ("Appearance" shows "Apparence" in a French Visual
        /// Studio), any other is shown as written.
        /// </summary>
        public static string DisplayName(string category)
        {
            // The printing components' own category (docs/PRINTING.md), localized like the standard ones.
            if (string.Equals(category, "Printing", StringComparison.OrdinalIgnoreCase))
            {
                return DesignerText.CategoryPrinting;
            }

            foreach (Category known in Enum.GetValues(typeof(Category)))
            {
                if (string.Equals(known.ToString(), (category ?? string.Empty).Replace(" ", string.Empty), StringComparison.OrdinalIgnoreCase))
                {
                    return DisplayName(known);
                }
            }

            return category ?? string.Empty;
        }

        /// <summary>Localized display name of <paramref name="category"/> (see <see cref="DesignerText"/>).</summary>
        public static string DisplayName(Category category)
        {
            switch (category)
            {
                case Category.Design: return DesignerText.CategoryDesign;
                case Category.Layout: return DesignerText.CategoryLayout;
                case Category.Appearance: return DesignerText.CategoryAppearance;
                case Category.Behavior: return DesignerText.CategoryBehavior;
                case Category.Data: return DesignerText.CategoryData;
                case Category.Accessibility: return DesignerText.CategoryAccessibility;
                case Category.Focus: return DesignerText.CategoryFocus;
                case Category.WindowStyle: return DesignerText.CategoryWindowStyle;
                default: return DesignerText.CategoryMisc;
            }
        }
    }
}
