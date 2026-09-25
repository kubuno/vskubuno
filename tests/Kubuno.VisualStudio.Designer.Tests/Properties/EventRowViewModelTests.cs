using System;
using Kubuno.VisualStudio.Designer.Properties;
using Kubuno.VisualStudio.Designer.Registry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Designer.Tests.Properties
{
    [TestClass]
    public class EventRowViewModelTests
    {
        private static EventMeta CreateClickMeta() => new EventMeta { Name = "Click", Doc = "doc" };

        [TestMethod]
        public void AttributeName_PrependsOn()
        {
            var row = new EventRowViewModel(CreateClickMeta(), null, Array.Empty<string>());

            Assert.AreEqual("OnClick", row.AttributeName);
        }

        [TestMethod]
        public void NoHandler_HasHandlerIsFalse()
        {
            var row = new EventRowViewModel(CreateClickMeta(), null, Array.Empty<string>());

            Assert.IsFalse(row.HasHandler);
        }

        [TestMethod]
        public void WithHandler_HasHandlerIsTrue()
        {
            var row = new EventRowViewModel(CreateClickMeta(), "button1_click", new[] { "button1_click" });

            Assert.IsTrue(row.HasHandler);
        }

        [TestMethod]
        public void RequestCreateHandler_RaisesEvent()
        {
            var row = new EventRowViewModel(CreateClickMeta(), null, Array.Empty<string>());
            var raised = false;
            row.CreateHandlerRequested += (_, _) => raised = true;

            row.RequestCreateHandler();

            Assert.IsTrue(raised);
        }

        [TestMethod]
        public void SettingHandlerName_RaisesPropertyChanged_ForHandlerNameAndHasHandler()
        {
            var row = new EventRowViewModel(CreateClickMeta(), null, Array.Empty<string>());
            var raised = new System.Collections.Generic.List<string?>();
            row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            row.HandlerName = "button1_click";

            CollectionAssert.AreEquivalent(
                new[] { nameof(EventRowViewModel.HandlerName), nameof(EventRowViewModel.HasHandler) },
                raised);
        }
    }
}
