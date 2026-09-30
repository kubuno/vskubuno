using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kubuno.Desktop.Designer.Editing;
using Kubuno.Desktop.Designer.Properties;
using Kubuno.Desktop.Designer.Registry;
using Kubuno.Desktop.Designer.Selection;
using Kubuno.Desktop.Tests.Designer.Selection.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.Designer.Selection
{
    [TestClass]
    public class SelectionSyncServiceTests
    {
        private const string DocumentUri = "file:///view.kbview";

        private static ComponentRegistry ButtonRegistry() => ComponentRegistry.FromJson(
            "[{\"name\":\"Button\",\"doc\":null,\"family\":\"core\",\"icon\":null,\"children\":\"None\",\"allowed_children\":[],\"layout_kind\":null," +
            "\"properties\":[{\"name\":\"Text\",\"kind\":\"String\",\"default\":\"\",\"doc\":null}]," +
            "\"events\":[{\"name\":\"OnClick\",\"doc\":null}]}]");

        private static LspRange Range(int startLine, int startChar, int endLine, int endChar) =>
            new LspRange(new LspPosition(startLine, startChar), new LspPosition(endLine, endChar));

        private sealed class Harness
        {
            public FakeDesignSurfaceHost SurfaceHost { get; } = new FakeDesignSurfaceHost();
            public FakeDesignSurfaceSelectionTarget SurfaceTarget { get; } = new FakeDesignSurfaceSelectionTarget();
            public FakeTextViewSelectionAdapter TextView { get; } = new FakeTextViewSelectionAdapter();
            public FakeViewsSelectionLanguageServerClient Client { get; } = new FakeViewsSelectionLanguageServerClient();
            public FakeOutlineSelectionTarget Outline { get; } = new FakeOutlineSelectionTarget();
            public PropertiesPanelViewModel Properties { get; } = new PropertiesPanelViewModel();
            public ComponentRegistry Registry { get; set; } = ButtonRegistry();

            public SelectionSyncService BuildService() => new SelectionSyncService(
                SurfaceHost, SurfaceTarget, TextView, Client, Registry, Properties, DocumentUri, Outline);
        }

        [TestMethod]
        public void Constructor_RejectsNullCollaborators()
        {
            var h = new Harness();
            Assert.ThrowsExactly<System.ArgumentNullException>(() =>
                new SelectionSyncService(null!, h.SurfaceTarget, h.TextView, h.Client, h.Registry, h.Properties, DocumentUri));
            Assert.ThrowsExactly<System.ArgumentNullException>(() =>
                new SelectionSyncService(h.SurfaceHost, null!, h.TextView, h.Client, h.Registry, h.Properties, DocumentUri));
            Assert.ThrowsExactly<System.ArgumentNullException>(() =>
                new SelectionSyncService(h.SurfaceHost, h.SurfaceTarget, null!, h.Client, h.Registry, h.Properties, DocumentUri));
            Assert.ThrowsExactly<System.ArgumentNullException>(() =>
                new SelectionSyncService(h.SurfaceHost, h.SurfaceTarget, h.TextView, null!, h.Registry, h.Properties, DocumentUri));
            Assert.ThrowsExactly<System.ArgumentNullException>(() =>
                new SelectionSyncService(h.SurfaceHost, h.SurfaceTarget, h.TextView, h.Client, null!, h.Properties, DocumentUri));
            Assert.ThrowsExactly<System.ArgumentNullException>(() =>
                new SelectionSyncService(h.SurfaceHost, h.SurfaceTarget, h.TextView, h.Client, h.Registry, null!, DocumentUri));
            Assert.ThrowsExactly<System.ArgumentNullException>(() =>
                new SelectionSyncService(h.SurfaceHost, h.SurfaceTarget, h.TextView, h.Client, h.Registry, h.Properties, null!));
        }

        [TestMethod]
        public async Task SurfaceSelection_MovesTheXmlViewAndTheOutline_NeverEchoesBackToTheSurface()
        {
            var h = new Harness();
            h.TextView.CurrentText = "<Button Text=\"Ok\"/>";
            h.Client.RangesByElementId[""] = Range(0, 0, 0, 20);
            _ = h.BuildService();

            h.SurfaceHost.RaiseSelectionChanged("");
            await WaitUntilAsync(() => h.TextView.Selections.Count > 0);

            Assert.AreEqual(1, h.TextView.Selections.Count);
            Assert.AreEqual(Range(0, 0, 0, 20), h.TextView.Selections[0]);
            Assert.AreEqual(0, h.SurfaceTarget.Selections.Count, "must never echo the selection back to its own origin");
            Assert.AreEqual("", h.Outline.LastSelection);
            Assert.IsTrue(h.Properties.HasSelection);
            Assert.AreEqual("Button", h.Properties.Component!.Name);
            Assert.AreEqual("Ok", h.Properties.Properties[0].EffectiveValue);
        }

        [TestMethod]
        public async Task CaretMoved_PushesToTheSurfaceAndTheOutline_NeverReSelectsTheTextView()
        {
            var h = new Harness();
            h.TextView.CurrentText = "<Button Text=\"Ok\"/>";
            h.TextView.CaretPosition = new LspPosition(0, 3);
            h.Client.ElementAtOffsetResponse = new ElementAtOffsetResponse("", Range(0, 0, 0, 20));
            _ = h.BuildService();

            h.TextView.RaiseCaretMoved();
            await WaitUntilAsync(() => h.SurfaceTarget.Selections.Count > 0);

            Assert.AreEqual("", h.SurfaceTarget.LastSelection);
            Assert.AreEqual(0, h.TextView.Selections.Count, "must never re-select the range in the view that already has the caret there");
            Assert.AreEqual("", h.Outline.LastSelection);
            Assert.AreEqual(0, h.Client.RangeOfElementCallCount, "elementAtOffset's own range must be reused, not re-fetched");
        }

        [TestMethod]
        public async Task OutlineActivation_MovesTheXmlViewAndTheSurface_NeverEchoesBackToTheOutline()
        {
            var h = new Harness();
            h.TextView.CurrentText = "<Button Text=\"Ok\"/>";
            h.Client.RangesByElementId[""] = Range(0, 0, 0, 20);
            var service = h.BuildService();

            await service.SelectFromOutlineAsync("");

            Assert.AreEqual(1, h.TextView.Selections.Count);
            Assert.AreEqual("", h.SurfaceTarget.LastSelection);
            Assert.AreEqual(0, h.Outline.Selections.Count, "must never echo the selection back into the outline that originated it");
        }

        [TestMethod]
        public async Task RepeatedSelectionOfTheSameElement_IsANoOp()
        {
            var h = new Harness();
            h.TextView.CurrentText = "<Button Text=\"Ok\"/>";
            h.Client.RangesByElementId[""] = Range(0, 0, 0, 20);
            _ = h.BuildService();

            h.SurfaceHost.RaiseSelectionChanged("");
            await WaitUntilAsync(() => h.TextView.Selections.Count > 0);
            h.SurfaceHost.RaiseSelectionChanged("");
            await Task.Delay(30); // give a wrongly-duplicated call a chance to land

            Assert.AreEqual(1, h.TextView.Selections.Count, "selecting the same element again must not re-apply anything");
            Assert.AreEqual(1, h.Client.RangeOfElementCallCount);
        }

        [TestMethod]
        public async Task UnresolvedElementId_DegradesToNoSelectionWithoutTouchingTheOtherViews()
        {
            var h = new Harness();
            // No entry in RangesByElementId - the server's own "stale id" no-op.
            _ = h.BuildService();

            h.SurfaceHost.RaiseSelectionChanged("7");
            await WaitUntilAsync(() => h.Client.RangeOfElementCallCount > 0);
            await Task.Delay(30);

            Assert.AreEqual(0, h.TextView.Selections.Count);
            Assert.AreEqual(0, h.SurfaceTarget.Selections.Count);
            Assert.AreEqual(0, h.Outline.Selections.Count);
            Assert.IsFalse(h.Properties.HasSelection);
        }

        [TestMethod]
        public async Task ClearingTheSelectionFromTheSurface_ClearsPropertiesAndTheOutlineButNeverEchoesToTheSurface()
        {
            var h = new Harness();
            h.TextView.CurrentText = "<Button Text=\"Ok\"/>";
            h.Client.RangesByElementId[""] = Range(0, 0, 0, 20);
            _ = h.BuildService();
            h.SurfaceHost.RaiseSelectionChanged("");
            await WaitUntilAsync(() => h.TextView.Selections.Count > 0);

            h.SurfaceHost.RaiseSelectionChanged(null);
            await WaitUntilAsync(() => h.Outline.Selections.Count > 1);

            Assert.IsFalse(h.Properties.HasSelection);
            Assert.IsNull(h.Outline.LastSelection);
            Assert.AreEqual(0, h.SurfaceTarget.Selections.Count, "the surface originated the clear - it must never be told to clear itself");
        }

        [TestMethod]
        public async Task ClearingTheSelectionFromTheTextView_PushesTheClearToTheSurfaceAndTheOutline()
        {
            var h = new Harness();
            h.TextView.CurrentText = "<Button Text=\"Ok\"/>";
            h.Client.ElementAtOffsetResponse = new ElementAtOffsetResponse("", Range(0, 0, 0, 20));
            _ = h.BuildService();
            h.TextView.RaiseCaretMoved();
            await WaitUntilAsync(() => h.SurfaceTarget.Selections.Count > 0);

            h.Client.ElementAtOffsetResponse = null; // the caret moved off any element
            h.TextView.RaiseCaretMoved();
            await WaitUntilAsync(() => h.SurfaceTarget.Selections.Count > 1);

            Assert.IsFalse(h.Properties.HasSelection);
            Assert.IsNull(h.Outline.LastSelection);
            Assert.IsNull(h.SurfaceTarget.LastSelection);
        }

        [TestMethod]
        public async Task UnregisteredTagName_ClearsThePropertiesPanelButStillSyncsTheOtherViews()
        {
            var h = new Harness();
            h.TextView.CurrentText = "<MysteryWidget/>";
            h.Client.RangesByElementId[""] = Range(0, 0, 0, 16);
            _ = h.BuildService();

            h.SurfaceHost.RaiseSelectionChanged("");
            await WaitUntilAsync(() => h.TextView.Selections.Count > 0);

            Assert.IsFalse(h.Properties.HasSelection);
            Assert.AreEqual("", h.Outline.LastSelection);
        }

        // ── docs/DESIGNER.md §13: multi-selection ─────────────────────────────

        private const string TwoButtons = "<Stack><Button Text=\"A\"/><Button Text=\"B\"/></Stack>";

        private static ComponentRegistry StackRegistry() => ComponentRegistry.FromJson(TestFixtures.ReadAllText("registry.sample.json"));

        [TestMethod]
        public async Task SurfaceMultiSelection_ExposesEveryElement_AndSelectsThePrimaryInTheXml()
        {
            var h = new Harness { Registry = StackRegistry() };
            h.TextView.CurrentText = TwoButtons;
            h.Client.RangesByElementId["1"] = Range(0, 26, 0, 46);
            var service = h.BuildService();
            var applied = new List<string?>();
            service.SelectionApplied += (_, id) => applied.Add(id);

            h.SurfaceHost.RaiseMultiSelectionChanged("1", "0");
            await WaitUntilAsync(() => h.TextView.Selections.Count > 0);

            Assert.AreEqual("1", service.CurrentElementId);
            CollectionAssert.AreEqual(new[] { "1", "0" }, service.CurrentElementIds.ToArray());
            Assert.AreEqual(Range(0, 26, 0, 46), h.TextView.Selections[0], "the XML view shows the primary element");
            Assert.AreEqual("1", h.Outline.LastSelection, "the Outline highlights the primary");
            CollectionAssert.AreEqual(new string?[] { "1" }, applied);

            // Same primary, one more element (a Shift+click that kept the primary): only the set changes.
            h.SurfaceHost.RaiseMultiSelectionChanged("1", "0", "2");
            await WaitUntilAsync(() => applied.Count > 1);
            CollectionAssert.AreEqual(new[] { "1", "0", "2" }, service.CurrentElementIds.ToArray());
            Assert.AreEqual(1, h.TextView.Selections.Count, "the XML selection is not redone");
            Assert.AreEqual(0, h.SurfaceTarget.Selections.Count, "never echoed back to the surface");
        }

        [TestMethod]
        public async Task CaretInsideThePrimary_KeepsTheMultiSelection()
        {
            var h = new Harness { Registry = StackRegistry() };
            h.TextView.CurrentText = TwoButtons;
            h.Client.RangesByElementId["1"] = Range(0, 26, 0, 46);
            h.Client.ElementAtOffsetResponse = new ElementAtOffsetResponse("1", Range(0, 26, 0, 46));
            var service = h.BuildService();
            h.SurfaceHost.RaiseMultiSelectionChanged("1", "0");
            await WaitUntilAsync(() => h.TextView.Selections.Count > 0);

            h.TextView.RaiseCaretMoved();
            await WaitUntilAsync(() => h.Client.ElementAtOffsetCallCount > 0);
            await Task.Delay(30);

            CollectionAssert.AreEqual(new[] { "1", "0" }, service.CurrentElementIds.ToArray());
            Assert.AreEqual(0, h.SurfaceTarget.Selections.Count);
        }

        [TestMethod]
        public async Task SelectingThePrimaryFromElsewhere_CollapsesTheMultiSelection()
        {
            var h = new Harness { Registry = StackRegistry() };
            h.TextView.CurrentText = TwoButtons;
            h.Client.RangesByElementId["1"] = Range(0, 26, 0, 46);
            var service = h.BuildService();
            h.SurfaceHost.RaiseMultiSelectionChanged("1", "0");
            await WaitUntilAsync(() => h.TextView.Selections.Count > 0);

            await service.SelectElementAsync("1");

            CollectionAssert.AreEqual(new[] { "1" }, service.CurrentElementIds.ToArray());
            Assert.AreEqual("1", h.SurfaceTarget.LastSelection, "the surface is told to select the primary alone");
        }

        private static async Task WaitUntilAsync(System.Func<bool> condition)
        {
            for (var i = 0; i < 100 && !condition(); i++)
            {
                await Task.Delay(10);
            }
        }
    }
}
