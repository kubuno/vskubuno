using System;
using System.Collections.Generic;
using System.Linq;
using Kubuno.VisualStudio.Designer.DesignSurface;
using Kubuno.VisualStudio.Designer.Editing;
using Kubuno.VisualStudio.Designer.Registry;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using OleInterop = Microsoft.VisualStudio.OLE.Interop;

namespace Kubuno.VisualStudio.Designer.Tests.DesignSurface
{
    /// <summary>docs/DESIGNER.md §12: the context-menu wire shapes, the menu model and its command target.</summary>
    [TestClass]
    public class DesignerContextMenuTests
    {
        private const string View = "<Card Title=\"App\">\n  <Stack>\n    <TextField x:Name=\"status\"/>\n    <Button x:Name=\"hello\" OnClick=\"hi\"/>\n  </Stack>\n</Card>";

        private static readonly ComponentRegistry Registry = ComponentRegistry.FromJson(TestFixtures.ReadAllText("registry.sample.json"));

        // ── protocol ────────────────────────────────────────────────────

        [TestMethod]
        public void ContextMenu_MatchesTheRustWireShape()
        {
            Assert.IsTrue(DesignSurfaceContextMenuProtocol.TryParseContextMenu(
                @"{""type"":""contextMenu"",""x"":10.5,""y"":20.0,""screenX"":300,""screenY"":-40,""elementId"":""0.1""}", out var menu));
            Assert.AreEqual(10.5, menu!.X);
            Assert.AreEqual(300, menu.ScreenX);
            Assert.AreEqual(-40, menu.ScreenY);
            Assert.AreEqual("0.1", menu.ElementId);

            Assert.IsTrue(DesignSurfaceContextMenuProtocol.TryParseContextMenu(
                @"{""type"":""contextMenu"",""x"":1.0,""y"":2.0,""screenX"":3,""screenY"":4,""elementId"":null}", out var view));
            Assert.IsNull(view!.ElementId);
        }

        [TestMethod]
        public void ContextMenu_RejectsOtherShapes()
        {
            Assert.IsFalse(DesignSurfaceContextMenuProtocol.TryParseContextMenu(@"{""type"":""contextMenu"",""x"":1.0}", out _));
            Assert.IsFalse(DesignSurfaceContextMenuProtocol.TryParseContextMenu(@"{""type"":""command"",""name"":""copy"",""elementId"":null}", out _));
            Assert.IsFalse(DesignSurfaceContextMenuProtocol.TryParseContextMenu("garbage", out _));
        }

        [TestMethod]
        public void Command_MatchesTheRustWireShape()
        {
            Assert.IsTrue(DesignSurfaceContextMenuProtocol.TryParseCommand(@"{""type"":""command"",""name"":""duplicate"",""elementId"":""0""}", out var duplicate));
            Assert.AreEqual(DesignSurfaceCommand.Duplicate, duplicate!.Command);
            Assert.AreEqual("0", duplicate.ElementId);
            Assert.IsTrue(DesignSurfaceContextMenuProtocol.TryParseCommand(@"{""type"":""command"",""name"":""paste"",""elementId"":null}", out var paste));
            Assert.AreEqual(DesignSurfaceCommand.Paste, paste!.Command);
            Assert.IsFalse(DesignSurfaceContextMenuProtocol.TryParseCommand(@"{""type"":""command"",""name"":""explode"",""elementId"":null}", out _));
        }

