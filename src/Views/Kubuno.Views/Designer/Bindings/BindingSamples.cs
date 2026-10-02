using System;

namespace Kubuno.Desktop.Designer.Bindings
{
    /// <summary>A sample source schema (the dialog gallery, the tests): a form with a few fields, a binding source, two converters.</summary>
    public static class BindingSamples
    {
        public static BindingSourceSchema Schema()
        {
            BindingMember M(string name, string path, string kind, BindingShape shape, string type, bool writable = true, string expression = "") => new BindingMember
            {
                Name = name,
                Path = path,
                Kind = kind,
                Shape = shape,
                RustType = type,
                Writable = writable,
                Expression = expression.Length > 0 ? expression : "{Binding " + path + "}",
            };

            var customers = M("customers", "customers", "component", BindingShape.List, "BindingSource", writable: false, expression: "{Binding Source=customers}");
            customers.Children = new[]
            {
                M("Name", "customers.Name", "column", BindingShape.Any, "TEXT", expression: "{Binding Source=customers, Path=Name}"),
                M("Position", "customers.Position", "state", BindingShape.Number, "f32", expression: "{Binding Source=customers, Path=Position}"),
                M("Count", "customers.Count", "state", BindingShape.Number, "f32", writable: false, expression: "{Binding Source=customers, Path=Count}"),
            };
            return new BindingSourceSchema
            {
                Context = new BindingContext
                {
                    Label = "MainForm",
                    Members = new[]
                    {
                        M("Title", "Title", "field", BindingShape.Text, "String"),
                        M("IsBusy", "IsBusy", "field", BindingShape.Bool, "bool"),
                        M("Total", "Total", "field", BindingShape.Number, "f64"),
                        M("Items", "Items", "field", BindingShape.List, "Rows"),
                    },
                },
                Components = new[] { customers },
                Resources = new[] { M("app_title", "app_title", "resource", BindingShape.Text, "strings.kbres", writable: false, expression: "{Res app_title}") },
                Converters = new[]
                {
                    new BindingConverterInfo { Name = "Not", Output = BindingShape.Bool, TwoWay = true },
                    new BindingConverterInfo { Name = "ToUpper", Output = BindingShape.Text, TwoWay = true },
                    new BindingConverterInfo { Name = "Initials", Output = BindingShape.Any, Project = true },
                },
                Issues = Array.Empty<BindingIssue>(),
            };
        }
    }
}
