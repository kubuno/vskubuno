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
            host.Compatible.Add("other_handler");
            click.SetValue(bound, "say_hello_clicked");
            click.SetValue(bound, "other_handler");
            click.SetValue(bound, "brand_new_name");
            click.SetValue(bound, string.Empty);
            CollectionAssert.AreEqual(
                new[]
                {
                    "handler 0.1 OnClick (default)",
                    "set 0.1 OnClick=other_handler",
                    "rename 0.1 OnClick other_handler->brand_new_name",
                    "removeHandler 0.1 OnClick",
                },
                host.Calls,
                "the same name shows it, a compatible handler rebinds, a new name renames, clearing removes");
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

        [TestMethod]
        public void DockAndAnchor_UnderAFlowParent_AreGreyedWithAnExplanation()
        {
            var properties = Create("0.1").Element.GetProperties();
            foreach (var name in new[] { "Dock", "Anchor" })
            {
                Assert.IsTrue(properties.Find(name, false).IsReadOnly, name);
                StringAssert.Contains(properties.Find(name, false).Description, "Panel");
            }

            Assert.IsFalse(properties.Find("Width", false).IsReadOnly);
        }

        [TestMethod]
        public void DockAndAnchor_InAPanel_UseTheWinFormsPickers()
        {
            var host = new FakeHost("<Panel>\n  <Button Anchor=\"Top, Right\"/>\n</Panel>");
            var properties = new KbviewElementObject(host, "0", Registry.Find("Button")!).GetProperties();

            Assert.IsFalse(properties.Find("Dock", false).IsReadOnly);
            Assert.IsInstanceOfType(properties.Find("Dock", false).GetEditor(typeof(System.Drawing.Design.UITypeEditor)), typeof(KbviewDockEditor));
            Assert.IsInstanceOfType(properties.Find("Anchor", false).GetEditor(typeof(System.Drawing.Design.UITypeEditor)), typeof(KbviewAnchorEditor));
            Assert.IsNull(properties.Find("Text", false).GetEditor(typeof(System.Drawing.Design.UITypeEditor)));
        }

        [TestMethod]
        public void Anchor_ShowsAndEditsLikeWinForms()
        {
            var host = new FakeHost("<Panel>\n  <Button/>\n  <Button Anchor=\"Left, Top\"/>\n  <Button Anchor=\"Bottom, Right\"/>\n</Panel>");
            PropertyDescriptor Anchor(string id, out KbviewElementObject element)
            {
                element = new KbviewElementObject(host, id, Registry.Find("Button")!);
                return element.GetProperties().Find("Anchor", false);
            }

            // Absent: WinForms' default "Top, Left", not bold; Dock shows "None".
            var anchor = Anchor("0", out var absent);
            Assert.AreEqual("Top, Left", anchor.GetValue(absent));
            Assert.IsFalse(anchor.ShouldSerializeValue(absent));
            Assert.AreEqual("None", absent.GetProperties().Find("Dock", false).GetValue(absent));

            // Written as the default (in any order): still not bold.
            anchor = Anchor("1", out var topLeft);
            Assert.IsFalse(anchor.ShouldSerializeValue(topLeft));

            // Any other value: bold, resettable.
            anchor = Anchor("2", out var bottomRight);
            Assert.AreEqual("Bottom, Right", anchor.GetValue(bottomRight));
            Assert.IsTrue(anchor.ShouldSerializeValue(bottomRight));
            Assert.IsTrue(anchor.CanResetValue(bottomRight));

            // A typed value is normalized like WinForms' enum converter; garbage is refused.
            anchor = Anchor("0", out var typed);
            anchor.SetValue(typed, "right, bottom");
            CollectionAssert.Contains(host.Calls, "set 0 Anchor=Bottom, Right");
            Assert.Throws<ArgumentException>(() => anchor.SetValue(typed, "Top, Middle"));
        }

        [TestMethod]
        public void TheView_ShowsItsDesignTimeSize()
        {
            var properties = new KbviewElementObject(new FakeHost(View), "", Registry.Find("Card")!).GetProperties();
            Assert.AreEqual(new CategoryAttribute("Layout").Category, properties.Find("DesignWidth", false).Category);
            Assert.IsNotNull(properties.Find("DesignHeight", false));
            Assert.IsNull(Create("0.1").Element.GetProperties().Find("DesignWidth", false), "only on the root");
        }

        [TestMethod]
        public void LayoutAttributeText_RoundTripsWinFormsValues()
        {
            Assert.AreEqual(System.Windows.Forms.DockStyle.Fill, LayoutAttributeText.ParseDock("Fill"));
            Assert.AreEqual(System.Windows.Forms.DockStyle.None, LayoutAttributeText.ParseDock("nonsense"));
            Assert.AreEqual("Left", LayoutAttributeText.FormatDock(System.Windows.Forms.DockStyle.Left));

            var anchor = LayoutAttributeText.ParseAnchor("Right,  Top ,Left");
            Assert.AreEqual("Top, Left, Right", LayoutAttributeText.FormatAnchor(anchor));
            Assert.AreEqual("Top, Left", LayoutAttributeText.FormatAnchor(LayoutAttributeText.ParseAnchor(null)), "the default anchor");
            Assert.AreEqual("None", LayoutAttributeText.FormatAnchor(LayoutAttributeText.ParseAnchor("None")));
            Assert.AreEqual("Top, Bottom, Left, Right", LayoutAttributeText.FormatAnchor(LayoutAttributeText.ParseAnchor("Bottom, Right, Left, Top")));
        }

        [TestMethod]
        public void Events_AreGroupedByCategory_WithDisplayNamesAndDescriptions()
        {
            var (element, _) = Create("0.1");
            var events = element.GetEventProperties();

            var down = events.Find("OnMouseDown", false);
            Assert.IsNotNull(down, "the events every control raises are listed");
            Assert.AreEqual("MouseDown", down.DisplayName);
            Assert.AreEqual(new CategoryAttribute("Mouse").Category, down.Category);
            StringAssert.StartsWith(down.Description, "Occurs when a mouse button is pressed");
            Assert.AreEqual("Click", events.Find("OnClick", false).DisplayName);
            Assert.AreEqual(new CategoryAttribute("Action").Category, events.Find("OnClick", false).Category);
            Assert.AreEqual(new CategoryAttribute("Focus").Category, events.Find("OnValidating", false).Category);
            Assert.AreEqual(new CategoryAttribute("Key").Category, events.Find("OnKeyPress", false).Category);
            Assert.IsNull(events.Find("OnLoad", false), "the view's own events belong to its root element");

            DesignerText.ForceFrench = true;
            var french = Create("0.1").Element.GetEventProperties().Find("OnMouseDown", false);
            Assert.AreEqual("Souris", french.Category);
            StringAssert.StartsWith(french.Description, "Se produit quand un bouton de la souris");
        }

        [TestMethod]
        public void DefaultEvent_ComesFromTheRegistry_AndIsLoadForTheRoot()
        {
            var (button, _) = Create("0.1");
            Assert.AreEqual("OnClick", button.GetDefaultEvent()?.Name);
            Assert.AreEqual("OnClick", ((DefaultEventAttribute)button.GetAttributes()[typeof(DefaultEventAttribute)]!).Name);

            var (field, _) = Create("0.0", "TextField");
            Assert.AreEqual("OnTextChanged", field.DefaultEventMeta?.Name);

            var (root, _) = Create(string.Empty, "Card");
            Assert.IsTrue(root.IsRoot);
            Assert.AreEqual("OnLoad", root.GetDefaultEvent()?.Name);
            Assert.IsNotNull(root.GetEventProperties().Find("OnShown", false));
            Assert.AreEqual("OnLoad", root.DefaultEventProperty?.Name);
        }

        [TestMethod]
        public void AnEventWrittenUnderAnOlderAlias_ShowsInItsRow_AndIsEditedInPlace()
        {
            var host = new FakeHost("<Stack>\n  <Switch x:Name=\"dark\" OnToggled=\"dark_toggled\"/>\n</Stack>");
            var element = new KbviewElementObject(host, "0", Registry.Find("Switch")!);
            var row = element.GetEventProperties().Find("OnCheckedChanged", false);

            Assert.AreEqual("CheckedChanged", row.DisplayName);
            Assert.AreEqual("dark_toggled", row.GetValue(element));
            Assert.IsTrue(row.ShouldSerializeValue(element));
            Assert.IsNull(element.GetEventProperties().Find("OnToggled", false), "the alias is not a second row");

            row.SetValue(element, "dark_changed");
            row.ResetValue(element);
            CollectionAssert.AreEqual(new[] { "rename 0 OnCheckedChanged dark_toggled->dark_changed", "removeHandler 0 OnCheckedChanged" }, host.Calls);
            Assert.IsTrue(KbviewEventBindingService.Instance.ShowCode(element, element.GetEvents()["OnCheckedChanged"]!));
        }

        [TestMethod]
        public void EventRow_Dropdown_ListsTheCompatibleHandlers_NotExclusive()
        {
            var (element, host) = Create("0.1");
            host.Compatible.AddRange(new[] { "say_hello_clicked", "any_click" });
            var row = element.GetEventProperties().Find("OnClick", false);
            var context = new ElementContext(element, row);

            Assert.IsTrue(row.Converter.GetStandardValuesSupported(context));
            Assert.IsFalse(row.Converter.GetStandardValuesExclusive(context), "a new name can still be typed");
            CollectionAssert.AreEqual(new[] { "say_hello_clicked", "any_click" }, row.Converter.GetStandardValues(context).Cast<string>().ToArray());
            Assert.IsFalse(row.Converter.GetStandardValuesSupported(null), "no element, no list");

            var methods = KbviewEventBindingService.Instance.GetCompatibleMethods(element.GetEvents()["OnClick"]!);
            CollectionAssert.AreEqual(new[] { "say_hello_clicked", "any_click" }, methods.Cast<string>().ToArray());
        }

        /// <summary>The grid's <see cref="ITypeDescriptorContext"/> for one row of one element.</summary>
        private sealed class ElementContext : ITypeDescriptorContext
        {
            public ElementContext(object instance, PropertyDescriptor property)
            {
                Instance = instance;
                PropertyDescriptor = property;
            }

            public IContainer? Container => null;

            public object Instance { get; }

            public PropertyDescriptor PropertyDescriptor { get; }

            public object? GetService(Type serviceType) => null;

            public void OnComponentChanged()
            {
            }

            public bool OnComponentChanging() => true;
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

            public List<string> Compatible { get; } = new List<string>();

            public IReadOnlyList<string> GetCompatibleHandlers(string elementId, string eventName) => Compatible;

            public void RenameHandler(string elementId, string eventName, string oldName, string newName) =>
                Calls.Add($"rename {elementId} {eventName} {oldName}->{newName}");

            public void RemoveHandler(string elementId, string eventName) => Calls.Add($"removeHandler {elementId} {eventName}");
        }
    }
}