        [TestMethod]
        public void DoubleClick_MatchesTheRustWireShape()
        {
            Assert.IsTrue(DesignSurfaceContextMenuProtocol.TryParseDoubleClick(@"{""type"":""doubleClick"",""elementId"":""0.1""}", out var element));
            Assert.AreEqual("0.1", element!.ElementId);
            Assert.IsTrue(DesignSurfaceContextMenuProtocol.TryParseDoubleClick(@"{""type"":""doubleClick"",""elementId"":""""}", out var root));
            Assert.AreEqual(string.Empty, root!.ElementId);
            Assert.IsFalse(DesignSurfaceContextMenuProtocol.TryParseDoubleClick(@"{""type"":""doubleClick"",""elementId"":null}", out _));
            Assert.IsFalse(DesignSurfaceContextMenuProtocol.TryParseDoubleClick(@"{""type"":""command"",""name"":""copy"",""elementId"":null}", out _));
        }

        [TestMethod]
        public void CreateHandlerMenu_ListsTheDefaultEventFirst_ThenTheComponentsOwnEvents()
        {
            var switchModel = DesignerMenuModel.Build("<Stack><Switch/><TextField/></Stack>", "0", Registry, clipboardTag: null);
            CollectionAssert.AreEqual(new[] { "OnCheckedChanged" }, switchModel.Events.ToArray(), "no mouse/key/focus events in the short menu");
            var rootModel = DesignerMenuModel.Build(View, "", Registry, clipboardTag: null);
            Assert.AreEqual("OnLoad", rootModel.Events.FirstOrDefault());
        }

        [TestMethod]
        public void SelectionChanged_KeepsTheRootIdAsARealSelection()
        {
            Assert.IsTrue(DesignSurfaceProtocol.TryParseSelectionChanged(@"{""type"":""selectionChanged"",""id"":"""",""bounds"":null}", out var ids));
            CollectionAssert.AreEqual(new[] { "" }, ids.ToArray());
        }

        // ── model ───────────────────────────────────────────────────────

        [TestMethod]
        public void ElementModel_ListsEventsAncestorsAndAllowedGestures()
        {
            var model = DesignerMenuModel.Build(View, "0.1", Registry, clipboardTag: "Button");

            CollectionAssert.AreEqual(new[] { "OnClick" }, model.Events.ToArray());
            CollectionAssert.AreEqual(new[] { "0", "" }, model.Ancestors.Select(a => a.Id).ToArray());
            Assert.AreEqual("Stack", model.Ancestors[0].Label);
            Assert.IsTrue(model.CanRemove);
            Assert.IsTrue(model.CanDuplicate);
            Assert.IsTrue(model.CanPaste);
            Assert.IsFalse(model.CanUnwrap);
            Assert.IsNull(model.FrontIndex, "already the last child");
            Assert.AreEqual(0, model.BackIndex);
            Assert.IsTrue(model.WrapOptions.Single(o => o.Container == "Stack").Allowed);
        }

        [TestMethod]
        public void RootModel_CannotBeRemovedOrDuplicated()
        {
            var model = DesignerMenuModel.Build(View, "", Registry, clipboardTag: null);

            Assert.IsFalse(model.CanRemove);
            Assert.IsFalse(model.CanDuplicate);
            Assert.IsTrue(model.CanUnwrap, "a root with exactly one child can be unwrapped");
            Assert.AreEqual(0, model.Ancestors.Count);
            Assert.IsFalse(model.CanPaste);
        }

        [TestMethod]
        public void ViewModel_OnlyOffersPaste()
        {
            var model = DesignerMenuModel.Build(View, null, Registry, clipboardTag: "Button");

            Assert.IsTrue(model.IsView);
            Assert.IsFalse(model.CanPaste, "the root Card already holds its one child and has no container to paste next to");
            Assert.IsFalse(model.CanCopy);
            Assert.IsTrue(DesignerMenuModel.Build("<Stack/>", null, Registry, clipboardTag: "Button").CanPaste);
        }

        // ── command target ──────────────────────────────────────────────

