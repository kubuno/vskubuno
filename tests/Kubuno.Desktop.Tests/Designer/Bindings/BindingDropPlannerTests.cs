using System.Linq;
using Kubuno.Desktop.Designer.Bindings;
using Kubuno.Views.Designer.Bindings;
using Kubuno.Views.Designer.Registry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.Designer.Bindings
{
    /// <summary>
    /// The Data Sources window's drop of a bindable member on a view (docs/DESIGNER.md, "Data bindings"): bind an existing
    /// control, or add a label and a bound control. The binding models themselves: tests/Kubuno.Views.Tests.
    /// </summary>
    [TestClass]
    public sealed class BindingDropPlannerTests
    {
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

        private static readonly ComponentRegistry Controls = ComponentRegistry.FromJson(
            "[{\"name\":\"Form\",\"children\":\"List\",\"properties\":[{\"name\":\"Text\",\"kind\":\"String\"}]}," +
            "{\"name\":\"TextField\",\"properties\":[{\"name\":\"Text\",\"kind\":\"String\",\"bindable\":true}]}," +
            "{\"name\":\"CheckBox\",\"properties\":[{\"name\":\"Text\",\"kind\":\"String\"},{\"name\":\"Checked\",\"kind\":\"Bool\"}]}," +
            "{\"name\":\"ListBox\",\"properties\":[{\"name\":\"ItemsSource\",\"kind\":\"String\",\"editor\":\"list\"}]}," +
            "{\"name\":\"Label\",\"properties\":[{\"name\":\"Text\",\"kind\":\"String\"}]}]");
    }
}
