using System.Linq;
using Kubuno.Views.Designer.PropertyBrowser;
using Kubuno.Views.Designer.Registry;
using Kubuno.Views.Designer.Toolbox;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Kubuno.Views.Designer;

namespace Kubuno.Views.Tests.Designer.Toolbox
{
    /// <summary>
    /// docs/PRINTING.md: the printing components in the designer - their Toolbox tab ("Printing" / "Impression"), the
    /// component tray, the Properties window (the Document reference drop-down) and the default event. The fixture is
    /// the real <c>kubuno/registry</c> answer of <c>kubuno-views-ls</c> for the printing sample (its scan of
    /// <c>kubuno-print</c>, reached through the <c>kubuno</c> crate's workspace dependencies).
    /// </summary>
    [TestClass]
    public class PrintingToolboxTests
    {
        private static ComponentRegistry Registry() => ComponentRegistry.FromJson(TestFixtures.ReadAllText("printing.registry.json"));

        [TestCleanup]
        public void ResetLanguage() => DesignerText.ForceFrench = null;

        [TestMethod]
        public void The_printing_components_have_their_own_toolbox_tab()
        {
            DesignerText.ForceFrench = false;
            var layout = NativeToolboxInstaller.Layout(Registry()).ToList();
            CollectionAssert.AreEqual(
                new[] { "PageSetupDialog", "PrintDialog", "PrintDocument", "PrintPreviewDialog" },
                layout.Where(i => i.Tab == "Printing").Select(i => i.Component).ToArray(),
                "alphabetical, like the WinForms Toolbox; the preview dialog's own page buttons are hidden");
            Assert.IsFalse(layout.Any(i => i.Component == "PreviewPagesButton"));
            DesignerText.ForceFrench = true;
            Assert.IsTrue(NativeToolboxInstaller.Layout(Registry()).All(i => i.Tab == "Impression"));
            Assert.AreEqual("Impression", PropertyCategoryMap.DisplayName("Printing"), "the properties' category, localized");
        }

        [TestMethod]
        public void The_components_go_to_the_tray_and_a_double_click_creates_a_print_page_handler()
        {
            var registry = Registry();
            var document = registry.Find("PrintDocument")!;
            Assert.IsTrue(document.NonVisual);
            Assert.AreEqual("kubuno_print", document.CrateName);
            Assert.AreEqual("OnPrintPage", document.DefaultEventFor(false)?.Name);
            CollectionAssert.IsSubsetOf(new[] { "OnBeginPrint", "OnQueryPageSettings", "OnPrintPage", "OnEndPrint" }, document.Events.Select(e => e.Name).ToArray());
            Assert.AreEqual("PrintPageEventArgs", document.FindEvent("OnPrintPage")!.ArgsType);
            Assert.AreEqual("DocumentName", document.DefaultProperty);
            foreach (var dialog in new[] { "PrintPreviewDialog", "PrintDialog", "PageSetupDialog" })
            {
                Assert.IsTrue(registry.Find(dialog)!.NonVisual, dialog);
            }
        }

        [TestMethod]
        public void The_document_property_of_the_dialogs_lists_the_views_documents()
        {
            var registry = Registry();
            var property = registry.Find("PrintPreviewDialog")!.Properties.Single(p => p.Name == "Document");
            var converter = RichEditors.ConverterFor(property) as ReferenceNamesConverter;
            Assert.IsNotNull(converter, "a reference drop-down, like WinForms' Document property");
            Assert.AreEqual("PrintDocument", converter!.Kind);
            const string view = "<Panel><PrintDocument x:Name=\"invoice\"/><PrintDocument x:Name=\"labels\"/><Button x:Name=\"print\"/><PrintPreviewDialog Document=\"invoice\"/></Panel>";
            CollectionAssert.AreEqual(new[] { "invoice", "labels" }, ReferenceNamesConverter.Names(view, registry, "PrintDocument").ToArray());
        }
    }
}
