using System.Linq;
using System.Text.Json.Nodes;
using Kubuno.Rust.Logic.IntelliSense;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Rust.Tests.IntelliSense
{
    [TestClass]
    public sealed class RustAnalyzerHandshakeTests
    {
        [TestMethod]
        public void TheClientAnnouncesSnippetsLabelDetailsAndTheCommandsKubunoHandles()
        {
            var request = "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"initialize\",\"params\":{\"capabilities\":{\"textDocument\":{\"completion\":{\"completionItem\":{\"snippetSupport\":false,\"resolveSupport\":{\"properties\":[\"textEdit\",\"additionalTextEdits\"]}},\"completionList\":{\"itemDefaults\":[\"editRange\"]}}}}}}";

            var rewritten = JsonNode.Parse(RustAnalyzerHandshake.RewriteInitializeRequest(request)!)!;

            var completion = rewritten["params"]!["capabilities"]!["textDocument"]!["completion"]!;
            Assert.AreEqual(true, (bool)completion["completionItem"]!["snippetSupport"]!);
            Assert.AreEqual(true, (bool)completion["completionItem"]!["labelDetailsSupport"]!);
            CollectionAssert.AreEqual(new[] { "additionalTextEdits" }, completion["completionItem"]!["resolveSupport"]!["properties"]!.AsArray().Select(p => (string)p!).ToArray());
            Assert.IsNull(completion["completionList"]!["itemDefaults"]);
            var commands = rewritten["params"]!["capabilities"]!["experimental"]!["commands"]!["commands"]!.AsArray().Select(c => (string)c!).ToList();
            CollectionAssert.Contains(commands, RustAnalyzerHandshake.TriggerParameterHintsCommand);
            CollectionAssert.Contains(commands, RustAnalyzerHandshake.ShowReferencesCommand);
        }

        [TestMethod]
        public void TheServerLegendBecomesCSharpClassificationsAndCompletionIsHiddenFromVisualStudio()
        {
            var response = "{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"capabilities\":{\"completionProvider\":{\"resolveProvider\":true},\"semanticTokensProvider\":{\"legend\":{\"tokenTypes\":[\"struct\",\"variable\",\"keyword\"],\"tokenModifiers\":[\"mutable\",\"controlFlow\"]},\"full\":{\"delta\":true}}}}}";
            RustSemanticTokenMap? map = null;

            var rewritten = JsonNode.Parse(RustAnalyzerHandshake.RewriteInitializeResponse(response, m => map = m)!)!;

            var capabilities = rewritten["result"]!["capabilities"]!;
            Assert.IsNull(capabilities["completionProvider"]);
            Assert.IsNotNull(map);
            var legend = capabilities["semanticTokensProvider"]!["legend"]!;
            CollectionAssert.AreEqual(map!.Legend.ToArray(), legend["tokenTypes"]!.AsArray().Select(t => (string)t!).ToArray());
            CollectionAssert.AreEqual(RustSemanticTokenMap.Modifiers.ToArray(), legend["tokenModifiers"]!.AsArray().Select(t => (string)t!).ToArray());
            CollectionAssert.Contains(map.Legend.ToList(), "struct name");
        }

        [TestMethod]
        public void AnAnswerWithoutSemanticTokensOnlyLosesItsCompletionCapability()
        {
            var response = "{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"capabilities\":{\"completionProvider\":{},\"hoverProvider\":true}}}";

            var rewritten = JsonNode.Parse(RustAnalyzerHandshake.RewriteInitializeResponse(response, _ => Assert.Fail("no legend"))!)!;

            Assert.IsNull(rewritten["result"]!["capabilities"]!["completionProvider"]);
            Assert.AreEqual(true, (bool)rewritten["result"]!["capabilities"]!["hoverProvider"]!);
        }

        [TestMethod]
        public void RustAnalyzerStartsWithReferenceLensesAndNoRunLenses()
        {
            var options = RustAnalyzerHandshake.InitializationOptions();

            Assert.AreEqual(true, (bool)options["lens"]!["references"]!["method"]!["enable"]!);
            Assert.AreEqual(false, (bool)options["lens"]!["run"]!["enable"]!);
            Assert.AreEqual("fill_arguments", (string)options["completion"]!["callable"]!["snippets"]!);
            Assert.AreEqual("all_symbols", (string)options["workspace"]!["symbol"]!["search"]!["kind"]!);
        }
    }
}