        [TestMethod]
        public void Target_ReportsStandardCommandStates()
        {
            var target = new DesignerContextMenuCommandTarget(DesignerMenuModel.Build(View, "0.1", Registry, null), new RecordingActions());

            Assert.AreEqual(Enabled, Std97(target, DesignerContextMenuCommandTarget.Std97Cut));
            Assert.AreEqual(Disabled, Std97(target, DesignerContextMenuCommandTarget.Std97Paste));
            Assert.AreEqual(Disabled, Std97(target, DesignerContextMenuCommandTarget.Std97BringToFront));
            Assert.AreEqual(Enabled, Std97(target, DesignerContextMenuCommandTarget.Std97SendToBack));
            Assert.AreEqual((int)OleInterop.Constants.OLECMDERR_E_NOTSUPPORTED, QueryRaw(target, DesignerContextMenuCommandTarget.StandardCommandSet97, 222u, out _));
        }

        [TestMethod]
        public void Target_DynamicListsEndWithAnUnsupportedId()
        {
            var target = new DesignerContextMenuCommandTarget(DesignerMenuModel.Build(View, "0.1", Registry, null), new RecordingActions());

            QueryRaw(target, DesignerCommandIds.CommandSet, DesignerCommandIds.CreateHandlerFirst, out var first);
            Assert.AreEqual(Enabled, first);
            Assert.AreNotEqual(0, QueryRaw(target, DesignerCommandIds.CommandSet, DesignerCommandIds.CreateHandlerFirst + 1, out _));
            QueryRaw(target, DesignerCommandIds.CommandSet, DesignerCommandIds.SelectFirst + 1, out var root);
            Assert.AreEqual(Enabled, root);
        }

        [TestMethod]
        public void Target_ExecutesThePickedCommand()
        {
            var actions = new RecordingActions();
            var target = new DesignerContextMenuCommandTarget(DesignerMenuModel.Build(View, "0.1", Registry, null), actions);

            Exec(target, DesignerCommandIds.CommandSet, DesignerCommandIds.CreateHandlerFirst);
            Exec(target, DesignerCommandIds.CommandSet, DesignerCommandIds.SelectFirst);
            Exec(target, DesignerCommandIds.CommandSet, DesignerCommandIds.WrapFirst + 2);
            Exec(target, DesignerContextMenuCommandTarget.StandardCommandSet97, (int)DesignerContextMenuCommandTarget.Std97SendToBack);
            Exec(target, DesignerContextMenuCommandTarget.StandardCommandSet97, (int)DesignerContextMenuCommandTarget.Std97Delete);

            CollectionAssert.AreEqual(
                new[] { "CreateHandler 0.1 OnClick", "Select 0", "Wrap 0.1 Card", "Move 0.1 0", "Delete 0.1" },
                actions.Calls);
        }

        [TestMethod]
        public void Target_SetsTheLocalizedTextOfOwnCommands()
        {
            DesignerText.ForceFrench = true;
            try
            {
                var target = new DesignerContextMenuCommandTarget(DesignerMenuModel.Build(View, "0.1", Registry, null), new RecordingActions());
                Assert.AreEqual("Dupliquer", QueryText(target, DesignerCommandIds.Duplicate));
                Assert.AreEqual("Créer un gestionnaire", QueryText(target, DesignerCommandIds.CreateHandlerMenu));
                Assert.AreEqual("OnClick", QueryText(target, DesignerCommandIds.CreateHandlerFirst));
            }
            finally
            {
                DesignerText.ForceFrench = null;
            }
        }

        // ── docs/DESIGNER.md §13: multi-selection and Layout submenus ─────

        private const string PanelView = "<Panel>\n  <Button X=\"10\" Y=\"10\"/>\n  <Button X=\"100\" Y=\"20\"/>\n  <Stack/>\n</Panel>";

