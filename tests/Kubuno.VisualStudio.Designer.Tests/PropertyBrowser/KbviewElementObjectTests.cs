using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Linq;
using Kubuno.VisualStudio.Designer.PropertyBrowser;
using Kubuno.VisualStudio.Designer.Registry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Designer.Tests.PropertyBrowser
{
    [TestClass]
    public class KbviewElementObjectTests
    {
        private const string View = "<Card Title=\"App\">\n  <Stack Gap=\"16\">\n    <TextField x:Name=\"status\" Text=\"{Binding Status}\" />\n    <Button x:Name=\"hello\" Text=\"Say hello\" Variant=\"Primary\" OnClick=\"say_hello_clicked\"/>\n    <Button/>\n  </Stack>\n</Card>";

        private static readonly ComponentRegistry Registry = ComponentRegistry.FromJson(TestFixtures.ReadAllText("registry.sample.json"));

        [TestInitialize]
        public void ForceEnglish() => DesignerText.ForceFrench = false;

        [TestCleanup]
        public void ResetLanguage() => DesignerText.ForceFrench = null;

        private static (KbviewElementObject Element, FakeHost Host) Create(string elementId, string tag = "Button")
        {
            var host = new FakeHost(View);
            return (new KbviewElementObject(host, elementId, Registry.Find(tag)!), host);
        }

        [TestMethod]
        public void Properties_ListName_RegistryProperties_AndLayoutAttributes_InCategories()
        {
            var (element, _) = Create("0.1");
            var properties = element.GetProperties();

            Assert.AreEqual("x:Name", properties[0].Name);
            Assert.AreEqual("Name", properties[0].DisplayName);
            Assert.IsTrue(properties[0].Attributes.Contains(new ParenthesizePropertyNameAttribute(true)));
            Assert.AreEqual(new CategoryAttribute("Design").Category, properties[0].Category);

            var text = properties.Find("Text", false);
            // CategoryAttribute itself localizes the standard WinForms category names to the UI culture.
            Assert.AreEqual(new CategoryAttribute("Appearance").Category, text.Category);
            StringAssert.StartsWith(text.Description, "Text displayed on the button");

            DesignerText.ForceFrench = true;
            Assert.AreEqual("Texte affiché sur le bouton.", Create("0.1").Element.GetProperties().Find("Text", false).Description);

            foreach (var layout in new[] { "Dock", "Anchor", "X", "Y", "Width", "Height" })
            {
                Assert.AreEqual(new CategoryAttribute("Layout").Category, properties.Find(layout, false).Category, layout);
            }

            Assert.AreEqual("Button", element.GetClassName());
            Assert.AreEqual("hello", element.GetComponentName());
            Assert.AreEqual("hello (Button)", element.ToString());
        }

        [TestMethod]
        public void Values_AreReadFromTheBuffer_WithDefaultsForAbsentAttributes()
        {
            var (element, _) = Create("0.1");
            var properties = element.GetProperties();

            Assert.AreEqual("Say hello", properties.Find("Text", false).GetValue(element));
            Assert.AreEqual("Md", properties.Find("Size", false).GetValue(element));
            Assert.IsTrue(properties.Find("Text", false).ShouldSerializeValue(element), "a written non-default value is bold");
            Assert.IsFalse(properties.Find("Size", false).ShouldSerializeValue(element), "an absent attribute shows the default, not bold");
            Assert.IsFalse(properties.Find("Variant", false).ShouldSerializeValue(element), "a written value equal to the default is not bold");
            Assert.IsTrue(properties.Find("Variant", false).CanResetValue(element), "but it can still be reset (removed)");
        }

        [TestMethod]
        public void BindingValues_AreShownAsIs()
        {
            var (element, _) = Create("0.0", "TextField");
            Assert.AreEqual("{Binding Status}", element.GetProperties().Find("Text", false).GetValue(element));
        }

        [TestMethod]
        public void SetValue_WritesANormalizedAttribute_AndShowsItOptimistically()
        {
            var (element, host) = Create("0.1");
            var variant = element.GetProperties().Find("Variant", false);

            variant.SetValue(element, "ghost");

            CollectionAssert.AreEqual(new[] { "set 0.1 Variant=Ghost" }, host.Calls);
            Assert.AreEqual("Ghost", variant.GetValue(element));

            host.Text = View.Replace("Variant=\"Primary\"", "Variant=\"Ghost\"");
            Assert.AreEqual("Ghost", variant.GetValue(element), "the buffer caught up");
        }

        [TestMethod]
        public void SetValue_SameValue_IsANoOp_AndEmpty_RemovesTheAttribute()
        {
            var (element, host) = Create("0.1");
            var text = element.GetProperties().Find("Text", false);

            text.SetValue(element, "Say hello");
            text.SetValue(element, string.Empty);

            CollectionAssert.AreEqual(new[] { "remove 0.1 Text" }, host.Calls);
        }

        [TestMethod]
        public void SetValue_InvalidNumber_Throws_AndWritesNothing()
        {
            var (element, host) = Create("0.1");
            Assert.ThrowsExactly<ArgumentException>(() => element.GetProperties().Find("Width", false).SetValue(element, "wide"));
            Assert.AreEqual(0, host.Calls.Count);
        }

        [TestMethod]
        public void Reset_RemovesTheAttribute()
        {
            var (element, host) = Create("0.1");
            element.GetProperties().Find("Text", false).ResetValue(element);
            CollectionAssert.AreEqual(new[] { "remove 0.1 Text" }, host.Calls);
        }

        [TestMethod]
        public void EnumAndBool_OfferTheirVariants_NonExclusively()
        {
            var (element, _) = Create("0.1");
            var variant = element.GetProperties().Find("Variant", false).Converter;
            var loading = element.GetProperties().Find("Loading", false).Converter;

            Assert.IsTrue(variant.GetStandardValuesSupported(null));
            Assert.IsFalse(variant.GetStandardValuesExclusive(null), "a {Binding} must stay typeable");
            CollectionAssert.Contains(variant.GetStandardValues(null), "Danger");
            CollectionAssert.AreEqual(new[] { "true", "false" }, loading.GetStandardValues(null));
        }

        [TestMethod]
        public void Events_AreComponentEvents_BoundThroughTheEventBindingService()
        {
            var (element, _) = Create("0.1");
            var click = element.GetEvents()["OnClick"];
            Assert.IsNotNull(click);
            Assert.IsNotNull(click.EventType);

            var binding = (IEventBindingService)element.Site!.GetService(typeof(IEventBindingService))!;
            var property = binding.GetEventProperty(click);
            Assert.AreSame(click, binding.GetEvent(property));
            Assert.AreEqual("say_hello_clicked", property.GetValue(element));
            Assert.AreEqual("on_hello_click", binding.CreateUniqueMethodName(element, click));
            Assert.AreEqual("hello", element.Site.Name);
            Assert.IsInstanceOfType<IComponent>(element, "PropertyGrid only wires events for components");
        }

        [TestMethod]
        public void EventSetValue_OnAnUnboundEvent_CreatesTheHandler_WithTheServerDefaultName()
        {
            var (element, host) = Create("0.2");
            var binding = KbviewEventBindingService.Instance;
            var property = binding.GetEventProperty(element.GetEvents()["OnClick"]!);

            property.SetValue(element, binding.CreateUniqueMethodName(element, element.GetEvents()["OnClick"]!));

            CollectionAssert.AreEqual(new[] { "handler 0.2 OnClick (default)" }, host.Calls);
        }

        [TestMethod]
        public void EventSetValue_TypedName_OrSameName_OrRename_OrClear()
        {
            var (unbound, unboundHost) = Create("0.2");
            var click = KbviewEventBindingService.Instance.GetEventProperty(unbound.GetEvents()["OnClick"]!);
            click.SetValue(unbound, "custom_name");
            CollectionAssert.AreEqual(new[] { "handler 0.2 OnClick custom_name" }, unboundHost.Calls);

            var (bound, host) = Create("0.1");
            click.SetValue(bound, "say_hello_clicked");
            click.SetValue(bound, "other_handler");
            click.SetValue(bound, string.Empty);
            CollectionAssert.AreEqual(new[] { "handler 0.1 OnClick (default)", "set 0.1 OnClick=other_handler", "remove 0.1 OnClick" }, host.Calls);
        }

        [TestMethod]
        public void ShowCode_DoesNotRequestTwice_WhileARequestIsRecent()
        {
            var (element, host) = Create("0.1");
            var click = element.GetEvents()["OnClick"]!;

            Assert.IsTrue(KbviewEventBindingService.Instance.ShowCode(element, click));
            host.Recent = true;
            Assert.IsTrue(KbviewEventBindingService.Instance.ShowCode(element, click));

            CollectionAssert.AreEqual(new[] { "handler 0.1 OnClick (default)" }, host.Calls);
        }

        [TestMethod]
        public void StaleId_IsReportedAsInvalid()
        {
            var (element, host) = Create("0.1");
            Assert.IsTrue(element.IsValid);
            host.Text = "<Card/>";
            Assert.IsFalse(element.IsValid);
        }

        private sealed class FakeHost : IKbviewElementHost
        {
            private string _text;

            public FakeHost(string text) => _text = text;

            public List<string> Calls { get; } = new List<string>();

            public bool Recent { get; set; }

            public string Text
            {
                get => _text;
                set
                {
                    _text = value;
                    CurrentVersion++;
                }
            }

            public ComponentRegistry Registry => KbviewElementObjectTests.Registry;

            public int CurrentVersion { get; private set; }

            public string GetCurrentText() => _text;

            public void SetAttribute(string elementId, string name, string value) => Calls.Add($"set {elementId} {name}={value}");

            public void RemoveAttribute(string elementId, string name) => Calls.Add($"remove {elementId} {name}");

            public void CreateOrShowHandler(string elementId, string eventName, string? suggestedName) =>
                Calls.Add($"handler {elementId} {eventName} {suggestedName ?? "(default)"}");

            public bool IsHandlerRequestRecent(string elementId, string eventName) => Recent;
        }
    }
}
