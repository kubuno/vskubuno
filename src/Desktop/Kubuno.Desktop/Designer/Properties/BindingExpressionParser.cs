using System.Text.RegularExpressions;

namespace Kubuno.Desktop.Designer.Properties
{
    /// <summary>
    /// Detects and formats the <c>{Binding Path[, Mode=TwoWay]}</c> shape docs/DESIGNER.md §1 describes
    /// ("any property's value can instead be typed as a {Binding Path[, Mode=TwoWay]} expression ...
    /// mirroring kubuno_views::binding's grammar"). This is deliberately a minimal, display/edit-only
    /// mirror of that shape for the Properties grid - NOT a reimplementation of
    /// <c>kubuno_views::binding</c>'s real grammar or validator, which stays in Rust
    /// (docs/ARCHITECTURE.md: "everything that knows Rust/Kubuno is in Rust; C# only integrates"). A
    /// value this class fails to recognize as a binding is simply shown as a literal string; a value it
    /// does recognize is still only ever round-tripped as raw text through <c>kubuno/applyEdit</c>
    /// (Editing/), where the language server, not this class, is the actual authority on whether it
    /// parses.
    /// </summary>
    public static class BindingExpressionParser
    {
        private static readonly Regex Pattern = new Regex(
            @"^\{Binding\s+(?<path>[^,{}]+?)\s*(,\s*Mode\s*=\s*(?<mode>[A-Za-z]+)\s*)?\}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static bool TryParse(string? rawValue, out BindingExpression? expression)
        {
            expression = null;
            if (string.IsNullOrEmpty(rawValue))
            {
                return false;
            }

            var match = Pattern.Match(rawValue!.Trim());
            if (!match.Success)
            {
                return false;
            }

            var path = match.Groups["path"].Value.Trim();
            if (path.Length == 0)
            {
                return false;
            }

            var mode = match.Groups["mode"].Success ? match.Groups["mode"].Value : null;
            expression = new BindingExpression(path, mode);
            return true;
        }

        public static bool IsBindingExpression(string? rawValue) => TryParse(rawValue, out _);

        public static string Format(BindingExpression expression) =>
            expression.Mode is null
                ? $"{{Binding {expression.Path}}}"
                : $"{{Binding {expression.Path}, Mode={expression.Mode}}}";
    }
}
