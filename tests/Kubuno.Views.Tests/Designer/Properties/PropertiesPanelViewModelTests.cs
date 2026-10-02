using System.Collections.Generic;
using System.Linq;
using Kubuno.Desktop.Designer.Properties;
using Kubuno.Desktop.Designer.Registry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.Designer.Properties
{
    [TestClass]
    public class PropertiesPanelViewModelTests
    {
        private static ComponentMeta CreateButtonMeta() => new ComponentMeta
        {
            Name = "Button",
            Family = "core",
            Children = ChildrenModel.None,
            Properties = new List<PropertyMeta>
            {
                new PropertyMeta { Name = "Text", Kind = PropKind.String, Default = "" },
                new PropertyMeta { Name = "Enabled", Kind = PropKind.Bool, Default = "true" },
            },
            Events = new List<EventMeta>
            {
                new EventMeta { Name = "Click" },
            },
        };

        [TestMethod]
        public void SetSelection_BuildsOneRowPerPropertyAndEvent()
        {
            var viewModel = new PropertiesPanelViewModel();

            viewModel.SetSelection(
                CreateButtonMeta(),
                new Dictionary<string, string?> { ["Text"] = "Save" },
                new Dictionary<string, string?>(),
                new[] { "button1_click" },
                "file:///view.kbview",
                "0");

            Assert.IsTrue(viewModel.HasSelection);
            Assert.AreEqual(2, viewModel.Properties.Count);
            Assert.AreEqual("Save", viewModel.Properties[0].RawValue);
            Assert.IsNull(viewModel.Properties[1].RawValue);

            Assert.AreEqual(1, viewModel.Events.Count);
            Assert.AreEqual("Click", viewModel.Events[0].Name);
            CollectionAssert.AreEqual(new[] { "button1_click" }, viewModel.Events[0].AvailableHandlerNames.ToArray());
            Assert.AreEqual("file:///view.kbview", viewModel.Events[0].DocumentUri);
            Assert.AreEqual("0", viewModel.Events[0].ElementId);
        }

        [TestMethod]
        public void SetSelection_PopulatesEventHandlerName_WhenPresent()
        {
            var viewModel = new PropertiesPanelViewModel();

            viewModel.SetSelection(
                CreateButtonMeta(),
                new Dictionary<string, string?>(),
                new Dictionary<string, string?> { ["Click"] = "button1_click" },
                new[] { "button1_click" },
                "file:///view.kbview",
                "0");

            Assert.AreEqual("button1_click", viewModel.Events[0].HandlerName);
        }

        [TestMethod]
        public void ClearSelection_EmptiesRows()
        {
            var viewModel = new PropertiesPanelViewModel();
            viewModel.SetSelection(CreateButtonMeta(), new Dictionary<string, string?>(), new Dictionary<string, string?>(), new string[0], "file:///view.kbview", "0");

            viewModel.ClearSelection();

            Assert.IsFalse(viewModel.HasSelection);
            Assert.AreEqual(0, viewModel.Properties.Count);
            Assert.AreEqual(0, viewModel.Events.Count);
        }

        [TestMethod]
        public void EventRow_CreateHandlerRequested_BubblesThroughPanelViewModel()
        {
            var viewModel = new PropertiesPanelViewModel();
            viewModel.SetSelection(CreateButtonMeta(), new Dictionary<string, string?>(), new Dictionary<string, string?>(), new string[0], "file:///view.kbview", "0");
            CreateHandlerRequestedEventArgs? captured = null;
            viewModel.CreateHandlerRequested += (_, e) => captured = e;

            viewModel.Events[0].RequestCreateHandler();

            Assert.IsNotNull(captured);
            Assert.AreSame(viewModel.Events[0], captured!.Row);
        }
    }
}
