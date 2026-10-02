namespace Kubuno.Desktop.Designer.Properties
{
    /// <summary>A parsed <c>{Binding Path[, Mode=TwoWay]}</c> value (docs/DESIGNER.md §1).</summary>
    public sealed class BindingExpression
    {
        public BindingExpression(string path, string? mode)
        {
            Path = path;
            Mode = mode;
        }

        public string Path { get; }

        public string? Mode { get; }

        public override string ToString() => BindingExpressionParser.Format(this);
    }
}