        [TestMethod]
        public void MultiModel_AppliesToTheWholeSelection_AndDropsSingleElementGestures()
        {
            var model = DesignerMenuModel.Build(PanelView, "1", Registry, clipboardTag: null, selectedIds: new[] { "1", "0" });

            Assert.IsTrue(model.IsMultiple);
            CollectionAssert.AreEqual(new[] { "1", "0" }, model.SelectedIds.ToArray());
            Assert.IsTrue(model.CanRemove);
            Assert.IsTrue(model.CanCopy);
            Assert.IsTrue(model.CanDuplicate);
            Assert.AreEqual(0, model.Events.Count);
            Assert.IsFalse(model.CanUnwrap);
            Assert.IsTrue(model.Layout.IsEnabled(DesignerLayoutCommand.AlignLefts));

            // A right-click outside the multi-selection is a menu for that element alone.
            Assert.IsFalse(DesignerMenuModel.Build(PanelView, "2", Registry, null, new[] { "1", "0" }).IsMultiple);
        }

        [TestMethod]
        public void Target_EnablesTheLayoutSubmenusAndCommandsFromTheSelection()
        {
            var actions = new RecordingActions();
            var multi = new DesignerContextMenuCommandTarget(DesignerMenuModel.Build(PanelView, "1", Registry, null, new[] { "1", "0" }), actions);

            Assert.AreEqual(Enabled, Std97(multi, 3u), "Align Lefts");
            Assert.AreEqual(Disabled, Std97(multi, 24u), "Make Horizontal Spacing Equal needs three");
            Assert.AreEqual(Enabled, Std97(multi, DesignerContextMenuCommandTarget.Std97BringToFront));
            QueryRaw(multi, DesignerCommandIds.CommandSet, DesignerCommandIds.AlignMenu, out var alignMenu);
            Assert.AreEqual(Enabled, alignMenu);

            Exec(multi, DesignerContextMenuCommandTarget.StandardCommandSet97, 3);
            Exec(multi, DesignerContextMenuCommandTarget.StandardCommandSet97, (int)DesignerContextMenuCommandTarget.Std97BringToFront);
            Exec(multi, DesignerContextMenuCommandTarget.StandardCommandSet97, (int)DesignerContextMenuCommandTarget.Std97Delete);
            CollectionAssert.AreEqual(new[] { "Layout AlignLefts", "Layout BringToFront", "Delete 1" }, actions.Calls);

            // One Anchor child: only centering; a Stack (flow) child: nothing.
            var single = new DesignerContextMenuCommandTarget(DesignerMenuModel.Build(PanelView, "0", Registry, null), new RecordingActions());
            Assert.AreEqual(Disabled, Std97(single, 3u));
            Assert.AreEqual(Enabled, Std97(single, 12u), "Center Horizontally");
            QueryRaw(single, DesignerCommandIds.CommandSet, DesignerCommandIds.AlignMenu, out var singleAlign);
            Assert.AreEqual(Disabled, singleAlign);
            QueryRaw(single, DesignerCommandIds.CommandSet, DesignerCommandIds.CenterMenu, out var center);
            Assert.AreEqual(Enabled, center);
        }

        [TestMethod]
        public void Target_NamesTheLayoutSubmenusInVisualStudiosLanguage()
        {
            DesignerText.ForceFrench = true;
            try
            {
                var target = new DesignerContextMenuCommandTarget(DesignerMenuModel.Build(PanelView, "0", Registry, null), new RecordingActions());
                Assert.AreEqual("Aligner", QueryText(target, DesignerCommandIds.AlignMenu));
                Assert.AreEqual("Espacement horizontal", QueryText(target, DesignerCommandIds.HorizontalSpacingMenu));
                Assert.AreEqual("Centrer dans la vue", QueryText(target, DesignerCommandIds.CenterMenu));
            }
            finally
            {
                DesignerText.ForceFrench = null;
            }
        }

        // ── helpers ─────────────────────────────────────────────────────

