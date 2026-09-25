using System;
using System.Linq;
using Kubuno.VisualStudio.Designer.Properties;
using Kubuno.VisualStudio.Designer.Registry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Designer.Tests.Properties
{
    [TestClass]
    public class EventRowViewModelTests
    {
        // EventMeta.Name is ALREADY the full attribute name in the real DSG-1 export (e.g. "OnClick",
        // never a bare "Click") - see that type's own doc comment.
        private static EventMeta CreateClickMeta() => new EventMeta { Name = "OnClick", Doc = "doc" };

        [TestMethod]
        public void AttributeName_MatchesEventNameAsIs()
        {
            var row = new EventRowViewModel(CreateClickMeta(), null, Array.Empty<string>(), "file:///view.kbview", "0");

            // Not "OnOnClick": Event.Name is already the full attribute name, so AttributeName must be a
            // plain passthrough, not a second "On" + Name prefix.
            Assert.AreEqual("OnClick", row.AttributeName);
        }

        [TestMethod]
        public void AttributeName_MatchesTheRealRegistryFixture()
        {
            // Regression test for the "OnOnClick" bug (DSG-8's own report): build the row from the SAME
            // registry.sample.json DSG-1's real export produces, not a hand-picked EventMeta, so a future
            // change to either side (the export's own naming, or this passthrough) is caught here.
            var registry = ComponentRegistry.FromJson(TestFixtures.ReadAllText("registry.sample.json"));
            var button = registry.Find("Button");
            Assert.IsNotNull(button);
            var onClick = button!.Events.Single(e => e.Name == "OnClick");

            var row = new EventRowViewModel(onClick, null, Array.Empty<string>(), "file:///view.kbview", "0");

            Assert.AreEqual("OnClick", row.AttributeName);
        }

        [TestMethod]
        public void NoHandler_HasHandlerIsFalse()
        {
            var row = new EventRowViewModel(CreateClickMeta(), null, Array.Empty<string>(), "file:///view.kbview", "0");

            Assert.IsFalse(row.HasHandler);
        }

        [TestMethod]
        public void WithHandler_HasHandlerIsTrue()
        {
            var row = new EventRowViewModel(CreateClickMeta(), "button1_click", new[] { "button1_click" }, "file:///view.kbview", "0");

            Assert.IsTrue(row.HasHandler);
        }

        [TestMethod]
        public void Constructor_ExposesDocumentUriAndElementId()
        {
            var row = new EventRowViewModel(CreateClickMeta(), null, Array.Empty<string>(), "file:///settings_view.kbview", "0.1");

            Assert.AreEqual("file:///settings_view.kbview", row.DocumentUri);
            Assert.AreEqual("0.1", row.ElementId);
        }

        [TestMethod]
        public void RequestCreateHandler_RaisesEvent()
        {
            var row = new EventRowViewModel(CreateClickMeta(), null, Array.Empty<string>(), "file:///view.kbview", "0");
            var raised = false;
            row.CreateHandlerRequested += (_, _) => raised = true;

            row.RequestCreateHandler();

            Assert.IsTrue(raised);
        }

        [TestMethod]
        public void SettingHandlerName_RaisesPropertyChanged_ForHandlerNameAndHasHandler()
        {
            var row = new EventRowViewModel(CreateClickMeta(), null, Array.Empty<string>(), "file:///view.kbview", "0");
            var raised = new System.Collections.Generic.List<string?>();
            row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            row.HandlerName = "button1_click";

            CollectionAssert.AreEquivalent(
                new[] { nameof(EventRowViewModel.HandlerName), nameof(EventRowViewModel.HasHandler) },
                raised);
        }
    }
}
