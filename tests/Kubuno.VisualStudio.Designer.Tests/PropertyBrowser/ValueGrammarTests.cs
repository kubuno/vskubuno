using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using Kubuno.VisualStudio.Designer.PropertyBrowser;
using Kubuno.VisualStudio.Designer.Registry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Designer.Tests.PropertyBrowser
{
    /// <summary>The colour, font and composite value grammars shared with the runtime, and the WCAG contrast math.</summary>
    [TestClass]
    public class ValueGrammarTests
    {
        [TestMethod]
        public void Colors_ParseEveryForm_AndNormalize()
        {
            Assert.IsTrue(ColorText.TryParse("primary", out var token));
            Assert.AreEqual(ColorValueKind.Token, token.Kind);
            Assert.AreEqual("Primary", token.Text);
            Assert.AreEqual(Color.FromArgb(0x1A, 0x73, 0xE8).ToArgb(), token.Light.ToArgb());
            Assert.AreEqual(Color.FromArgb(0x8A, 0xB4, 0xF8).ToArgb(), token.Dark.ToArgb());
            Assert.IsFalse(token.IsFree);

            Assert.AreEqual("#FF8800", ColorText.Normalize("#f80"));
            Assert.AreEqual("#FF8800", ColorText.Normalize(" #ff8800 "));
            Assert.AreEqual("#11223380", ColorText.Normalize("#11223380"));
            Assert.AreEqual(0x80, ColorText.ParseHex("#11223380")!.Value.A);
            Assert.AreEqual("CornflowerBlue", ColorText.Normalize("cornflowerblue"));
            Assert.AreEqual("Transparent", ColorText.WebColorNames[0]);
            Assert.AreEqual("WindowText", ColorText.Normalize("windowtext"));
            Assert.IsTrue(ColorText.TryParse("Control", out var system) && system.Kind == ColorValueKind.System && !system.IsFree);
            Assert.IsTrue(ColorText.TryParse("Red", out var web) && web.Kind == ColorValueKind.Web && web.IsFree);
            Assert.IsTrue(ColorText.TryParse("", out var empty) && empty.Kind == ColorValueKind.Empty);
            Assert.AreEqual("{Binding Accent}", ColorText.Normalize("{Binding Accent}"));
            foreach (var bad in new[] { "#12", "#GG0000", "blurple", "#1234567" })
            {
                Assert.IsFalse(ColorText.TryParse(bad, out _), bad);
                Assert.ThrowsExactly<ArgumentException>(() => ColorText.Normalize(bad), bad);
            }

            Assert.AreEqual("#0A0B0C", ColorText.FormatHex(Color.FromArgb(10, 11, 12)));
            CollectionAssert.DoesNotContain(ColorText.WebColorNames.ToArray(), "Control", "system colours are not web colours");
            Assert.IsTrue(ColorText.SystemColorNames.All(n => ColorText.SystemColor(n) != Color.Empty), "every system name resolves");
        }

        [TestMethod]
        public void ThemeTokens_HaveDistinctNames_ValidColours_AndBothLanguages()
        {
            Assert.AreEqual(ThemeTokens.All.Count, ThemeTokens.All.Select(t => t.Name).Distinct().Count());
            foreach (var t in ThemeTokens.All)
            {
                Assert.IsNotNull(ColorText.ParseHex(t.Light), t.Name);
                Assert.IsNotNull(ColorText.ParseHex(t.Dark), t.Name);
                CollectionAssert.Contains(ColorText.SystemColorNames.ToArray(), t.HighContrast, t.Name);
                Assert.IsFalse(string.IsNullOrEmpty(t.Doc) || string.IsNullOrEmpty(t.DocFr), t.Name);
                Assert.IsFalse(ColorText.WebColorNames.Contains(t.Name, StringComparer.OrdinalIgnoreCase), t.Name + " would be ambiguous");
            }
        }

        /// <summary>The runtime's own table, exported by a Rust test into the fixture when it exists.</summary>
        [TestMethod]
        public void ThemeTokens_MatchTheRuntimesTable_WhenTheFixtureExists()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", "theme-tokens.json");
            if (!File.Exists(path))
            {
                Assert.Inconclusive("theme-tokens.json is generated from the runtime; not present yet.");
                return;
            }

            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var exported = json.RootElement.EnumerateArray().ToList();
            Assert.AreEqual(ThemeTokens.All.Count, exported.Count);
            foreach (var e in exported)
            {
                var name = e.GetProperty("name").GetString();
                var token = ThemeTokens.Find(name) ?? throw new AssertFailedException("unknown token " + name);
                Assert.AreEqual(token.Name, name);
                Assert.AreEqual(ColorText.ParseHex(token.Light), ColorText.ParseHex(e.GetProperty("light").GetString()), name);
                Assert.AreEqual(ColorText.ParseHex(token.Dark), ColorText.ParseHex(e.GetProperty("dark").GetString()), name);
                Assert.AreEqual(token.HighContrast, e.GetProperty("high_contrast").GetString(), name);
            }
        }

        [TestMethod]
        public void Contrast_FollowsWcag()
        {
            Assert.AreEqual(21.0, ColorText.ContrastRatio(Color.Black, Color.White), 0.01);
            Assert.AreEqual(1.0, ColorText.ContrastRatio(Color.Red, Color.Red), 0.001);
            Assert.AreEqual(4.48, ColorText.ContrastRatio(ColorText.ParseHex("#777777")!.Value, Color.White), 0.01);
            Assert.IsTrue(ColorText.ContrastRatio(ColorText.ParseHex("#76767680")!.Value, Color.White) < 2, "a translucent colour is composited first");

            string? back = "#FFFFFF";
            var counterpart = ColorContrast.Counterpart("ForeColor", name => name == "BackColor" ? back : null)!;
            Assert.AreEqual("#FFFFFF", counterpart.Text);
            ColorText.TryParse("#777777", out var grey);
            var ratio = ColorContrast.Of("ForeColor", grey, counterpart)!.Value;
            Assert.AreEqual(4.48, ratio.Light, 0.01);

            back = null;
            Assert.AreEqual("Background", ColorContrast.Counterpart("ForeColor", _ => back)!.Text);
            Assert.AreEqual("TextPrimary", ColorContrast.Counterpart("BackColor", _ => null)!.Text);
            Assert.IsNull(ColorContrast.Counterpart("Text", _ => null));

            // Tokens are read in each theme: the text colour passes on the background in both, the accent as text only in dark.
            ColorText.TryParse("TextPrimary", out var text);
            var readable = ColorContrast.Of("ForeColor", text, ColorContrast.Counterpart("ForeColor", _ => null))!.Value;
            Assert.IsTrue(readable.Light >= 4.5 && readable.Dark >= 4.5, $"{readable.Light} / {readable.Dark}");
            ColorText.TryParse("Primary", out var primary);
            var accent = ColorContrast.Of("ForeColor", primary, ColorContrast.Counterpart("ForeColor", _ => null))!.Value;
            Assert.IsTrue(accent.Light < 4.5 && accent.Dark >= 4.5, $"{accent.Light} / {accent.Dark}");
        }

        [TestMethod]
        public void Fonts_RoundTripTheWinFormsText()
        {
            Assert.IsTrue(FontText.TryParse("Segoe UI, 12pt, style=Bold, Italic", out var font));
            Assert.AreEqual("Segoe UI", font.Family);
            Assert.AreEqual(12f, font.Size);
            Assert.IsTrue(font.Bold && font.Italic && !font.Underline);
            Assert.AreEqual("Segoe UI, 12pt, style=Bold, Italic", FontText.Format(font));
            Assert.AreEqual("Consolas, 10.5pt", FontText.Normalize("Consolas,10.5"));
            Assert.AreEqual("Arial, 16px, style=Underline, Strikeout", FontText.Normalize("Arial, 16px, underline, strikeout"));
            Assert.IsTrue(FontText.TryParse("Arial, 16px", out var px) && px.Points == 12f);
            Assert.AreEqual(string.Empty, FontText.Normalize("  "));
            foreach (var bad in new[] { "Arial", "Arial, big", "Arial, 12pt, style=Wavy", ", 12pt", "Arial, -3pt" })
            {
                Assert.ThrowsExactly<ArgumentException>(() => FontText.Normalize(bad), bad);
            }

            using var drawing = new Font("Arial", 11.25f, FontStyle.Bold);
            Assert.AreEqual("Arial, 11.25pt, style=Bold", FontText.FromDrawingFont(drawing));
            using var back = FontText.ToDrawingFont("Arial, 9pt, style=Italic")!;
            Assert.IsTrue(back.Italic && back.SizeInPoints == 9f);
            Assert.IsNotNull(FontText.ToDrawingFont(string.Empty), "the ambient font");
        }

        [TestMethod]
        public void Composites_ParseLikeWinForms()
        {
            CollectionAssert.AreEqual(new[] { 3f, 3f, 3f, 3f }, CompositeText.ParsePadding("3"));
            CollectionAssert.AreEqual(new[] { 1f, 2f, 3f, 4f }, CompositeText.ParsePadding(" 1, 2 ,3,4"));
            Assert.IsNull(CompositeText.ParsePadding("1, 2"));
            CollectionAssert.AreEqual(new[] { 10f, 20.5f }, CompositeText.ParseList("10; 20.5", 2));
            Assert.AreEqual("1.5, 2", CompositeText.FormatList(1.5f, 2f));
            Assert.AreEqual(80f, CompositeText.ParseOpacity("80 %"));
            Assert.AreEqual(80f, CompositeText.ParseOpacity("80"));
            Assert.AreEqual(50f, CompositeText.ParseOpacity("0.5"));
            Assert.AreEqual(0f, CompositeText.ParseOpacity("-4%"));
            Assert.IsNull(CompositeText.ParseOpacity("opaque"));
            Assert.AreEqual("80 %", CompositeText.FormatOpacity(80f));
        }
    }
}
