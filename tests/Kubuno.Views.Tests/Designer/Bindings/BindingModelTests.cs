using System;
using System.Linq;
using Kubuno.Desktop.Designer;
using Kubuno.Desktop.Designer.Bindings;
using Kubuno.Desktop.Designer.PropertyBrowser;
using Kubuno.Desktop.Designer.Registry;
using Kubuno.Desktop.Tests.Designer.PropertyBrowser;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Kubuno.Desktop.Tests.Designer.Bindings
{
    /// <summary>The data binding models of the designer (docs/DESIGNER.md, "Data bindings"): the lossless markup, the picker, the dialog's edits and their undo unit, the drops.</summary>
    [TestClass]
    public sealed class BindingModelTests
    {
        [TestMethod]
        public void Markup_ReadsEveryPart_AndWritesBackUntouchedText()
        {
            const string raw = "{Binding  Source=customers ,Path=Name, Mode=TwoWay, FormatString='#,##0.00', Odd=1}";
            Assert.IsTrue(BindingMarkup.TryParse(raw, out var m));
            Assert.AreEqual("customers", m!.Source);
            Assert.AreEqual("Name", m.Path);
            Assert.AreEqual("customers.Name", m.FullPath);
            Assert.AreEqual("#,##0.00", m.Get("StringFormat"), "an alias is read under its canonical name");
            CollectionAssert.AreEqual(new[] { "Odd" }, m.UnknownKeys.ToArray());
            Assert.AreEqual(raw, m.ToString(), "nothing changed: the text as written");

            m.Set("Mode", "OneTime");
            Assert.AreEqual("{Binding Source=customers, Path=Name, Mode=OneTime, FormatString='#,##0.00', Odd=1}", m.ToString(), "one option changed: the others kept, in their order and alias");
            m.Set("StringFormat", null).Set("Converter", "ToUpper").Set("ConverterParameter", "a, b");
            Assert.AreEqual("{Binding Source=customers, Path=Name, Mode=OneTime, Odd=1, Converter=ToUpper, ConverterParameter='a, b'}", m.ToString());
            Assert.AreEqual("a, b", m.Get("ConverterParameter"));

            Assert.IsTrue(BindingMarkup.TryParse("{Binding Title, Mode=TwoWay}", out var simple));
            Assert.AreEqual("{Binding Source=orders, Path=Total, Mode=TwoWay}", simple!.Retarget("orders", "Total").ToString());
            Assert.AreEqual("{Binding Count, Mode=TwoWay}", simple.Retarget(null, "Count").ToString(), "back to a view-model path: the bare first part");

            Assert.IsFalse(BindingMarkup.TryParse("{Res title}", out _));
            Assert.IsTrue(BindingMarkup.IsMarkupExtension("{Res title}"));
            Assert.AreEqual("title", BindingMarkup.ResourceKey("{Res title, Source=strings}"));
            Assert.IsFalse(BindingMarkup.IsMarkupExtension("{Binding"));
            Assert.IsFalse(BindingMarkup.TryParse("{BindingX Y}", out _));
            Assert.AreEqual("'it''s'", BindingMarkup.Quote("it's"));
            Assert.AreEqual("it's", BindingMarkup.Unquote("'it''s'"));
        }

        [TestMethod]
        public void Parser_RecognizesTheWholeGrammar()
        {
            Assert.IsTrue(Kubuno.Desktop.Designer.Properties.BindingExpressionParser.TryParse("{Binding Source=customers, Path=Name, FormatString=N2}", out var e));
            Assert.AreEqual("customers.Name", e!.Path);
            Assert.IsTrue(Kubuno.Desktop.Designer.Properties.BindingExpressionParser.TryParse("{Binding Path=Title}", out var p));
            Assert.AreEqual("Title", p!.Path, "Path= is the path, not « Path=Title »");
        }

        [TestMethod]
        public void Picker_PutsFittingMembersFirst_AndGreysTheOthers()
        {
            var schema = BindingSamples.Schema();
            var groups = BindingPickerModel.Build(schema, BindingShape.Bool, "{Binding IsBusy}");
            CollectionAssert.AreEqual(new[] { "context", "sources" }, groups.Select(g => g.Id).ToArray(), "no resources for a boolean property");
            var context = groups[0].Nodes;
            Assert.AreEqual("IsBusy", context[0].Label);
            Assert.IsTrue(context[0].IsCurrent);
            Assert.AreEqual("Total", context[1].Label, "a number converts to a boolean");
            Assert.AreEqual("Title", context[2].Label, "text converts to a boolean only when it parses: after the fitting ones");
            Assert.IsNull(context[2].Fits);
            Assert.IsFalse(context.Single(n => n.Label == "Items").Compatible, "a list never fits a boolean");
            Assert.AreEqual("Items", context.Last().Label);

            var text = BindingPickerModel.Build(schema, BindingShape.Text, null);
            Assert.AreEqual("resources", text.Last().Id);
            var customers = text.Single(g => g.Id == "sources").Nodes.Single();
            Assert.IsTrue(customers.Compatible, "a list source is offered for its members");
            Assert.AreEqual(0, BindingPickerModel.Build(schema, BindingShape.Text, null, "zzz").Count, "the search filters");
            Assert.AreEqual("Title", BindingPickerModel.Build(schema, BindingShape.Text, null, "tit")[0].Nodes.Single().Label);
        }

        [TestMethod]
        public void Picker_RetargetsTheBinding_KeepingItsOptions()
        {
            var schema = BindingSamples.Schema();
            var title = schema.Context.Members.Single(m => m.Path == "Title");
            var name = schema.Components[0].Children.Single(m => m.Name == "Name");
            Assert.AreEqual("{Binding Title}", BindingPickerModel.Apply("Hello", title), "a literal becomes the member's binding");
            Assert.AreEqual("{Binding Title, Mode=TwoWay, StringFormat=N2}", BindingPickerModel.Apply("{Binding Total, Mode=TwoWay, StringFormat=N2}", title));
            Assert.AreEqual("{Binding Source=customers, Path=Name, Mode=TwoWay}", BindingPickerModel.Apply("{Binding Total, Mode=TwoWay}", name));
            Assert.AreEqual("{Res app_title}", BindingPickerModel.Apply("{Binding Total}", schema.Resources[0]));
            Assert.AreEqual(BindingShape.Number, BindingShapes.Of(new PropertyMeta { Name = "Value", Kind = PropKind.F32 }));
            Assert.AreEqual(BindingShape.List, BindingShapes.Of(null, "ItemsSource"));
            Assert.AreEqual(false, BindingShapes.Fits(BindingShape.List, BindingShape.Text));
            Assert.AreEqual(true, BindingShapes.Fits(BindingShape.Any, BindingShape.Bool));
        }

        [TestMethod]
        public void Dialog_WritesOnlyWhatChanged_AndTheDesignValue()
        {
            const string raw = "{Binding Title,  Mode=TwoWay, Odd=keep}";
            var model = new BindingEditModel("Text", raw, null);
            Assert.IsTrue(model.WasBound);
            Assert.AreEqual(0, model.Edits().Count, "OK without a change writes nothing");
            Assert.AreEqual(raw, model.Build(), "the expression as written");

            model.Converter = "ToUpper";
            model.FallbackValue = "(none)";
            model.DesignValue = "Stockage";
            var edits = model.Edits();
            CollectionAssert.AreEqual(
                new[] { "Text={Binding Title, Mode=TwoWay, Odd=keep, Converter=ToUpper, FallbackValue=(none)}", "d:Text=Stockage" },
                edits.Select(e => e.ToString()).ToArray());

            model.Mode = "OneWay";
            StringAssert.DoesNotMatch(model.Build(), new System.Text.RegularExpressions.Regex("Mode="), "the default mode is not written");
            model.Path = string.Empty;
            CollectionAssert.AreEqual(new[] { "-Text", "d:Text=Stockage" }, model.Edits().Select(e => e.ToString()).ToArray(), "no path: the binding goes");

            var fresh = new BindingEditModel("Checked", "true", null) { Path = "IsBusy", Mode = "TwoWay", Trigger = "LostFocus" };
            Assert.IsFalse(fresh.WasBound);
            Assert.AreEqual("{Binding IsBusy, Mode=TwoWay, UpdateSourceTrigger=LostFocus}", fresh.Build());
            var explicitDefault = new BindingEditModel("Text", "{Binding Title, Mode=OneWay}", null) { Mode = "OneWay" };
            Assert.AreEqual("{Binding Title, Mode=OneWay}", explicitDefault.Build(), "a default written by hand stays");

            CollectionAssert.AreEqual(new[] { "Text=Stockage", "-d:Text" }, BindingEditModel.RemoveBinding("Text", "Stockage").Select(e => e.ToString()).ToArray(), "removing restores the value shown");
            CollectionAssert.AreEqual(new[] { "-Text" }, BindingEditModel.RemoveBinding("Text", null).Select(e => e.ToString()).ToArray());
        }

        [TestMethod]
        public void DialogEdits_LandAsOneUndoUnit()
        {
            var host = new RichHost("<Panel>\n  <!-- keep -->\n  <Button Text=\"{Binding Title}\" X=\"8\" Y=\"8\"/>\n</Panel>");
            var label = new KbviewElementObject(host, "0", host.Registry.Find("Button")!);
            var model = new BindingEditModel("Text", label.GetRawValue("Text"), label.GetRawValue("d:Text")) { StringFormat = "N2", DesignValue = "42" };
            BindingActions.Apply(label, model.Edits());
            Assert.AreEqual(0, host.Batches.Count, "queued for the dispatcher turn");
            host.RunScheduled();
            Assert.AreEqual(1, host.Batches.Count, "the binding and its design-time value: one batch, one undo unit");
            CollectionAssert.AreEqual(new[] { "set 0 Text={Binding Title, StringFormat=N2}", "set 0 d:Text=42" }, host.Batches[0].Select(e => e.ToString()).ToArray());
            Assert.AreEqual("{Binding Title, StringFormat=N2}", label.GetRawValue("Text"), "shown at once (pending edit)");

            BindingActions.RemoveBinding(label, "Text");
            host.RunScheduled();
            CollectionAssert.AreEqual(new[] { "set 0 Text=42", "remove 0 d:Text" }, host.Batches[1].Select(e => e.ToString()).ToArray());
        }

        [TestMethod]
        public void PropertiesRows_GetThePickerAndTheMarker()
        {
            var host = new RichHost("<Panel><Button Text=\"{Binding Statut}\" d:Text=\"Prêt\" X=\"8\" Y=\"8\"/></Panel>");
            host.Schema = new BindingSourceSchema
            {
                Context = BindingSamples.Schema().Context,
                Issues = new[] { new BindingIssue { ElementId = "0", Attribute = "Text", Code = "binding-unknown-path", Message = "`Statut` is not a member" } },
            };
            var label = new KbviewElementObject(host, "0", host.Registry.Find("Button")!);
            var text = (KbviewAttributePropertyDescriptor)label.GetProperties().Find("Text", false)!;
            Assert.IsTrue(text.IsBindable);
            var editor = (KbviewBindableEditor)text.GetEditor(typeof(System.Drawing.Design.UITypeEditor))!;
            Assert.AreEqual(System.Drawing.Design.UITypeEditorEditStyle.DropDown, editor.GetEditStyle(null), "a drop-down arrow in the value cell");
            var context = new TestContext(label, text);
            Assert.IsTrue(editor.GetPaintValueSupported(context), "a bound value paints its marker");
            var shown = text.Converter.ConvertToString(context, text.GetValue(label));
            StringAssert.StartsWith(shown, "{Binding Statut}");
            StringAssert.Contains(shown, "Prêt", "the design-time value");
            StringAssert.Contains(shown, "⚠", "the problem");
            Assert.AreEqual("{Binding Statut}", text.Converter.ConvertFromString(context, shown), "what is typed back is read up to the brace");

            Assert.IsFalse(((KbviewAttributePropertyDescriptor)label.GetProperties().Find("x:Name", false)!).IsBindable, "the name is never bound");
            Assert.ThrowsExactly<ArgumentException>(() => text.SetValue(label, "{Binding Title"));
            text.SetValue(label, "{Res app_title}");
            CollectionAssert.Contains(host.Calls, "set 0 Text={Res app_title}");
        }

        [TestMethod]
        public void DataBindingsRow_ListsBoundCustomProperties_AndCountsBindings()
        {
            var button = new KbviewElementObject(new RichHost("<Panel><Button Text=\"{Binding Title}\" Tag=\"{Binding Hero}\"/></Panel>"), "0", RichRegistry.Registry.Find("Button")!);
            var bindings = button.GetProperties().Find(KbviewBindingsPropertyDescriptor.RowName, false)!;
            var value = bindings.GetValue(button)!;
            Assert.AreEqual(DesignerText.IsFrench ? "2 liaisons" : "2 bindings", bindings.Converter.ConvertToString(null, value));
            var parts = bindings.Converter.GetProperties(value)!.Cast<System.ComponentModel.PropertyDescriptor>().ToList();
            Assert.IsInstanceOfType<KbviewBindableEditor>(parts.Single(p => p.Name == "Text").GetEditor(typeof(System.Drawing.Design.UITypeEditor)));
        }

        [TestMethod]
        public void Drops_BindAControl_OrAddABoundOne()
        {
            const string view = "<Form>\n  <TextField x:Name=\"name\" X=\"10\" Y=\"10\" Width=\"200\" Height=\"28\"/>\n  <CheckBox x:Name=\"busy\" X=\"10\" Y=\"60\" Width=\"200\" Height=\"24\"/>\n</Form>";
            var schema = BindingSamples.Schema();
            var title = schema.Context.Members.Single(m => m.Path == "Title");
            var plan = BindingDropPlanner.Plan(view, Controls, title, string.Empty, 50, 20, out var error)!;
            Assert.IsNotNull(plan, error);
            Assert.AreEqual(0, plan.Insertions.Count);
            Assert.AreEqual("0:Text={Binding Title, Mode=TwoWay}", plan.Edits.Select(e => e.ElementId + ":" + e.Attribute + "=" + e.Value).Single());

            var busy = schema.Context.Members.Single(m => m.Path == "IsBusy");
            Assert.AreEqual("Checked", BindingDropPlanner.Plan(view, Controls, busy, string.Empty, 20, 70, out _)!.Edits.Single().Attribute);

            var added = BindingDropPlanner.Plan(view, Controls, title, string.Empty, 10, 100, out _)!;
            Assert.AreEqual(0, added.Edits.Count);
            Assert.AreEqual(2, added.Insertions.Count, "a label and a bound control");
            StringAssert.Contains(added.Insertions[1].Xml, "<TextField x:Name=\"title_text_field\" Text=\"{Binding Title, Mode=TwoWay}\"");
            Assert.AreEqual("3", added.SelectElementId);
            var items = schema.Context.Members.Single(m => m.Path == "Items");
            StringAssert.Contains(BindingDropPlanner.Plan(view, Controls, items, string.Empty, 10, 200, out _)!.Insertions.Last().Xml, "<ListBox x:Name=\"items_list_box\" ItemsSource=\"{Binding Items}\"");
            Assert.AreEqual("Status text", BindingDropPlanner.Humanize("StatusText"));
        }

        [TestMethod]
        public void Schema_IsReadFromTheServerAnswer()
        {
            var json = JToken.Parse(@"{
                ""schema"": {
                    ""context"": { ""label"": ""StorageSection"", ""open"": false, ""members"": [
                        { ""name"": ""Blocks"", ""path"": ""Blocks"", ""expression"": ""{Binding Blocks}"", ""kind"": ""property"", ""rustType"": ""Rows"", ""shape"": ""List"", ""writable"": true,
                          ""location"": { ""uri"": ""file:///c%3A/a/admin_storage.rs"", ""range"": { ""start"": { ""line"": 276, ""character"": 8 }, ""end"": { ""line"": 276, ""character"": 14 } } } } ] },
                    ""item"": { ""label"": ""Repeater · Blocks"", ""open"": false, ""members"": [ { ""name"": ""Title"", ""path"": ""Title"", ""expression"": ""{Binding Title}"", ""kind"": ""rowField"", ""shape"": ""Text"", ""writable"": true } ] },
                    ""components"": [], ""resources"": [],
                    ""converters"": [ { ""name"": ""Not"", ""output"": ""Bool"", ""twoWay"": true, ""doc"": ""d"", ""project"": false } ]
                },
                ""issues"": [ { ""elementId"": ""2.0"", ""attribute"": ""Hero"", ""severity"": ""warning"", ""code"": ""binding-unknown-path"", ""message"": ""m"" } ]
            }");
            var schema = BindingSourceSchema.Parse(json);
            Assert.AreEqual("StorageSection", schema.Context.Label);
            Assert.AreEqual(BindingShape.List, schema.Context.Members[0].Shape);
            Assert.AreEqual(276, schema.Context.Members[0].Location!.Line);
            Assert.AreEqual("Title", schema.Item!.Members[0].Path);
            Assert.AreEqual(BindingShape.Bool, schema.Converters[0].Output);
            Assert.AreEqual("m", schema.IssuesOf("Hero").Single().Message);
            Assert.AreSame(BindingSourceSchema.Empty, BindingSourceSchema.Parse(null));
            Assert.AreEqual("Title", schema.Find("Title")!.Name, "the row first");
        }

        /// <summary>A few controls in the export's shape (what a drop binds).</summary>
        private static readonly ComponentRegistry Controls = ComponentRegistry.FromJson(
            "[{\"name\":\"Form\",\"children\":\"List\",\"properties\":[{\"name\":\"Text\",\"kind\":\"String\"}]}," +
            "{\"name\":\"TextField\",\"properties\":[{\"name\":\"Text\",\"kind\":\"String\",\"bindable\":true}]}," +
            "{\"name\":\"CheckBox\",\"properties\":[{\"name\":\"Text\",\"kind\":\"String\"},{\"name\":\"Checked\",\"kind\":\"Bool\"}]}," +
            "{\"name\":\"ListBox\",\"properties\":[{\"name\":\"ItemsSource\",\"kind\":\"String\",\"editor\":\"list\"}]}," +
            "{\"name\":\"Label\",\"properties\":[{\"name\":\"Text\",\"kind\":\"String\"}]}]");

        /// <summary>A grid context over one element and one row.</summary>
        private sealed class TestContext : System.ComponentModel.ITypeDescriptorContext
        {
            public TestContext(object instance, System.ComponentModel.PropertyDescriptor row)
            {
                Instance = instance;
                PropertyDescriptor = row;
            }

            public System.ComponentModel.IContainer? Container => null;

            public object Instance { get; }

            public System.ComponentModel.PropertyDescriptor PropertyDescriptor { get; }

            public object? GetService(Type serviceType) => null;

            public void OnComponentChanged()
            {
            }

            public bool OnComponentChanging() => true;
        }
    }
}
