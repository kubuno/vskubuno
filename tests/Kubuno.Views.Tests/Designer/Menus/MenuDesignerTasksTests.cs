using System;
using System.Collections.Generic;
using System.Linq;
using Kubuno.Views.Designer.DesignSurface;
using Kubuno.Views.Designer.Editing;
using Kubuno.Views.Designer.Menus;
using Kubuno.Views.Designer.PropertyBrowser;
using Kubuno.Views.Designer.Registry;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OleInterop = Microsoft.VisualStudio.OLE.Interop;

namespace Kubuno.Views.Tests.Designer.Menus
{
    /// <summary>The menu designer tasks (docs/MENUS.md section 5): "Add" choices, smart tag verbs, standard items, commands.</summary>
    [TestClass]
    public class MenuDesignerTasksTests
    {
        private static string Entry(string name, string family, string[] properties, params string[] children) =>
            "{\"name\":\"" + name + "\",\"family\":\"" + family + "\",\"children\":\"" + (children.Length == 0 ? "None" : "List") +
            "\",\"allowed_children\":[" + string.Join(",", children.Select(c => "\"" + c + "\"")) + "],\"properties\":[" +
            string.Join(",", properties.Select(p => "{\"name\":\"" + p + "\",\"kind\":\"String\",\"default\":\"\",\"doc\":\"\"}")) + "],\"events\":[]}";

        private static readonly string[] None = Array.Empty<string>();

        private static readonly ComponentRegistry Registry = ComponentRegistry.FromJson("[" + string.Join(",", new[]
        {
            Entry("Panel", "core", None, "MenuBar", "ContextMenu", "Command", "DropDownButton", "Label"),
            Entry("Label", "core", new[] { "Text" }),
            Entry("Command", "ribbon", new[] { "Label" }),
            Entry("MenuBar", "menus", None, "MenuItem"),
            Entry("ContextMenu", "menus", None, "MenuItem", "MenuSeparator", "MenuHeader"),
            Entry("DropDownButton", "menus", new[] { "Text", "Icon" }, "MenuItem", "MenuSeparator", "MenuHeader"),
            Entry("MenuItem", "menus", new[] { "Text", "Icon", "ShortcutKeys" }, "MenuItem", "MenuSeparator", "MenuHeader"),
            Entry("MenuSeparator", "menus", None),
            Entry("MenuHeader", "menus", new[] { "Text" }),
        }) + "]");

        private const string View =
            "<Panel>\n" +
            "  <Command x:Name=\"cmd_save\" Label=\"Enregistrer\"/>\n" +
            "  <MenuBar x:Name=\"bar\">\n" +
            "    <MenuItem Text=\"&amp;Fichier\">\n" +
            "      <MenuItem x:Name=\"menu_save\" Text=\"&amp;Enregistrer\" Icon=\"Save\" ShortcutKeys=\"Ctrl+S\" ToolTip=\"Enregistre\" CheckOnClick=\"true\"/>\n" +
            "    </MenuItem>\n" +
            "  </MenuBar>\n" +
            "  <ContextMenu x:Name=\"edit\"/>\n" +
            "  <MenuBar x:Name=\"empty\" Dock=\"Top\"/>\n" +
            "  <Label Text=\"x\"/>\n" +
            "</Panel>";

        [TestMethod]
        public void AddChoices_AreMenusOnABar_AndItemsSeparatorsHeadersElsewhere()
        {
            CollectionAssert.AreEqual(new[] { "MenuItem" }, MenuDesignerTasks.AddChoices(View, "1", Registry).ToList());
            CollectionAssert.AreEqual(new[] { "MenuItem", "MenuSeparator", "MenuHeader" }, MenuDesignerTasks.AddChoices(View, "1.0", Registry).ToList());
            CollectionAssert.AreEqual(new[] { "MenuItem", "MenuSeparator", "MenuHeader" }, MenuDesignerTasks.AddChoices(View, "2", Registry).ToList());
            Assert.AreEqual(0, MenuDesignerTasks.AddChoices(View, "4", Registry).Count, "a label is no menu");
        }

        [TestMethod]
        public void Collections_NameTheirRowLikeWinForms()
        {
            Assert.AreEqual("Items", MenuDesignerTasks.CollectionOf(Registry.Find("MenuBar")!)!.Row);
            CollectionAssert.AreEqual(new[] { "MenuItem" }, MenuDesignerTasks.CollectionOf(Registry.Find("MenuBar")!)!.Tags.ToList());
            Assert.AreEqual("DropDownItems", MenuDesignerTasks.CollectionOf(Registry.Find("MenuItem")!)!.Row);
            Assert.AreEqual("DropDownItems", MenuDesignerTasks.CollectionOf(Registry.Find("DropDownButton")!)!.Row);
            Assert.IsNull(MenuDesignerTasks.CollectionOf(Registry.Find("MenuSeparator")!));
        }

