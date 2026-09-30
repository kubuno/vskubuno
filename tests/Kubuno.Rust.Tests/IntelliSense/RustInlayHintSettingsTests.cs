using Kubuno.Rust.Logic.IntelliSense;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Rust.Tests.IntelliSense
{
    [TestClass]
    public sealed class RustInlayHintSettingsTests
    {
        [TestMethod]
        public void DefaultsMapToRustAnalyzersInlayHintsSettings()
        {
            var json = new RustInlayHintSettings().ToRustAnalyzerSettings();

            Assert.AreEqual(true, (bool)json["parameterHints"]!["enable"]!);
            Assert.AreEqual(true, (bool)json["typeHints"]!["enable"]!);
            Assert.AreEqual(true, (bool)json["typeHints"]!["hideNamedConstructor"]!);
            Assert.AreEqual(true, (bool)json["typeHints"]!["hideClosureInitialization"]!);
            Assert.AreEqual(false, (bool)json["typeHints"]!["hideClosureParameter"]!);
            Assert.AreEqual("never", (string)json["closureReturnTypeHints"]!["enable"]!);
            Assert.AreEqual("never", (string)json["lifetimeElisionHints"]!["enable"]!);
            Assert.AreEqual(false, (bool)json["bindingModeHints"]!["enable"]!);
            Assert.AreEqual(false, (bool)json["closingBraceHints"]!["enable"]!);
        }

        [TestMethod]
        public void EachKindMapsToItsOwnSetting()
        {
            var json = new RustInlayHintSettings
            {
                ParameterNames = false,
                Types = false,
                ClosureParameterTypes = false,
                Chaining = false,
                ClosureReturnTypes = RustClosureReturnTypeHints.WithBlock,
                LifetimeElision = RustLifetimeElisionHints.SkipTrivial,
                BindingModes = true,
                ClosingBraces = true,
            }.ToRustAnalyzerSettings();

            Assert.AreEqual(false, (bool)json["parameterHints"]!["enable"]!);
            Assert.AreEqual(false, (bool)json["typeHints"]!["enable"]!);
            Assert.AreEqual(true, (bool)json["typeHints"]!["hideClosureParameter"]!);
            Assert.AreEqual(false, (bool)json["chainingHints"]!["enable"]!);
            Assert.AreEqual("with_block", (string)json["closureReturnTypeHints"]!["enable"]!);
            Assert.AreEqual("skip_trivial", (string)json["lifetimeElisionHints"]!["enable"]!);
            Assert.AreEqual(true, (bool)json["bindingModeHints"]!["enable"]!);
            Assert.AreEqual(true, (bool)json["closingBraceHints"]!["enable"]!);
        }

        [TestMethod]
        public void TheHandshakeCarriesTheChosenHints()
        {
            var options = RustAnalyzerHandshake.InitializationOptions(new RustInlayHintSettings { Chaining = false });

            Assert.AreEqual(false, (bool)options["inlayHints"]!["chainingHints"]!["enable"]!);
        }

        [TestMethod]
        [DataRow("5, b)", true)]
        [DataRow("-3)", true)]
        [DataRow("1.5f32,", true)]
        [DataRow("0xFF_u8 )", true)]
        [DataRow("\"a, b\", x", true)]
        [DataRow("'c')", true)]
        [DataRow("b'c')", true)]
        [DataRow("r#\"raw\"#)", true)]
        [DataRow("true)", true)]
        [DataRow("false", true)]
        [DataRow("truely)", false)]
        [DataRow("name)", false)]
        [DataRow("3.max(2))", false)]
        [DataRow("5 + x)", false)]
        [DataRow("\"a\".to_string())", false)]
        [DataRow("-x)", false)]
        [DataRow("", false)]
        public void LiteralArgumentsAreRecognized(string argument, bool literal) =>
            Assert.AreEqual(literal, RustArgumentText.IsLiteral(argument));

        [TestMethod]
        public void ParameterHintsFollowTheArgumentKindOptions()
        {
            var noLiterals = new RustInlayHintSettings { ParameterNamesForLiterals = false };
            Assert.IsFalse(noLiterals.KeepsParameterHint("5)"));
            Assert.IsTrue(noLiterals.KeepsParameterHint("count)"));
            Assert.IsTrue(noLiterals.FiltersParameterNames);

            var onlyLiterals = new RustInlayHintSettings { ParameterNamesForOtherArguments = false };
            Assert.IsTrue(onlyLiterals.KeepsParameterHint("\"x\")"));
            Assert.IsFalse(onlyLiterals.KeepsParameterHint("count)"));

            Assert.IsFalse(new RustInlayHintSettings().FiltersParameterNames);
            Assert.IsFalse(new RustInlayHintSettings { ParameterNames = false }.KeepsParameterHint("5)"));
        }

        [TestMethod]
        public void OnlyTheOldDefaultIsMovedToTheNewDefault()
        {
            Assert.AreEqual(RustInlayHintsMode.WhilePressingAltF1, RustInlayHintsMigration.Migrate(0, RustInlayHintsMode.Always));
            Assert.AreEqual(RustInlayHintsMode.Never, RustInlayHintsMigration.Migrate(0, RustInlayHintsMode.Never));
            Assert.AreEqual(RustInlayHintsMode.VisualStudioSetting, RustInlayHintsMigration.Migrate(1, RustInlayHintsMode.VisualStudioSetting));
            Assert.AreEqual(RustInlayHintsMode.Always, RustInlayHintsMigration.Migrate(RustInlayHintsMigration.CurrentVersion, RustInlayHintsMode.Always));
        }
    }
}
