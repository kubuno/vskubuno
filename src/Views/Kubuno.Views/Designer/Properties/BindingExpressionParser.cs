using Kubuno.Views.Designer.Bindings;

namespace Kubuno.Views.Designer.Properties
{
    /// <summary>
    /// Recognizes the <c>{Binding …}</c> values of the Properties grid (docs/DESIGNER.md §1, "Data bindings"): any value
    /// the runtime reads as a binding - the whole desktop grammar (docs/VIEWS-SPEC.md §6.1: <c>Path</c>, <c>Source</c>,
    /// <c>Mode</c>, <c>Converter</c>, formats…), read without loss by <see cref="BindingMarkup"/>. This is a display/edit
    /// helper, not the authority: the language server (<c>kubuno_desktop_views::binding</c>) decides what the runtime accepts and
    /// reports the rest as diagnostics.
    /// </summary>
    public static class BindingExpressionParser
    {
        public static bool TryParse(string? rawValue, out BindingExpression? expression)
        {
            expression = null;
            if (!BindingMarkup.TryParse(rawValue, out var markup) || markup!.FullPath is not { Length: > 0 } path)
            {
                return false;
            }

            expression = new BindingExpression(path, markup.Mode);
            return true;
        }

        public static bool IsBindingExpression(string? rawValue) => TryParse(rawValue, out _);

        public static string Format(BindingExpression expression) =>
            expression.Mode is null
                ? $"{{Binding {expression.Path}}}"
                : $"{{Binding {expression.Path}, Mode={expression.Mode}}}";
    }
}
