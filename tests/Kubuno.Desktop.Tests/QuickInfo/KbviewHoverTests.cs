using System.Linq;
using Kubuno.Shared.Logic.QuickInfo;
using Kubuno.Desktop.Logic.QuickInfo;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.QuickInfo
{
    /// <summary>kubuno-views-ls hovers (formats of crates/kubuno-views-ls/src/hover.rs) turned into C#-style QuickInfo.</summary>
    [TestClass]
    public class KbviewHoverTests
    {
        [TestMethod]
        public void AnElementShowsItsControlIconItsNameAsATypeAndTheLocalizedDoc()
        {
            var hover = KbviewHover.Parse("**`<Button>`**\n\nA push button that raises `OnClick` when activated.")!;
            Assert.AreEqual(KbviewHoverKind.Element, hover.Kind);
            Assert.AreEqual("Button", hover.Name);

            var tooltip = hover.ToQuickInfo(new KbviewSymbolDetails { Documentation = "Un bouton qui déclenche `OnClick`.", Container = "Contrôles communs" });
            var (icon, signature, path) = QuickInfoAssertions.Header(tooltip);
            Assert.AreEqual("Button", icon.Tag);
            Assert.AreEqual("Button", signature.ToPlainText());
            Assert.AreEqual(QuickInfoTextKind.Class, signature.Runs.Single().Kind);
            Assert.AreEqual("Contrôles communs", path);
            StringAssert.StartsWith(((QuickInfoContainer)tooltip).Children[1].ToPlainText(), "Un bouton qui déclenche OnClick.");
        }

        [TestMethod]
        public void APropertyShowsNameTypeAndDefaultLikeAFieldDeclaration()
        {
            var hover = KbviewHover.Parse("**`Text`** on `<Button>`\n\nText displayed on the button.\n\n*Default: ``*")!;
            Assert.AreEqual(KbviewHoverKind.Property, hover.Kind);
            Assert.AreEqual("Button", hover.Element);
            Assert.AreEqual(string.Empty, hover.Default);

            var tooltip = hover.ToQuickInfo(new KbviewSymbolDetails { TypeName = "String" });
            var (icon, signature, path) = QuickInfoAssertions.Header(tooltip);
            Assert.AreEqual("PropertyPublic", icon.MonikerName);
            Assert.AreEqual("Text: String = \"\"", signature.ToPlainText());
            Assert.AreEqual(QuickInfoTextKind.Property, QuickInfoAssertions.KindOf(signature.Runs, "Text"));
            Assert.AreEqual(QuickInfoTextKind.Class, QuickInfoAssertions.KindOf(signature.Runs, "String"));
            Assert.AreEqual(QuickInfoTextKind.String, QuickInfoAssertions.KindOf(signature.Runs, "\"\""));
            Assert.AreEqual("Button", path);
            Assert.AreEqual("Text displayed on the button.", ((QuickInfoContainer)tooltip).Children[1].ToPlainText());
        }

        [TestMethod]
        public void NumbersAndBooleansAreNotQuoted()
        {
            var width = KbviewHover.Parse("**`Width`** on `<Button>`\n\nWidth.\n\n*Default: `0`*")!.ToQuickInfo(new KbviewSymbolDetails { TypeName = "f32" });
            Assert.AreEqual("Width: f32 = 0", QuickInfoAssertions.Header(width).Signature.ToPlainText());
            Assert.AreEqual(QuickInfoTextKind.Keyword, QuickInfoAssertions.KindOf(QuickInfoAssertions.Header(width).Signature.Runs, "f32"));

            var enabled = KbviewHover.Parse("**`Enabled`** on `<Button>`\n\nOn.\n\n*Default: `true`*")!.ToQuickInfo(new KbviewSymbolDetails { TypeName = "bool" });
            Assert.AreEqual(QuickInfoTextKind.Keyword, QuickInfoAssertions.KindOf(QuickInfoAssertions.Header(enabled).Signature.Runs, "true"));
        }

        [TestMethod]
        public void AnEnumValueHoverListsTheValidValues()
        {
            var hover = KbviewHover.Parse("**`Variant`** on `<Button>`\n\nVisual style.\n\nValid values: Primary, Secondary, Ghost")!;
            CollectionAssert.AreEqual(new[] { "Primary", "Secondary", "Ghost" }, hover.ValidValues.ToArray());
            var tooltip = (QuickInfoContainer)hover.ToQuickInfo(new KbviewSymbolDetails { ValidValuesLabel = "Valeurs possibles :" });
            var values = (QuickInfoText)tooltip.Children.Last();
            Assert.AreEqual("Valeurs possibles : Primary, Secondary, Ghost", values.ToPlainText());
            Assert.AreEqual(QuickInfoTextKind.EnumMember, QuickInfoAssertions.KindOf(values.Runs, "Ghost"));
        }

        [TestMethod]
        public void AnEventShowsTheEventKeywordItsArgsAndCategory()
        {
            var hover = KbviewHover.Parse("**`OnClick`** event on `<Button>`\n\nRaised when the button is activated.\n\n*Action — `MouseEventArgs`*")!;
            Assert.AreEqual(KbviewHoverKind.Event, hover.Kind);
            Assert.AreEqual("MouseEventArgs", hover.EventArgs);
            Assert.AreEqual("Action", hover.EventCategory);

            var (icon, signature, path) = QuickInfoAssertions.Header(hover.ToQuickInfo(new KbviewSymbolDetails { EventCategory = "Action" }));
            Assert.AreEqual("EventPublic", icon.MonikerName);
            Assert.AreEqual("event OnClick(MouseEventArgs)", signature.ToPlainText());
            Assert.AreEqual(QuickInfoTextKind.Keyword, QuickInfoAssertions.KindOf(signature.Runs, "event"));
            Assert.AreEqual(QuickInfoTextKind.Event, QuickInfoAssertions.KindOf(signature.Runs, "OnClick"));
            Assert.AreEqual("Button · Action", path);
        }

        [TestMethod]
        public void AnOlderEventNameKeepsItsRemark()
        {
            var hover = KbviewHover.Parse("**`OnPress`** event on `<Button>`\n\nRaised when clicked.\n\n*Action — `MouseEventArgs`*\n\n*Older name for `OnClick`.*")!;
            StringAssert.EndsWith(hover.ToQuickInfo().ToPlainText(), "Older name for OnClick.");
        }

        [TestMethod]
        public void CommonAttributesAndXNameAreRecognized()
        {
            var dock = KbviewHover.Parse("**`Dock`** (common attribute)\n\nEdge of the parent.")!;
            Assert.AreEqual(KbviewHoverKind.CommonAttribute, dock.Kind);
            Assert.IsNull(dock.Element);

            var name = KbviewHover.Parse("**`x:Name`**\n\nName of the element.")!;
            Assert.AreEqual(KbviewHoverKind.XName, name.Kind);
            var (icon, signature, _) = QuickInfoAssertions.Header(name.ToQuickInfo());
            Assert.AreEqual("FieldPublic", icon.MonikerName);
            Assert.AreEqual("x:Name: String", signature.ToPlainText());

            Assert.IsNull(KbviewHover.Parse("no title"));
            Assert.IsNull(KbviewHover.Parse(null));
        }

        /// <summary>The icons the tooltips use that the Solution Explorer mapping does not already cover exist in the installed image catalog.</summary>
        [TestMethod]
        public void EveryQuickInfoMonikerExistsInTheInstalledImageCatalog()
        {
            var catalog = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles),
                @"Microsoft Visual Studio\18\Community\Common7\IDE\Microsoft.VisualStudio.ImageCatalog.dll");
            if (!System.IO.File.Exists(catalog))
            {
                Assert.Inconclusive("Visual Studio 2026 Community is not installed at the expected path: " + catalog);
            }

            var knownMonikers = System.Reflection.Assembly.LoadFrom(catalog).GetType("Microsoft.VisualStudio.Imaging.KnownMonikers", throwOnError: true)!;
            foreach (var name in new[] { "LocalVariable", "Parameter", "Type", "IntellisenseKeyword", "Assembly", "EventPublic", "PropertyPublic", "FieldPublic" })
            {
                Assert.IsNotNull(knownMonikers.GetProperty(name), name);
            }
        }
    }
}
