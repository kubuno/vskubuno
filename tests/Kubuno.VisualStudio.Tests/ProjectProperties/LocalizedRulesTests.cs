using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Tests.ProjectProperties
{
    /// <summary>
    /// Guards the French copies of the Project Properties rule files (Rules\fr\): they must stay
    /// structurally identical to the English references, with only the user-visible strings translated.
    /// </summary>
    [TestClass]
    public class LocalizedRulesTests
    {
        private static readonly string[] RuleFiles =
        {
            "rust_application.xaml",
            "rust_build.xaml",
            "rust_package.xaml",
            "rust_codeanalysis.xaml",
            "rust_debug.xaml",
            "rust_resources.xaml",
            "rust_settings.xaml",
        };

        // NameValuePair names whose Value is user-visible (or a search aid) and may differ between languages.
        private static readonly HashSet<string> TranslatedPairNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "EvaluatedValueFailedValidationMessage",
            "TypeDescriptorText",
            "FileTypeFilter",
            "SearchTerms",
        };

        // DisplayName values that are legitimately identical in French (product/page names, license and
        // lint identifiers, cognates). Kept short and explicit on purpose.
        private static readonly HashSet<string> IdenticalDisplayNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Application", "Build", "Package", "Clippy", "Version", "Description", "Copyright",
            "Windows 10 / Windows 11", "Windows 8.1", "Windows 8", "Windows 7",
            "Unix (LF)", "Windows (CRLF)",
            "MIT", "ISC", "zlib", "Apache 2.0", "BSD 3-Clause", "BSD 2-Clause", "Zero-Clause BSD", "The Unlicense",
            "Thin", "unsafe_code", "missing_docs",
        };

        private static readonly Regex IdenticalDisplayNamePattern =
            new Regex(@"^(Rust \d{4}|clippy::\w+)$", RegexOptions.CultureInvariant);

        private static string RulesDirectory
        {
            get
            {
                var start = Path.GetDirectoryName(typeof(LocalizedRulesTests).Assembly.Location) ?? AppContext.BaseDirectory;
                for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                {
                    var candidate = Path.Combine(dir.FullName, "sdk", "Kubuno.Rust.Sdk", "Sdk", "Rules");
                    if (Directory.Exists(candidate))
                        return candidate;
                }
                throw new DirectoryNotFoundException("Could not locate sdk\\Kubuno.Rust.Sdk\\Sdk\\Rules above " + start);
            }
        }

        private static XDocument LoadEnglish(string file) => XDocument.Load(Path.Combine(RulesDirectory, file));

        private static XDocument LoadFrench(string file) => XDocument.Load(Path.Combine(RulesDirectory, "fr", file));

        private static string? PairName(XElement e) =>
            e.Name.LocalName == "NameValuePair" ? (string?)e.Attribute("Name") : null;

        private static bool IsTranslatedAttribute(XElement e, XAttribute a)
        {
            var name = a.Name.LocalName;
            if (name == "DisplayName" || name == "Description")
                return true;
            if (name == "Value" && PairName(e) is string pair && TranslatedPairNames.Contains(pair))
                return true;
            return false;
        }

        [TestMethod]
        public void EveryEnglishRuleFileHasAFrenchCounterpartAndViceVersa()
        {
            var rules = RulesDirectory;
            foreach (var file in RuleFiles)
            {
                Assert.IsTrue(File.Exists(Path.Combine(rules, file)), "Missing English file " + file);
                Assert.IsTrue(File.Exists(Path.Combine(rules, "fr", file)), "Missing French file " + file);
            }

            var frenchFiles = Directory.GetFiles(Path.Combine(rules, "fr"), "*.xaml")
                .Select(p => Path.GetFileName(p)!)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();
            CollectionAssert.AreEqual(RuleFiles.OrderBy(n => n, StringComparer.Ordinal).ToArray(), frenchFiles,
                "Rules\\fr must contain exactly the localized rule files.");
        }

        [TestMethod]
        [DataRow("rust_application.xaml")]
        [DataRow("rust_build.xaml")]
        [DataRow("rust_package.xaml")]
        [DataRow("rust_codeanalysis.xaml")]
        [DataRow("rust_debug.xaml")]
        [DataRow("rust_resources.xaml")]
        [DataRow("rust_settings.xaml")]
        public void FrenchFileHasTheSameElementSequence(string file)
        {
            var en = LoadEnglish(file).Descendants().Select(e => e.Name.LocalName).ToArray();
            var fr = LoadFrench(file).Descendants().Select(e => e.Name.LocalName).ToArray();
            CollectionAssert.AreEqual(en, fr, file + ": element sequence differs.");
        }

        [TestMethod]
        [DataRow("rust_application.xaml")]
        [DataRow("rust_build.xaml")]
        [DataRow("rust_package.xaml")]
        [DataRow("rust_codeanalysis.xaml")]
        [DataRow("rust_debug.xaml")]
        [DataRow("rust_resources.xaml")]
        [DataRow("rust_settings.xaml")]
        public void FrenchFileChangesOnlyTranslatableAttributes(string file)
        {
            var en = LoadEnglish(file).Descendants().ToArray();
            var fr = LoadFrench(file).Descendants().ToArray();
            Assert.AreEqual(en.Length, fr.Length, file + ": element count differs.");

            var problems = new List<string>();
            for (var i = 0; i < en.Length; i++)
            {
                var e = en[i];
                var f = fr[i];
                var where = file + " #" + i + " <" + e.Name.LocalName + " " + (string?)e.Attribute("Name") + ">";

                var enAttrs = e.Attributes().Select(a => a.Name.ToString()).OrderBy(n => n, StringComparer.Ordinal);
                var frAttrs = f.Attributes().Select(a => a.Name.ToString()).OrderBy(n => n, StringComparer.Ordinal);
                if (!enAttrs.SequenceEqual(frAttrs))
                    problems.Add(where + ": attribute names differ.");

                foreach (var a in e.Attributes())
                {
                    if (IsTranslatedAttribute(e, a))
                        continue;
                    var other = f.Attribute(a.Name);
                    if (other == null || !string.Equals(a.Value, other.Value, StringComparison.Ordinal))
                        problems.Add(where + ": attribute " + a.Name.LocalName + " differs ('" + a.Value + "' vs '" + other?.Value + "').");
                }

                // Leaf text (e.g. <NameValuePair.Value> visibility conditions) must be untouched.
                if (!e.HasElements && !string.Equals(e.Value, f.Value, StringComparison.Ordinal))
                    problems.Add(where + ": text content differs.");
            }

            Assert.AreEqual(0, problems.Count, string.Join(Environment.NewLine, problems));
        }

        [TestMethod]
        [DataRow("rust_application.xaml")]
        [DataRow("rust_build.xaml")]
        [DataRow("rust_package.xaml")]
        [DataRow("rust_codeanalysis.xaml")]
        [DataRow("rust_debug.xaml")]
        [DataRow("rust_resources.xaml")]
        [DataRow("rust_settings.xaml")]
        public void FileTypeFilterKeepsTheSamePatterns(string file)
        {
            var en = FileTypeFilters(LoadEnglish(file));
            var fr = FileTypeFilters(LoadFrench(file));
            Assert.AreEqual(en.Count, fr.Count, file + ": FileTypeFilter count differs.");
            for (var i = 0; i < en.Count; i++)
            {
                var enSegments = en[i].Split('|');
                var frSegments = fr[i].Split('|');
                Assert.AreEqual(enSegments.Length, frSegments.Length, file + ": FileTypeFilter segment count differs: " + fr[i]);
                for (var s = 1; s < enSegments.Length; s += 2)
                    Assert.AreEqual(enSegments[s], frSegments[s], file + ": FileTypeFilter pattern differs: " + fr[i]);
                for (var s = 0; s < enSegments.Length; s += 2)
                    Assert.AreNotEqual(enSegments[s], frSegments[s], file + ": FileTypeFilter label not translated: " + fr[i]);
            }
        }

        [TestMethod]
        [DataRow("rust_application.xaml")]
        [DataRow("rust_build.xaml")]
        [DataRow("rust_package.xaml")]
        [DataRow("rust_codeanalysis.xaml")]
        [DataRow("rust_debug.xaml")]
        [DataRow("rust_resources.xaml")]
        [DataRow("rust_settings.xaml")]
        public void FrenchStringsArePresentAndTranslated(string file)
        {
            var en = LoadEnglish(file).Descendants().ToArray();
            var fr = LoadFrench(file).Descendants().ToArray();
            var problems = new List<string>();

            for (var i = 0; i < en.Length && i < fr.Length; i++)
            {
                foreach (var a in en[i].Attributes())
                {
                    var isDisplayName = a.Name.LocalName == "DisplayName";
                    var isDescription = a.Name.LocalName == "Description";
                    var isMessage = a.Name.LocalName == "Value" && (PairName(en[i]) is string p
                        && (p == "EvaluatedValueFailedValidationMessage" || p == "TypeDescriptorText"));
                    if (!isDisplayName && !isDescription && !isMessage)
                        continue;

                    var frValue = (string?)fr[i].Attribute(a.Name);
                    var where = file + " <" + en[i].Name.LocalName + " " + (string?)en[i].Attribute("Name") + "> " + a.Name.LocalName;
                    if (string.IsNullOrWhiteSpace(frValue))
                    {
                        problems.Add(where + ": missing or empty in French.");
                        continue;
                    }

                    if (string.Equals(a.Value, frValue, StringComparison.Ordinal) && !IsAllowedIdentical(a.Value))
                        problems.Add(where + ": not translated ('" + a.Value + "').");
                }
            }

            Assert.AreEqual(0, problems.Count, string.Join(Environment.NewLine, problems));
        }

        [TestMethod]
        [DataRow("rust_application.xaml")]
        [DataRow("rust_build.xaml")]
        [DataRow("rust_package.xaml")]
        [DataRow("rust_codeanalysis.xaml")]
        [DataRow("rust_debug.xaml")]
        [DataRow("rust_resources.xaml")]
        [DataRow("rust_settings.xaml")]
        public void EveryPropertyCategoryIsDeclaredByTheRule(string file)
        {
            foreach (var doc in new[] { LoadEnglish(file), LoadFrench(file) })
            {
                var root = doc.Root!;
                var declared = new HashSet<string>(
                    root.Elements().Where(e => e.Name.LocalName == "Rule.Categories")
                        .SelectMany(e => e.Elements())
                        .Select(c => (string?)c.Attribute("Name") ?? string.Empty),
                    StringComparer.Ordinal);
                Assert.IsTrue(declared.Count > 0, file + ": no categories declared.");

                foreach (var property in root.Elements().Where(e => e.Attribute("Category") != null))
                {
                    var category = (string)property.Attribute("Category")!;
                    Assert.IsTrue(declared.Contains(category),
                        file + ": property " + (string?)property.Attribute("Name") + " references undeclared category '" + category + "'.");
                }
            }
        }

        private static bool IsAllowedIdentical(string value) =>
            IdenticalDisplayNames.Contains(value) || IdenticalDisplayNamePattern.IsMatch(value);

        private static List<string> FileTypeFilters(XDocument doc) =>
            doc.Descendants()
                .Where(e => PairName(e) == "FileTypeFilter")
                .Select(e => (string?)e.Attribute("Value") ?? string.Empty)
                .ToList();
    }
}