        [TestMethod]
        public void Verbs_OfferStandardItemsOnlyOnAnEmptyBar_AndCommandsOnAnItem()
        {
            Assert.IsFalse(MenuDesignerTasks.Verbs(View, "1", Registry).Any(v => v.Kind == MenuVerbKind.InsertStandardItems), "the bar has menus");
            Assert.IsTrue(MenuDesignerTasks.Verbs(View, "3", Registry).Any(v => v.Kind == MenuVerbKind.InsertStandardItems));
            var item = MenuDesignerTasks.Verbs(View, "1.0.0", Registry);
            CollectionAssert.IsSubsetOf(
                new[] { MenuVerbKind.EditItems, MenuVerbKind.Add, MenuVerbKind.EditText, MenuVerbKind.ChooseIcon, MenuVerbKind.BindCommand, MenuVerbKind.NewCommand },
                item.Select(v => v.Kind).Distinct().ToList());
            Assert.AreEqual("cmd_save", item.Single(v => v.Kind == MenuVerbKind.BindCommand).Argument);
            Assert.IsTrue(item.All(v => v.Text.Length > 0));
            Assert.AreEqual(0, MenuDesignerTasks.Verbs(View, "4", Registry).Count);
        }

        [TestMethod]
        public void StandardItems_FillAnEmptyBar_WithNamedItemsAndResources()
        {
            var keys = new List<string>();
            var edits = MenuDesignerTasks.PlanStandardItems(View, "3", (name, text) => { keys.Add(name); return "{Res " + name + "}"; }, out var resources);
            Assert.AreEqual(1, edits.Count);
            var after = ChildCollectionPlanner.Apply(View, edits);
            StringAssert.Contains(after, "<MenuBar x:Name=\"empty\" Dock=\"Top\">\n    <MenuItem x:Name=\"menu_file\" Text=\"{Res menu_file}\">\n      <MenuItem x:Name=\"menu_file_new\" Text=\"{Res menu_file_new}\" Icon=\"FilePlus\" ShortcutKeys=\"Ctrl+N\"/>");
            StringAssert.Contains(after, "<MenuSeparator/>");
            StringAssert.Contains(after, "    </MenuItem>\n  </MenuBar>");
            Assert.AreEqual(4, Kubuno.Views.Designer.Selection.ViewDocument.Find(Kubuno.Views.Designer.Selection.ViewDocument.Parse(after), "3")!.Children.Count, "Fichier, Ã‰dition, Outils, Aide");
            Assert.AreEqual(keys.Count, resources.Count);
            Assert.IsTrue(resources.Any(r => r.Key == "menu_edit_paste" && r.Value == "C&oller"));
            // Literal texts without resources, and names the view uses get a number.
            var literal = ChildCollectionPlanner.Apply(View, MenuDesignerTasks.PlanStandardItems(View.Replace("x:Name=\"menu_save\"", "x:Name=\"menu_file_new\""), "3", null, out _));
            StringAssert.Contains(literal, "Text=\"&amp;Fichier\"");
            Assert.AreEqual(0, MenuDesignerTasks.PlanStandardItems(View, "1", null, out _).Count, "a bar with menus keeps them");
        }

        [TestMethod]
        public void NewCommand_TakesTheItemsTextIconShortcutAndTooltip()
        {
            var edits = MenuDesignerTasks.PlanNewCommand(View, "1.0.0", out var name);
            Assert.AreEqual("cmd_save2", name, "cmd_save is taken");
            var after = ChildCollectionPlanner.Apply(View, edits);
            StringAssert.Contains(after, "<Command x:Name=\"cmd_save2\" Label=\"&amp;Enregistrer\" SmallIcon=\"Save\" Shortcut=\"Ctrl+S\" ScreenTipText=\"Enregistre\" IsCheckable=\"true\"/>");
            StringAssert.Contains(after, "<MenuItem x:Name=\"menu_save\" Command=\"cmd_save2\"/>");
        }

        [TestMethod]
        public void ShortcutText_ReadsAliasesAndWritesTheCanonicalSpelling()
        {
            Assert.AreEqual("Ctrl+Shift+S", ShortcutText.Normalize("shift+ctrl+s"));
            Assert.AreEqual("Ctrl+Plus", ShortcutText.Normalize("Ctrl++"));
            Assert.AreEqual("Shift+Delete", ShortcutText.Normalize("Maj+Del"));
            Assert.AreEqual("Ctrl+1", ShortcutText.Normalize("Ctrl+D1"));
            Assert.AreEqual("F24", ShortcutText.Normalize("f24"));
            Assert.AreEqual(string.Empty, ShortcutText.Normalize("Ctrl+Shift"));
            Assert.AreEqual(79, ShortcutText.Keys.Count);
            Assert.AreEqual("Ctrl+Shift+S", RichEditors.Normalize(new PropertyMeta { Name = "ShortcutKeys", Editor = "shortcut" }, PropKind.String, "ctrl+maj+s"));
            Assert.ThrowsExactly<ArgumentException>(() => RichEditors.Normalize(new PropertyMeta { Name = "ShortcutKeys", Editor = "shortcut" }, PropKind.String, "Ctrl+Shift"));
            Assert.IsInstanceOfType(RichEditors.EditorFor(new PropertyMeta { Name = "ShortcutKeys", Editor = "shortcut" }), typeof(KbviewShortcutKeysEditor));
        }

