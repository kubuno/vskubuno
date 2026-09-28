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
    /// Rust symbols only: <c>.kbview</c> element nodes use the Kubuno control icons (<see cref="ControlIcons"/>),
    /// shared with the designer's Toolbox (docs/DESIGNER.md section 11).
    /// </summary>
    public static class SymbolMonikerNames
    {
        /// <summary>Used when a name cannot be resolved (never expected; guards against catalog changes).</summary>
        public const string Fallback = "Type";

        /// <summary>The moniker name for <paramref name="symbol"/>.</summary>
        public static string For(SolutionSymbol symbol)
        {
            if (symbol is null)
            {
                throw new ArgumentNullException(nameof(symbol));
            }

            // .kbview elements use the Kubuno control icons (ControlIcons), not the image catalog.
            return symbol.Kind == SolutionSymbolKind.ViewElement ? Fallback : ForRust(symbol.Kind, symbol.Visibility);
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

            yield return Fallback;
        }
    }
}
