using System;
using System.Collections.Generic;

namespace Kubuno.VisualStudio.Core.SolutionExplorer
{
    /// <summary>
    /// Picks the Visual Studio image catalog moniker (a <c>KnownMonikers</c> property name) for a
    /// Solution Explorer symbol node, the way Roslyn does for C#: one glyph family per kind
    /// (<c>Structure</c>, <c>Enumeration</c>, <c>Interface</c>, <c>Method</c>...) and one variant per
    /// accessibility (<c>...Public</c> = no overlay, <c>...Internal</c> = Friend heart,
    /// <c>...Protected</c> = star, <c>...Private</c> = lock). Kept as names so the mapping is
    /// unit-testable without the image catalog; the VSIX resolves them once by reflection.
    /// </summary>
    public static class SymbolMonikerNames
    {
        /// <summary>Used when a name cannot be resolved (never expected; guards against catalog changes).</summary>
        public const string Fallback = "Type";

        private static readonly Dictionary<string, string> ElementFamilies = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Containers / layout.
            ["Card"] = "Panel",
            ["Panel"] = "Panel",
            ["Stack"] = "StackPanel",
            ["Grid"] = "Grid",
            ["Dock"] = "DockPanel",
            ["Wrap"] = "WrapPanel",
            ["GroupBox"] = "GroupBox",
            ["ScrollArea"] = "ScrollViewer",
            ["Splitter"] = "GridSplitter",
            ["Accordion"] = "Expander",
            ["AccordionSection"] = "Expander",
            ["Tabs"] = "Tab",
            ["TabItem"] = "Tab",
            ["Toolbar"] = "ToolBar",
            ["ToolbarItem"] = "MenuItem",
            ["Separator"] = "MenuSeparator",

            // Commands.
            ["Button"] = "Button",
            ["IconButton"] = "ImageButton",
            ["LinkLabel"] = "HyperLink",
            ["Breadcrumb"] = "LinkButton",
            ["BreadcrumbItem"] = "LinkButton",
            ["Stepper"] = "ButtonGroup",
            ["Step"] = "Button",

            // Choices.
            ["CheckBox"] = "CheckBoxChecked",
            ["Switch"] = "ToggleButton",
            ["RadioButton"] = "RadioButton",
            ["CheckedListBox"] = "CheckBoxList",
            ["ComboBox"] = "ComboBox",
            ["Dropdown"] = "ComboBox",
            ["Option"] = "ComboBoxItem",
            ["Item"] = "ComboBoxItem",
            ["ListBox"] = "ListBox",
            ["ListView"] = "ListView",
            ["TreeView"] = "TreeView",
            ["DataTable"] = "Table",
            ["Column"] = "TemplateColumn",

            // Text input.
            ["TextField"] = "TextBox",
            ["SearchField"] = "TextBox",
            ["MaskedField"] = "TextBox",
            ["ColorField"] = "TextBox",
            ["PasswordField"] = "PasswordBox",
            ["TextArea"] = "RichTextBox",
            ["NumericField"] = "Numeric",
            ["DatePicker"] = "DateTimePicker",
            ["MonthCalendar"] = "Calendar",
            ["Slider"] = "Slider",

            // Display.
            ["Label"] = "Label",
            ["Text"] = "TextBlock",
            ["Badge"] = "Label",
            ["Callout"] = "RichTooltip",
            ["EmptyState"] = "Label",
            ["Icon"] = "ImageIcon",
            ["Image"] = "Image",
            ["ProgressBar"] = "ProgressBar",
            ["Spinner"] = "Spinner",
        };

        /// <summary>The moniker name for <paramref name="symbol"/>.</summary>
        public static string For(SolutionSymbol symbol)
        {
            if (symbol is null)
            {
                throw new ArgumentNullException(nameof(symbol));
            }

            return symbol.Kind == SolutionSymbolKind.ViewElement
                ? ForViewElement(symbol.ElementTag)
                : ForRust(symbol.Kind, symbol.Visibility);
        }

        public static string ForRust(SolutionSymbolKind kind, SymbolVisibility visibility)
        {
            string? family;
            switch (kind)
            {
                case SolutionSymbolKind.Struct: family = "Structure"; break;
                case SolutionSymbolKind.Enum: family = "Enumeration"; break;
                case SolutionSymbolKind.Union: family = "Union"; break;
                case SolutionSymbolKind.Trait: family = "Interface"; break;
                case SolutionSymbolKind.Function:
                case SolutionSymbolKind.Method: family = "Method"; break;
                case SolutionSymbolKind.Field: family = "Field"; break;
                case SolutionSymbolKind.Variant: family = "EnumerationItem"; break;
                case SolutionSymbolKind.Const:
                case SolutionSymbolKind.Static: family = "Constant"; break;
                case SolutionSymbolKind.Module: family = "Module"; break;
                case SolutionSymbolKind.TypeAlias: family = "TypeDefinition"; break;
                case SolutionSymbolKind.Macro: family = "Macro"; break;
                case SolutionSymbolKind.TraitImpl: return "ImplementInterface";
                case SolutionSymbolKind.Impl: return "Type";
                case SolutionSymbolKind.Region: return "Namespace";
                case SolutionSymbolKind.ExternBlock: return "Reference";
                default: family = "Type"; break;
            }

            return family + visibility;
        }

        /// <summary>A control-like glyph per element family (button, check box, text box, panel, list...).</summary>
        public static string ForViewElement(string tag)
        {
            if (tag != null && ElementFamilies.TryGetValue(tag, out var name))
            {
                return name;
            }

            // Unknown / custom elements: guess from the usual suffixes, else a generic control.
            tag ??= string.Empty;
            if (tag.EndsWith("Button", StringComparison.Ordinal)) return "Button";
            if (tag.EndsWith("Field", StringComparison.Ordinal) || tag.EndsWith("Box", StringComparison.Ordinal)) return "TextBox";
            if (tag.EndsWith("List", StringComparison.Ordinal)) return "ListBox";
            if (tag.EndsWith("Panel", StringComparison.Ordinal) || tag.EndsWith("Layout", StringComparison.Ordinal)) return "Panel";
            if (tag.EndsWith("View", StringComparison.Ordinal) || tag.EndsWith("Window", StringComparison.Ordinal)) return "WindowsForm";
            return "UserControl";
        }

        /// <summary>Every moniker name this class can return (checked against the real catalog by the tests).</summary>
        public static IEnumerable<string> AllNames()
        {
            foreach (SolutionSymbolKind kind in Enum.GetValues(typeof(SolutionSymbolKind)))
            {
                if (kind == SolutionSymbolKind.ViewElement)
                {
                    continue;
                }

                foreach (SymbolVisibility visibility in Enum.GetValues(typeof(SymbolVisibility)))
                {
                    yield return ForRust(kind, visibility);
                }
            }

            foreach (var name in ElementFamilies.Values)
            {
                yield return name;
            }

            foreach (var name in new[] { "Button", "TextBox", "ListBox", "Panel", "WindowsForm", "UserControl", Fallback })
            {
                yield return name;
            }
        }
    }
}
