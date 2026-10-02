using System.Linq;
using Kubuno.Desktop.Designer;
using Kubuno.Desktop.Designer.DesignSurface;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.Designer.DesignSurface
{
    /// <summary>
    /// docs/DESIGNER.md section 16: the <c>renderStatus</c> line (checked against the exact string
    /// <c>kubuno-views/src/protocol.rs</c>'s <c>render_status_carries_the_state_and_utf16_positions</c> asserts)
    /// and the error banner built from it.
    /// </summary>
    [TestClass]
    public class DesignSurfaceRenderStatusTests
    {
        private const string TolerantLine =
            @"{""type"":""renderStatus"",""state"":""tolerant"",""diagnostics"":[{""line"":3,""column"":4,""endLine"":3,""endColumn"":8,""message"":""unknown element `<Frob>`"",""code"":""unknownElement"",""element"":""Frob""}]}";

        private bool? _french;

        [TestInitialize]
        public void Init()
        {
            _french = DesignerText.ForceFrench;
            DesignerText.ForceFrench = true;
        }

        [TestCleanup]
        public void Cleanup() => DesignerText.ForceFrench = _french;

        [TestMethod]
        public void TryParseRenderStatus_ReadsTheRustWireShape()
        {
            Assert.IsTrue(DesignSurfaceProtocol.TryParseRenderStatus(TolerantLine, out var status));
            Assert.IsNotNull(status);
            Assert.AreEqual(DesignSurfaceRenderState.Tolerant, status!.State);
            Assert.AreEqual(1, status.Diagnostics.Count);
            var d = status.Diagnostics[0];
            Assert.AreEqual((3, 4, 3, 8), (d.Line, d.Column, d.EndLine, d.EndColumn));
            Assert.AreEqual("unknown element `<Frob>`", d.Message);
            Assert.IsTrue(d.IsUnknownElement);
            Assert.AreEqual("Frob", d.Element);

            Assert.IsTrue(DesignSurfaceProtocol.TryParseRenderStatus(@"{""type"":""renderStatus"",""state"":""clean"",""diagnostics"":[]}", out var clean));
            Assert.AreEqual(DesignSurfaceRenderState.Clean, clean!.State);
            Assert.AreEqual(0, clean.Diagnostics.Count);
        }

        [TestMethod]
        public void TryParseRenderStatus_RejectsOtherLinesAndSkipsBrokenEntries()
        {
            Assert.IsFalse(DesignSurfaceProtocol.TryParseRenderStatus(@"{""type"":""selectionChanged"",""id"":null}", out _));
            Assert.IsFalse(DesignSurfaceProtocol.TryParseRenderStatus(@"{""type"":""renderStatus"",""state"":""weird"",""diagnostics"":[]}", out _));
            Assert.IsFalse(DesignSurfaceProtocol.TryParseRenderStatus("not json", out _));
            // An entry without a message is skipped; an entry without an end ends where it starts.
            Assert.IsTrue(DesignSurfaceProtocol.TryParseRenderStatus(
                @"{""type"":""renderStatus"",""state"":""stale"",""diagnostics"":[{""line"":2,""column"":5},{""line"":7,""column"":1,""message"":""expected `>`""}]}", out var stale));
            Assert.AreEqual(DesignSurfaceRenderState.Stale, stale!.State);
            Assert.AreEqual(1, stale.Diagnostics.Count);
            Assert.AreEqual((7, 1, 7, 1), (stale.Diagnostics[0].Line, stale.Diagnostics[0].Column, stale.Diagnostics[0].EndLine, stale.Diagnostics[0].EndColumn));
            Assert.IsFalse(stale.Diagnostics[0].IsUnknownElement);
        }

        [TestMethod]
        public void Banner_IsHiddenForACleanView()
        {
            Assert.IsFalse(DesignErrorBannerModel.Build(null, null).IsVisible);
            Assert.IsFalse(DesignErrorBannerModel.Build(new DesignSurfaceRenderStatus(DesignSurfaceRenderState.Clean, new DesignSurfaceDiagnostic[0]), null).IsVisible);
        }

        [TestMethod]
        public void Banner_KeepsTheLastValidPreviewForAMalformedText()
        {
            var status = new DesignSurfaceRenderStatus(DesignSurfaceRenderState.Stale, new[]
            {
                new DesignSurfaceDiagnostic(9, 3, 9, 4, "expected `>`"),
                new DesignSurfaceDiagnostic(2, 12, 2, 20, "unterminated string"),
            });
            var model = DesignErrorBannerModel.Build(status, null);
            Assert.IsTrue(model.IsVisible && model.IsError);
            Assert.AreEqual("La vue contient des erreurs — dernier aperçu valide affiché.", model.Headline);
            // In text order, each with its line:column - what a click selects in the XML pane.
            CollectionAssert.AreEqual(new[] { "2:12", "9:3" }, model.Entries.Select(e => e.Location).ToArray());
            Assert.AreEqual("unterminated string", model.Entries[0].Text);
            Assert.AreEqual(20, model.Entries[0].Diagnostic.EndColumn);
        }

        [TestMethod]
        public void Banner_TellsAControlThePreviewLacksFromAMisspeltOne()
        {
            var status = new DesignSurfaceRenderStatus(DesignSurfaceRenderState.Tolerant, new[]
            {
                new DesignSurfaceDiagnostic(4, 6, 4, 12, "unknown element `<Ribbon>`", "unknownElement", "Ribbon"),
            });
            // Visual Studio's registry knows <Ribbon>: the bundled runtime does not yet - not an error of the view.
            var gap = DesignErrorBannerModel.Build(status, name => name == "Ribbon");
            Assert.IsTrue(gap.IsVisible);
            Assert.IsFalse(gap.IsError);
            Assert.IsTrue(gap.Entries[0].IsRuntimeGap);
            StringAssert.StartsWith(gap.Headline, "L'aperçu ne connaît pas encore 1 contrôle");
            StringAssert.Contains(gap.Entries[0].Text, "<Ribbon>");

            // Nobody knows <Ribbn>: an error, the element shown as a placeholder.
            var typo = DesignErrorBannerModel.Build(new DesignSurfaceRenderStatus(DesignSurfaceRenderState.Tolerant, new[]
            {
                new DesignSurfaceDiagnostic(4, 6, 4, 11, "unknown element `<Ribbn>`", "unknownElement", "Ribbn"),
                new DesignSurfaceDiagnostic(5, 14, 5, 18, "attribute `Width`: expected a number, found `wide`"),
            }), name => name == "Ribbon");
            Assert.IsTrue(typo.IsError);
            Assert.AreEqual(2, typo.Entries.Count(e => !e.IsRuntimeGap));
            StringAssert.StartsWith(typo.Headline, "La vue contient 2 erreurs");
        }

        [TestMethod]
        public void Banner_CountsWhatItListsBySeverityAndListsSyntaxErrorsFirst()
        {
            var status = new DesignSurfaceRenderStatus(DesignSurfaceRenderState.Tolerant, new[]
            {
                new DesignSurfaceDiagnostic(5, 28, 5, 32, "attribut inconnu `Text` sur `<Switch>`"),
                new DesignSurfaceDiagnostic(7, 4, 7, 10, "élément inconnu `<Avatar>`", "unknownElement", "Avatar"),
                new DesignSurfaceDiagnostic(8, 4, 8, 15, "élément inconnu `<Frobnicator>`", "unknownElement", "Frobnicator"),
            });
            var model = DesignErrorBannerModel.Build(status, name => name == "Avatar");
            Assert.AreEqual(3, model.Entries.Count);
            StringAssert.StartsWith(model.Headline, "La vue contient 2 erreurs et 1 avertissement —");
            Assert.AreEqual(1, model.Entries.Count(e => e.IsWarning));

            var stale = DesignErrorBannerModel.Build(new DesignSurfaceRenderStatus(DesignSurfaceRenderState.Stale, new[]
            {
                new DesignSurfaceDiagnostic(2, 1, 2, 5, "attribut inconnu `Colour` sur `<Label>`"),
                new DesignSurfaceDiagnostic(10, 16, 10, 16, "attribut ou `>` fermant la balise attendu", isSyntax: true),
                new DesignSurfaceDiagnostic(9, 4, 9, 4, "balise non terminée, `>` ou `/>` attendu", isSyntax: true),
            }), null);
            Assert.AreEqual("9:4", stale.Entries[0].Location, "the syntax error that keeps the last preview comes first");
            Assert.AreEqual(2, stale.Entries.Count, "the parser's follow-up errors are not listed");
        }

        [TestMethod]
        public void TryParseGoToSource_ReadsAMarkerClick()
        {
            Assert.IsTrue(DesignSurfaceProtocol.TryParseGoToSource(
                @"{""type"":""goToSource"",""diagnostic"":{""line"":5,""column"":28,""endLine"":5,""endColumn"":32,""message"":""m""}}", out var d));
            Assert.AreEqual((5, 28, 5, 32), (d!.Line, d.Column, d.EndLine, d.EndColumn));
            Assert.IsFalse(DesignSurfaceProtocol.TryParseGoToSource(@"{""type"":""goToSource""}", out _));
            Assert.IsTrue(DesignSurfaceProtocol.TryParseRenderStatus(
                @"{""type"":""renderStatus"",""state"":""stale"",""diagnostics"":[{""line"":2,""column"":5,""endLine"":2,""endColumn"":5,""message"":""m"",""syntax"":true}]}", out var s));
            Assert.IsTrue(s!.Diagnostics[0].IsSyntax);
        }

        [TestMethod]
        public void Banner_SaysWhenNothingCanBeShownOrTheFileWasRecovered()
        {
            var empty = DesignErrorBannerModel.Build(new DesignSurfaceRenderStatus(DesignSurfaceRenderState.Empty, new[] { new DesignSurfaceDiagnostic(1, 1, 1, 1, "expected an element name") }), null);
            StringAssert.StartsWith(empty.Headline, "La vue contient des erreurs — rien");
            var recovered = DesignErrorBannerModel.Build(new DesignSurfaceRenderStatus(DesignSurfaceRenderState.Recovered, new[] { new DesignSurfaceDiagnostic(3, 9, 3, 9, "expected `\"`") }), null);
            StringAssert.StartsWith(recovered.Headline, "La vue n'est pas du XML bien formé");
            Assert.IsTrue(recovered.IsError);
        }
    }
}
