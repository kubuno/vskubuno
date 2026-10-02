using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Kubuno.Views.Designer.DesignSurface;
using Kubuno.Views.Designer.PropertyBrowser;
using Kubuno.Views.Designer.Registry;
using Kubuno.Views.Designer.Toolbox;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Kubuno.Views.Designer;

namespace Kubuno.Views.Tests.Designer.Registry
{
    /// <summary>docs/EVENTS.md EVT-7b: the project's own controls in the designer (registry, Properties window, Toolbox, surface).</summary>
    [TestClass]
    public class ProjectControlsTests
    {
        // A project control as kubuno-views-ls exports it (its scan of `#[derive(Component)] struct RoundButton`).
        private const string RoundButtonJson = @"{
            ""name"": ""RoundButton"", ""doc"": ""A pill."", ""doc_fr"": null, ""family"": ""project"", ""icon"": ""circle"",
            ""children"": ""None"", ""allowed_children"": [], ""layout_kind"": null,
            ""properties"": [
                { ""name"": ""CornerRadius"", ""kind"": ""F32"", ""default"": ""18.0"", ""doc"": ""The radius of the corners."", ""doc_fr"": null, ""category"": ""Appearance"", ""browsable"": true, ""bindable"": false, ""localizable"": false, ""serialization"": null, ""editor"": null, ""type_converter"": null },
                { ""name"": ""Shape"", ""kind"": { ""Enum"": [""Pill"", ""Square""] }, ""default"": ""Pill"", ""doc"": ""The outline."", ""doc_fr"": null, ""category"": ""Look and feel"", ""browsable"": true, ""bindable"": true, ""localizable"": false, ""serialization"": null, ""editor"": null, ""type_converter"": null },
                { ""name"": ""Secret"", ""kind"": ""String"", ""default"": """", ""doc"": """", ""doc_fr"": null, ""category"": null, ""browsable"": false, ""bindable"": false, ""localizable"": false, ""serialization"": null, ""editor"": null, ""type_converter"": null },
                { ""name"": ""Text"", ""kind"": ""String"", ""default"": """", ""doc"": ""The label."", ""doc_fr"": null }
            ],
            ""events"": [ { ""name"": ""OnLongPress"", ""display_name"": ""LongPress"", ""doc"": ""Held."", ""category"": ""Mouse"", ""args_type"": ""MouseEventArgs"", ""browsable"": true, ""root_only"": false, ""common"": false, ""inherited_from"": null } ],
            ""default_event"": ""OnLongPress"", ""base_chain"": [""RoundButton"", ""Button"", ""ButtonBase"", ""Control"", ""Component""],
            ""origin"": ""project"", ""kind"": ""control"", ""non_visual"": false, ""linked"": false, ""extends"": ""Button"", ""crate_name"": ""round_app"",
            ""toolbox_category"": ""Kubuno"", ""toolbox_icon"": ""circle"", ""browsable"": true, ""default_property"": ""CornerRadius"",
            ""view_path"": null, ""source_file"": ""C:\\p\\src\\round_button.rs"", ""source_line"": 6
        }";

        private static ComponentMeta Round() => ComponentRegistry.FromJson("[" + RoundButtonJson + "]").Components.Single();

        [TestInitialize]
        public void ForceEnglish() => DesignerText.ForceFrench = false;

        [TestCleanup]
        public void ResetLanguage()
        {
            DesignerText.ForceFrench = null;
            NativeToolboxInstaller.IconName = null;
        }

        [TestMethod]
        public void The_builtin_registry_marks_its_classes_and_the_non_visual_components()
        {
            var registry = ComponentRegistry.FromJson(TestFixtures.ReadAllText("registry.sample.json"));

            Assert.IsTrue(registry.Components.All(c => c.Origin == "builtin" && !c.IsProject && c.Browsable));
            var timer = registry.Find("Timer")!;
            Assert.IsTrue(timer.NonVisual);
            Assert.AreEqual("component", timer.Kind);
            Assert.AreEqual("OnTick", timer.DefaultEvent);
            Assert.AreEqual("components", timer.Family);
            var userControl = registry.Find("UserControl")!;
            Assert.IsFalse(userControl.NonVisual);
            CollectionAssert.AreEqual(new[] { "UserControl", "ContainerControl", "ScrollableControl", "Control", "Component" }, userControl.BaseChain);
            Assert.IsTrue(registry.Find("Button")!.IsA("ButtonBase"));
            Assert.AreEqual(0, registry.ProjectComponents.Count());
        }

        [TestMethod]
        public void A_project_control_reads_its_design_time_attributes()
        {
            var round = Round();

            Assert.IsTrue(round.IsProject);
            Assert.AreEqual("control", round.Kind);
            Assert.AreEqual("Button", round.Extends);
            Assert.AreEqual("round_app", round.CrateName);
            Assert.AreEqual("circle", round.ToolboxIcon);
            Assert.AreEqual("CornerRadius", round.DefaultProperty);
            Assert.AreEqual(6, round.SourceLine);
            Assert.AreEqual("Appearance", round.Properties[0].Category);
            Assert.IsFalse(round.Properties[2].Browsable);
            Assert.IsTrue(round.Properties[1].Bindable);
            Assert.IsTrue(round.Properties[3].Browsable, "an older export without the key keeps the default");
            Assert.AreEqual("OnLongPress", round.DefaultEventFor(false)?.Name);
        }

        [TestMethod]
        public void The_properties_window_uses_the_declared_categories_hides_non_browsable_and_selects_the_default_property()
        {
            var round = Round();
            var element = new KbviewElementObject(new Host("<Panel><RoundButton/></Panel>", round), "0", round);
            var properties = element.GetProperties();

            Assert.AreEqual(new CategoryAttribute("Appearance").Category, properties["CornerRadius"]!.Category);
            Assert.AreEqual("Look and feel", properties["Shape"]!.Category, "a category of the project's own is shown as written");
            Assert.AreEqual("The radius of the corners.", properties["CornerRadius"]!.Description);
            Assert.IsNull(properties["Secret"], "#[browsable(false)] is not listed");
            Assert.AreEqual("CornerRadius", element.GetDefaultProperty()?.Name);
            Assert.AreEqual("CornerRadius", ((DefaultPropertyAttribute)element.GetAttributes()[typeof(DefaultPropertyAttribute)]!).Name);
            DesignerText.ForceFrench = true;
            Assert.AreEqual("Apparence", PropertyCategoryMap.DisplayName("appearance"), "a standard category is localized, like WinForms'");
        }

        [TestMethod]
        public void The_toolbox_icon_of_a_project_control_is_its_own_or_its_kind_s()
        {
            NativeToolboxInstaller.IconName = tag => tag is "Circle" or "Button" or "UserControl" or "Component" or "CustomControl" ? tag : "Control";
            var round = Round();
            Assert.AreEqual("Circle", NativeToolboxInstaller.ProjectIconKey(round));
            round.ToolboxIcon = "button";
            Assert.AreEqual("Button", NativeToolboxInstaller.ProjectIconKey(round));
            round.ToolboxIcon = "no-such-icon";
            Assert.AreEqual("CustomControl", NativeToolboxInstaller.ProjectIconKey(round));
            round.Kind = "user_control";
            Assert.AreEqual("UserControl", NativeToolboxInstaller.ProjectIconKey(round));
            round.Kind = "component";
            round.ToolboxIcon = null;
            Assert.AreEqual("Component", NativeToolboxInstaller.ProjectIconKey(round));
            Assert.AreEqual("RoundApp Components", DesignerText.ProjectToolboxTabName("RoundApp"));
        }

        [TestMethod]
        public void The_surface_gets_the_project_controls_verbatim()
        {
            Assert.AreEqual("{\"type\":\"projectComponents\",\"components\":[{\"name\":\"X\"}]}", DesignSurfaceProtocol.EncodeProjectComponents("[{\"name\":\"X\"}]"));
            Assert.AreEqual("{\"type\":\"projectComponents\",\"components\":[]}", DesignSurfaceProtocol.EncodeProjectComponents(" "));
        }

        [TestMethod]
        public void The_toolbox_project_tab_lists_the_controls_the_design_build_linked()
        {
            var path = Path.Combine(Path.GetTempPath(), "kubuno-registry-" + Guid.NewGuid().ToString("N") + ".json");
            var linked = RoundButtonJson.Replace("\"linked\": false", "\"linked\": true");
            var declared = RoundButtonJson.Replace("\"RoundButton\"", "\"OnlyDeclared\"");
            File.WriteAllText(path, "{\"version\":\"1\",\"components\":[{\"name\":\"Button\",\"origin\":\"builtin\"}," + linked + "," + declared + "]}");
            try
            {
                var components = ProjectComponentsFile.ReadLinked(path);
                CollectionAssert.AreEqual(new[] { "RoundButton" }, components.Select(c => c.Name).ToList());
                Assert.AreEqual("round_app::RoundButton", ProjectComponentsFile.ChoiceKey(components[0]));
            }
            finally
            {
                File.Delete(path);
            }

            Assert.AreEqual(0, ProjectComponentsFile.ReadLinked(path + ".missing").Count);
        }

        [TestMethod]
        public void TheApplicationsOwnControlLibrariesAreItsPathAndWorkspaceDependencies()
        {
            var root = Path.Combine(Path.GetTempPath(), "kubuno-appdeps-" + Guid.NewGuid().ToString("N"));
            var app = Path.Combine(root, "apps", "chat");
            Directory.CreateDirectory(app);
            File.WriteAllText(Path.Combine(root, "Cargo.toml"), "[workspace]\nmembers = [\"apps/*\"]\n\n[workspace.dependencies]\nchat-controls = { path = \"libs/chat-controls\" }\nserde = \"1\"\n");
            File.WriteAllText(
                Path.Combine(app, "Cargo.toml"),
                "[package]\nname = \"chat\"\n\n[dependencies]\n# A comment = \"x\"\nkubuno = { path = \"../../kubuno\" }\nfoundations-controls = { path = \"controls\" }\nrenamed = { package = \"ui-kit\", path = \"../ui\" }\nchat-controls.workspace = true\nserde = { workspace = true }\nregex = \"1\"\n\n[dev-dependencies]\ntest-helpers = { path = \"../h\" }\n");
            try
            {
                var crates = ProjectComponentsFile.ApplicationDependencyCrates(Path.Combine(app, "Cargo.toml"));
                CollectionAssert.AreEquivalent(new[] { "foundations_controls", "ui_kit", "chat_controls" }, crates.ToList());
                Assert.AreEqual("foundations_controls", ProjectComponentsFile.Normalize("foundations-controls"));
                Assert.AreEqual(0, ProjectComponentsFile.ApplicationDependencyCrates(Path.Combine(app, "missing.toml")).Count);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private sealed class Host : IKbviewElementHost
        {
            private readonly string _text;

            public Host(string text, ComponentMeta project)
            {
                _text = text;
                Registry = ComponentRegistry.FromJson("[" + RoundButtonJson + "]");
            }

            public ComponentRegistry Registry { get; }

            public int CurrentVersion => 1;

            public string GetCurrentText() => _text;

            public void SetAttribute(string elementId, string name, string value)
            {
            }

            public void RemoveAttribute(string elementId, string name)
            {
            }

            public void CreateOrShowHandler(string elementId, string eventName, string? suggestedName)
            {
            }

            public bool IsHandlerRequestRecent(string elementId, string eventName) => false;

            public IReadOnlyList<string> GetCompatibleHandlers(string elementId, string eventName) => Array.Empty<string>();

            public void RenameHandler(string elementId, string eventName, string oldName, string newName)
            {
            }

            public void RemoveHandler(string elementId, string eventName)
            {
            }
        }
    }
}
