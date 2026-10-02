using System.Collections.Generic;
using System.Linq;
using Kubuno.Shared.Logic.QuickInfo;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.QuickInfo
{
    /// <summary>Reads the C#-style QuickInfo model back (the same helpers as the Rust layer's hover tests).</summary>
    internal static class QuickInfoAssertions
    {
        internal static QuickInfoTextKind KindOf(IEnumerable<QuickInfoRun> runs, string text)
        {
            var run = runs.FirstOrDefault(r => r.Text == text) ?? throw new AssertFailedException($"no run '{text}' in: {string.Join("|", runs.Select(r => r.Text))}");
            return run.Kind;
        }

        /// <summary>The header row: Wrapped[image, text or Stacked[text, path]].</summary>
        internal static (QuickInfoImage Icon, QuickInfoText Signature, string? Path) Header(QuickInfoElement tooltip)
        {
            var root = (QuickInfoContainer)tooltip;
            Assert.AreEqual(QuickInfoContainerStyle.Stacked | QuickInfoContainerStyle.VerticalPadding, root.Style);
            var header = (QuickInfoContainer)root.Children[0];
            Assert.AreEqual(QuickInfoContainerStyle.Wrapped, header.Style);
            var icon = (QuickInfoImage)header.Children[0];
            if (header.Children[1] is QuickInfoText signature)
            {
                return (icon, signature, null);
            }

            var stacked = (QuickInfoContainer)header.Children[1];
            var path = (QuickInfoText)stacked.Children[1];
            Assert.IsTrue(path.Runs.All(r => r.Kind == QuickInfoTextKind.Muted), "the path is grey");
            return (icon, (QuickInfoText)stacked.Children[0], path.ToPlainText());
        }
    }
}