        [TestMethod]
        public void ConvertToTypedHandlers_IsOnTheViewMenuOnly_AndRuns()
        {
            DesignerText.ForceFrench = true;
            try
            {
                var actions = new RecordingActions();
                var view = new DesignerContextMenuCommandTarget(DesignerMenuModel.Build(View, null, Registry, null), actions);
                var group = DesignerCommandIds.CommandSet;
                Assert.AreEqual(0, QueryRaw(view, group, DesignerCommandIds.ConvertHandlers, out var flags));
                Assert.AreEqual(Enabled, flags);
                Assert.AreEqual("Convertir en gestionnaires typés", QueryText(view, DesignerCommandIds.ConvertHandlers));
                Exec(view, group, DesignerCommandIds.ConvertHandlers);
                CollectionAssert.AreEqual(new[] { "ConvertHandlers" }, actions.Calls);

                var element = new DesignerContextMenuCommandTarget(DesignerMenuModel.Build(View, "0.1", Registry, null), new RecordingActions());
                QueryRaw(element, group, DesignerCommandIds.ConvertHandlers, out var hidden);
                Assert.AreNotEqual(0u, hidden & (uint)OleInterop.OLECMDF.OLECMDF_INVISIBLE, "not on an element's menu");
            }
            finally
            {
                DesignerText.ForceFrench = null;
            }
        }

        private const uint Enabled =(uint)(OleInterop.OLECMDF.OLECMDF_SUPPORTED | OleInterop.OLECMDF.OLECMDF_ENABLED);
        private const uint Disabled = (uint)OleInterop.OLECMDF.OLECMDF_SUPPORTED;

        private static uint Std97(DesignerContextMenuCommandTarget target, uint id)
        {
            QueryRaw(target, DesignerContextMenuCommandTarget.StandardCommandSet97, (uint)id, out var flags);
            return flags;
        }

        private static int QueryRaw(DesignerContextMenuCommandTarget target, Guid group, int id, out uint flags) => QueryRaw(target, group, (uint)id, out flags);

        private static int QueryRaw(DesignerContextMenuCommandTarget target, Guid group, uint id, out uint flags)
        {
            var cmds = new[] { new OleInterop.OLECMD { cmdID = id } };
            var hr = target.QueryStatus(ref group, 1, cmds, IntPtr.Zero);
            flags = cmds[0].cmdf;
            return hr;
        }

        private static string QueryText(DesignerContextMenuCommandTarget target, int id)
        {
            // OLECMDTEXT with room for 128 characters, asking for the name.
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

        private static void Exec(DesignerContextMenuCommandTarget target, Guid group, int id) =>
            Assert.AreEqual(0, target.Exec(ref group, (uint)id, 0, IntPtr.Zero, IntPtr.Zero));

        private sealed class RecordingActions : IDesignerMenuActions
        {
            public List<string> Calls { get; } = new List<string>();

            public void ViewCode() => Calls.Add("ViewCode");

            public void CreateHandler(string elementId, string eventName) => Calls.Add($"CreateHandler {elementId} {eventName}");

            public void Cut(string elementId) => Calls.Add("Cut " + elementId);

            public void Copy(string elementId) => Calls.Add("Copy " + elementId);

            public void Paste(string? targetId) => Calls.Add("Paste " + targetId);

            public void Duplicate(string elementId) => Calls.Add("Duplicate " + elementId);

            public void Delete(string elementId) => Calls.Add("Delete " + elementId);

            public void SelectElement(string elementId) => Calls.Add("Select " + elementId);

            public void MoveWithinParent(string elementId, int index) => Calls.Add($"Move {elementId} {index}");

            public void Wrap(string elementId, string container) => Calls.Add($"Wrap {elementId} {container}");

            public void Unwrap(string elementId) => Calls.Add("Unwrap " + elementId);

            public void ShowProperties(string? elementId) => Calls.Add("Properties " + elementId);

            public void EditDesignSize() => Calls.Add("DesignSize");

            public void RunLayoutCommand(DesignerLayoutCommand command) => Calls.Add("Layout " + command);

            public void ConvertHandlers() => Calls.Add("ConvertHandlers");

            public void ChooseToolboxItems() => Calls.Add("ChooseToolboxItems");
        }
    }
}