        [TestMethod]
        public void SmartTag_ListsTheVerbs_AndRunsThePickedOne()
        {
            var model = DesignerMenuModel.BuildMenuTasks(View, "1.0.0", Registry, null, null);
            Assert.IsTrue(model.Verbs.Count > 0);
            var actions = new Recorder();
            var target = new DesignerContextMenuCommandTarget(model, actions);
            var first = DesignerCommandIds.RibbonAddFirst;
            Assert.AreEqual(model.Verbs[0].Text, QueryText(target, first));
            var bind = model.Verbs.ToList().FindIndex(v => v.Kind == MenuVerbKind.BindCommand);
            var group = DesignerCommandIds.CommandSet;
            Assert.AreEqual(0, target.Exec(ref group, (uint)(first + bind), 0, IntPtr.Zero, IntPtr.Zero));
            CollectionAssert.AreEqual(new[] { "1.0.0 BindCommand(cmd_save)" }, actions.Verbs);
        }

        [TestMethod]
        public void ContextMenu_OfAMenuElement_OffersAddItemsIconAndText_AndNoLayoutOnAnItem()
        {
            var item = DesignerMenuModel.Build(View, "1.0.0", Registry, null);
            CollectionAssert.AreEqual(new[] { "MenuItem", "MenuSeparator", "MenuHeader" }, item.AddChoices.ToList());
            Assert.AreEqual("DropDownItems", item.Collection!.Row);
            Assert.AreEqual("Icon", item.IconAttribute);
            Assert.AreEqual("Text", item.LabelAttribute);
            Assert.IsTrue(item.InRibbon, "an item is laid out by its menu: no Layout submenus");
            Assert.AreEqual(Kubuno.Views.Designer.DesignerText.MenuElementName("MenuSeparator"), item.AddChoiceText(1));
            var bar = DesignerMenuModel.Build(View, "1", Registry, null);
            Assert.IsFalse(bar.InRibbon);
            Assert.IsNull(bar.IconAttribute);
        }

        private static string QueryText(DesignerContextMenuCommandTarget target, int id)
        {
            var size = 12 + (128 * 2);
            var buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(size);
            try
            {
                System.Runtime.InteropServices.Marshal.WriteInt32(buffer, 0, (int)OleInterop.OLECMDTEXTF.OLECMDTEXTF_NAME);
                System.Runtime.InteropServices.Marshal.WriteInt32(buffer, 4, 0);
                System.Runtime.InteropServices.Marshal.WriteInt32(buffer, 8, 128);
                var group = DesignerCommandIds.CommandSet;
                var cmds = new[] { new OleInterop.OLECMD { cmdID = (uint)id } };
                target.QueryStatus(ref group, 1, cmds, buffer);
                var count = System.Runtime.InteropServices.Marshal.ReadInt32(buffer, 4);
                return System.Runtime.InteropServices.Marshal.PtrToStringUni(IntPtr.Add(buffer, 12), Math.Max(0, count - 1));
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>Records the menu verbs run; the other actions are not expected here.</summary>
        private sealed class Recorder : IDesignerMenuActions
        {
            public List<string> Verbs { get; } = new List<string>();

            public void RunMenuVerb(string elementId, MenuVerb verb) => Verbs.Add(elementId + " " + verb);

            public void ViewCode() => throw new AssertFailedException();

            public void CreateHandler(string elementId, string eventName) => throw new AssertFailedException();

            public void Cut(string elementId) => throw new AssertFailedException();

            public void Copy(string elementId) => throw new AssertFailedException();

            public void Paste(string? targetId) => throw new AssertFailedException();

            public void Duplicate(string elementId) => throw new AssertFailedException();

            public void Delete(string elementId) => throw new AssertFailedException();

            public void SelectElement(string elementId) => throw new AssertFailedException();

            public void MoveWithinParent(string elementId, int index) => throw new AssertFailedException();

            public void Wrap(string elementId, string container) => throw new AssertFailedException();

            public void Unwrap(string elementId) => throw new AssertFailedException();

            public void ShowProperties(string? elementId) => throw new AssertFailedException();

            public void EditDesignSize() => throw new AssertFailedException();

            public void ConvertHandlers() => throw new AssertFailedException();

            public void ChooseToolboxItems() => throw new AssertFailedException();

            public void RunLayoutCommand(DesignerLayoutCommand command) => throw new AssertFailedException();

            public void AddChild(string parentId, string tag) => throw new AssertFailedException();

            public void CreateCommandFrom(string elementId) => throw new AssertFailedException();

            public void EditCollection(string elementId, Kubuno.Views.Designer.Ribbon.RibbonCollection collection) => throw new AssertFailedException();

            public void ChooseIcon(string elementId, string attribute) => throw new AssertFailedException();

            public void EditLabel(string elementId, string attribute) => throw new AssertFailedException();

            public void SetRibbonSize(string elementId, string size) => throw new AssertFailedException();
        }
    }
}
