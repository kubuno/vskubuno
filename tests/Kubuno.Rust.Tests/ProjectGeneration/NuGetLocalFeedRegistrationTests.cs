using Kubuno.Rust.Logic.ProjectGeneration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Rust.Tests.ProjectGeneration
{
    [TestClass]
    public class NuGetLocalFeedRegistrationTests
    {
        private const string SourceName = "Kubuno.Rust.Sdk (bundled)";
        private const string SourcePath = @"C:\Program Files\Kubuno\SdkFeed";

        [TestMethod]
        public void Plan_CreatesAMinimalConfig_WhenNoneExistsYet()
        {
            var (content, changed) = NuGetLocalFeedRegistration.Plan(null, SourceName, SourcePath);

            Assert.IsTrue(changed);
            StringAssert.Contains(content, "<configuration>");
            StringAssert.Contains(content, $"key=\"{SourceName}\"");
            StringAssert.Contains(content, $"value=\"{SourcePath}\"");
        }

        [TestMethod]
        public void Plan_AddsTheSource_ToAnExistingConfigWithOtherSources()
        {
            var existing =
                "<configuration>\n" +
                "  <packageSources>\n" +
                "    <add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" />\n" +
                "  </packageSources>\n" +
                "</configuration>\n";

            var (content, changed) = NuGetLocalFeedRegistration.Plan(existing, SourceName, SourcePath);

            Assert.IsTrue(changed);
            StringAssert.Contains(content, "nuget.org"); // preserved
            StringAssert.Contains(content, $"key=\"{SourceName}\"");
        }

        [TestMethod]
        public void Plan_IsIdempotent_WhenTheSameSourceIsAlreadyRegistered()
        {
            var first = NuGetLocalFeedRegistration.Plan(null, SourceName, SourcePath).Content;

            var (content, changed) = NuGetLocalFeedRegistration.Plan(first, SourceName, SourcePath);

            Assert.IsFalse(changed);
            Assert.AreEqual(first, content);
        }

        [TestMethod]
        public void Plan_RepointsTheSource_WhenItsPathChanged()
        {
            var first = NuGetLocalFeedRegistration.Plan(null, SourceName, SourcePath).Content;
            var newPath = @"D:\Kubuno\SdkFeed";

            var (content, changed) = NuGetLocalFeedRegistration.Plan(first, SourceName, newPath);

            Assert.IsTrue(changed);
            StringAssert.Contains(content, $"value=\"{newPath}\"");
            Assert.IsFalse(content.Contains(SourcePath));
        }

        [TestMethod]
        public void Plan_CreatesThePackageSourcesElement_WhenTheConfigHasNoneYet()
        {
            var existing = "<configuration>\n  <config>\n    <add key=\"globalPackagesFolder\" value=\"C:\\packages\" />\n  </config>\n</configuration>\n";

            var (content, changed) = NuGetLocalFeedRegistration.Plan(existing, SourceName, SourcePath);

            Assert.IsTrue(changed);
            StringAssert.Contains(content, "<packageSources>");
            StringAssert.Contains(content, "globalPackagesFolder"); // preserved
        }

        [TestMethod]
        public void Plan_LeavesAnUnrecognizedFile_Untouched()
        {
            var notNuGetConfig = "<root><foo /></root>";

            var (content, changed) = NuGetLocalFeedRegistration.Plan(notNuGetConfig, SourceName, SourcePath);

            Assert.IsFalse(changed);
            Assert.AreEqual(notNuGetConfig, content);
        }

        [TestMethod]
        public void Plan_MatchesAnExistingEntry_ByKeyCaseInsensitively()
        {
            var existing =
                "<configuration>\n" +
                "  <packageSources>\n" +
                $"    <add key=\"{SourceName.ToUpperInvariant()}\" value=\"{SourcePath}\" />\n" +
                "  </packageSources>\n" +
                "</configuration>\n";

            var (_, changed) = NuGetLocalFeedRegistration.Plan(existing, SourceName, SourcePath);

            Assert.IsFalse(changed);
        }
    }
}
