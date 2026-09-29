using System.Collections.Generic;
using System.Linq;
using Kubuno.VisualStudio.Core.ProjectProperties;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Tests.ProjectProperties
{
    [TestClass]
    public class PropertyListEncodingTests
    {
        [TestMethod]
        public void Strings_RoundTripWithEscapes()
        {
            string encoded = PropertyListEncoding.EncodeStrings(new[] { "a", "b,c", "d=e", "f/g", "a" });
            Assert.AreEqual("a=False,b/,c=False,d/=e=False,f//g=False", encoded);
            CollectionAssert.AreEqual(new[] { "a", "b,c", "d=e", "f/g" }, PropertyListEncoding.DecodeStrings(encoded).ToArray());
        }

        [TestMethod]
        public void Decode_IgnoresEmptyAndMalformedEntries()
        {
            Assert.AreEqual(0, PropertyListEncoding.DecodeStrings(null).Count);
            Assert.AreEqual(0, PropertyListEncoding.DecodeStrings("  ").Count);
            CollectionAssert.AreEqual(new[] { "x" }, PropertyListEncoding.DecodeStrings("=True,x=True,novalue").ToArray());
        }

        [TestMethod]
        public void EnvironmentLines_ConvertBothWays()
        {
            string lines = "RUST_LOG=debug\r\n# comment\r\n\r\nPATH=C:\\a;C:\\b=c\n KEY = spaced";
            string pairs = PropertyListEncoding.EnvironmentLinesToPairs(lines);
            Assert.AreEqual("RUST_LOG=debug,PATH=C:\\a;C:\\b/=c,KEY= spaced", pairs);
            Assert.AreEqual("RUST_LOG=debug\r\nPATH=C:\\a;C:\\b=c\r\nKEY= spaced", PropertyListEncoding.PairsToEnvironmentLines(pairs));
        }

        [TestMethod]
        public void MSBuildList_IsSplitAndTrimmed()
        {
            CollectionAssert.AreEqual(new[] { "a", "b" }, PropertyListEncoding.SplitMSBuildList(" a ;; b;").ToArray());
            Assert.AreEqual(0, PropertyListEncoding.SplitMSBuildList(null).Count);
        }
    }

    [TestClass]
    public class PropertyValidationTests
    {
        [TestMethod]
        [DataRow("hello", true)]
        [DataRow("hello-rust_2", true)]
        [DataRow("Kubuno", true)]
        [DataRow("2fast", false)]
        [DataRow("_x", false)]
        [DataRow("has space", false)]
        [DataRow("nul", false)]
        [DataRow("", false)]
        public void CrateNames(string name, bool valid) => Assert.AreEqual(valid, PropertyValidation.IsValidCrateName(name));

        [TestMethod]
        [DataRow("1.0.0", true)]
        [DataRow("0.1.0-alpha.1+build.5", true)]
        [DataRow("1.0", false)]
        [DataRow("01.0.0", false)]
        [DataRow("v1.0.0", false)]
        public void SemVer(string version, bool valid) => Assert.AreEqual(valid, PropertyValidation.IsValidSemVer(version));

        [TestMethod]
        [DataRow("1.80", true)]
        [DataRow("1.80.1", true)]
        [DataRow("1", false)]
        [DataRow("1.80.1.2", false)]
        [DataRow("stable", false)]
        public void RustVersions(string version, bool valid) => Assert.AreEqual(valid, PropertyValidation.IsValidRustVersion(version));

        [TestMethod]
        [DataRow("MIT", true)]
        [DataRow("MIT OR Apache-2.0", true)]
        [DataRow("MIT/Apache-2.0", true)]
        [DataRow("(MIT OR Apache-2.0) AND BSD-3-Clause", true)]
        [DataRow("GPL-2.0-or-later WITH Classpath-exception-2.0", true)]
        [DataRow("GPL-2.0+", true)]
        [DataRow("MIT OR", false)]
        [DataRow("(MIT", false)]
        [DataRow("MIT AND AND BSD", false)]
        [DataRow("MIT WITH", false)]
        [DataRow("MIT; rm -rf", false)]
        [DataRow("", false)]
        public void SpdxExpressions(string expression, bool valid) => Assert.AreEqual(valid, PropertyValidation.IsValidSpdxExpression(expression));

        [TestMethod]
        public void UrlsKeywordsAndRanges()
        {
            Assert.IsTrue(PropertyValidation.IsValidUrl("https://github.com/kubuno/vskubuno"));
            Assert.IsFalse(PropertyValidation.IsValidUrl("github.com/kubuno"));
            Assert.IsTrue(PropertyValidation.IsValidKeyword("gui"));
            Assert.IsTrue(PropertyValidation.IsValidKeyword("c++"));
            Assert.IsFalse(PropertyValidation.IsValidKeyword("9lives"));
            Assert.IsFalse(PropertyValidation.IsValidKeyword("abcdefghijklmnopqrstu"));
            Assert.IsTrue(PropertyValidation.IsIntegerInRange("16", 1, 16));
            Assert.IsFalse(PropertyValidation.IsIntegerInRange("-1", 1, 16));
            Assert.IsFalse(PropertyValidation.IsIntegerInRange("17", 1, 16));
        }
    }

    [TestClass]
    public class WindowsSubsystemAttributeTests
    {
        [TestMethod]
        [DataRow("fn main() {}", "console")]
        [DataRow("#![windows_subsystem = \"windows\"]\nfn main() {}", "windows")]
        [DataRow("#![windows_subsystem=\"console\"]\nfn main() {}", "console")]
        [DataRow("//! Docs\n#![cfg_attr(not(debug_assertions), windows_subsystem = \"windows\")]\nfn main() {}", "windows-release")]
        [DataRow("#![cfg_attr(feature = \"gui\", windows_subsystem = \"windows\")]\nfn main() {}", "windows")]
        [DataRow("/* header */\n#![allow(dead_code)]\n#![windows_subsystem = \"windows\"] // no console\nfn main() {}", "windows")]
        [DataRow("fn main() {}\n// #![windows_subsystem = \"windows\"] in a comment after an item\n", "console")]
        public void Read(string source, string expected) => Assert.AreEqual(expected, WindowsSubsystemAttribute.Read(source));

        [TestMethod]
        public void Write_InsertsAfterLeadingDocCommentsAndAttributes()
        {
            const string source = "//! My app.\n//! Second line.\n#![allow(unused)]\n\nuse std::io;\nfn main() {}\n";
            TextEdit? edit = WindowsSubsystemAttribute.Write(source, "windows");
            Assert.IsNotNull(edit);
            Assert.AreEqual("//! My app.\n//! Second line.\n#![allow(unused)]\n#![windows_subsystem = \"windows\"]\n\nuse std::io;\nfn main() {}\n", edit.Value.ApplyTo(source));
        }

        [TestMethod]
        public void Write_ReplacesTheExistingLine_KeepingCrLf()
        {
            const string source = "//! App\r\n#![windows_subsystem = \"windows\"]\r\nfn main() {}\r\n";
            TextEdit? edit = WindowsSubsystemAttribute.Write(source, "console");
            Assert.AreEqual("//! App\r\n#![windows_subsystem = \"console\"]\r\nfn main() {}\r\n", edit!.Value.ApplyTo(source));
            Assert.AreEqual(0, edit.Value.Start - source.IndexOf("#!", System.StringComparison.Ordinal), "only the attribute line is replaced");
        }

        [TestMethod]
        public void Write_IsANoOp_WhenEquivalent()
        {
            Assert.IsNull(WindowsSubsystemAttribute.Write("fn main() {}", "console"));
            Assert.IsNull(WindowsSubsystemAttribute.Write("#![windows_subsystem=\"windows\"]\nfn main() {}", "windows"));
        }

        [TestMethod]
        public void Write_AtTheTopOfAPlainFile()
        {
            const string source = "fn main() {}\n";
            Assert.AreEqual("#![cfg_attr(not(debug_assertions), windows_subsystem = \"windows\")]\nfn main() {}\n", WindowsSubsystemAttribute.Write(source, "windows-release")!.Value.ApplyTo(source));
        }
    }

    [TestClass]
    public class TargetTriplesTests
    {
        [TestMethod]
        [DataRow(null, "Windows (x86_64, MSVC)")]
        [DataRow("", "Windows (x86_64, MSVC)")]
        [DataRow("aarch64-pc-windows-msvc", "Windows (aarch64, MSVC)")]
        [DataRow("x86_64-pc-windows-gnu", "Windows (x86_64, GNU)")]
        [DataRow("x86_64-unknown-linux-musl", "Linux (x86_64, musl)")]
        [DataRow("aarch64-apple-darwin", "macOS (aarch64)")]
        [DataRow("wasm32-unknown-unknown", "WebAssembly (wasm32)")]
        [DataRow("wasm32-wasip1", "WASI (wasm32)")]
        [DataRow("thumbv7em-none-eabihf", "bare metal (thumbv7em, eabihf)")]
        public void Describe(string? triple, string expected) => Assert.AreEqual(expected, TargetTriples.Describe(triple));
    }
}
