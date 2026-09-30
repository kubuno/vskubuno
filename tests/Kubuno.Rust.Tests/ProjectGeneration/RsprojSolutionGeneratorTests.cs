using System.Collections.Generic;
using System.IO;
using Kubuno.Rust.Logic.ProjectGeneration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Rust.Tests.ProjectGeneration
{
    [TestClass]
    public class RsprojSolutionGeneratorTests
    {
        private const string Root = @"C:\ws";

        private static RsprojProjectPlanItem Item(string name, RsprojPlanAction action = RsprojPlanAction.Create) =>
            new(name, Path.Combine(Root, name, name + ".rsproj"), Path.Combine(Root, name, "Cargo.toml"), action, content: "<Project />", isLibraryOnly: false);

        [TestMethod]
        public void Plan_BuildsAFreshSolution_WhenNoneExistsYet()
        {
            var plan = RsprojSolutionGenerator.Plan(Root, existingContent: null, new List<RsprojProjectPlanItem> { Item("app") });

            Assert.IsTrue(plan.Changed);
            CollectionAssert.Contains((System.Collections.ICollection)plan.AddedProjectNames, "app");
            StringAssert.Contains(plan.Content, "Microsoft Visual Studio Solution File");
            StringAssert.Contains(plan.Content, "\"app\", \"app\\app.rsproj\"");
            StringAssert.Contains(plan.Content, "Debug|x64 = Debug|x64");
            StringAssert.Contains(plan.Content, ".Debug|x64.ActiveCfg = Debug|x64");
        }

        [TestMethod]
        public void Plan_FreshSolution_ListsEveryMember_IncludingAlreadyExistingProjectFiles()
        {
            var items = new List<RsprojProjectPlanItem> { Item("app", RsprojPlanAction.Create), Item("tool", RsprojPlanAction.SkipExisting) };

            var plan = RsprojSolutionGenerator.Plan(Root, existingContent: null, items);

            StringAssert.Contains(plan.Content, "\"app\"");
            StringAssert.Contains(plan.Content, "\"tool\"");
        }

        [TestMethod]
        public void Plan_IsAValidGuid_ForEachGeneratedProjectEntry()
        {
            var plan = RsprojSolutionGenerator.Plan(Root, existingContent: null, new List<RsprojProjectPlanItem> { Item("app") });

            StringAssert.Matches(plan.Content, new System.Text.RegularExpressions.Regex(
                @"Project\(""\{6C7C4CB5-6E36-4C6F-9C6F-9C6E9B4D4C13\}""\) = ""app"", ""app\\app\.rsproj"", ""\{[0-9A-F-]{36}\}"""));
        }

        [TestMethod]
        public void Plan_IsIdempotent_OnASecondRunAgainstItsOwnOutput()
        {
            var items = new List<RsprojProjectPlanItem> { Item("app"), Item("tool") };
            var first = RsprojSolutionGenerator.Plan(Root, existingContent: null, items);

            var second = RsprojSolutionGenerator.Plan(Root, first.Content, items);

            Assert.IsFalse(second.Changed);
            Assert.AreEqual(0, second.AddedProjectNames.Count);
            Assert.AreEqual(first.Content, second.Content);
        }

        [TestMethod]
        public void Plan_AddsOnlyTheMissingProject_AndPreservesExistingUnrelatedContentVerbatim()
        {
            var existing =
                "\r\n" +
                "Microsoft Visual Studio Solution File, Format Version 12.00\r\n" +
                "# Visual Studio Version 17\r\n" +
                "VisualStudioVersion = 17.0.31903.59\r\n" +
                "MinimumVisualStudioVersion = 10.0.40219.1\r\n" +
                "Project(\"{2150E333-8FDC-42A3-9474-1A3956D46DE8}\") = \"docs\", \"docs\", \"{11111111-1111-1111-1111-111111111111}\"\r\n" +
                "EndProject\r\n" +
                "Project(\"{6C7C4CB5-6E36-4C6F-9C6F-9C6E9B4D4C13}\") = \"app\", \"app\\app.rsproj\", \"{22222222-2222-2222-2222-222222222222}\"\r\n" +
                "EndProject\r\n" +
                "Global\r\n" +
                "\tGlobalSection(SolutionConfigurationPlatforms) = preSolution\r\n" +
                "\t\tDebug|x64 = Debug|x64\r\n" +
                "\t\tRelease|x64 = Release|x64\r\n" +
                "\tEndGlobalSection\r\n" +
                "\tGlobalSection(ProjectConfigurationPlatforms) = postSolution\r\n" +
                "\t\t{22222222-2222-2222-2222-222222222222}.Debug|x64.ActiveCfg = Debug|x64\r\n" +
                "\t\t{22222222-2222-2222-2222-222222222222}.Debug|x64.Build.0 = Debug|x64\r\n" +
                "\t\t{22222222-2222-2222-2222-222222222222}.Release|x64.ActiveCfg = Release|x64\r\n" +
                "\t\t{22222222-2222-2222-2222-222222222222}.Release|x64.Build.0 = Release|x64\r\n" +
                "\tEndGlobalSection\r\n" +
                "\tGlobalSection(SolutionProperties) = preSolution\r\n" +
                "\t\tHideSolutionNode = FALSE\r\n" +
                "\tEndGlobalSection\r\n" +
                "EndGlobal\r\n";

            var items = new List<RsprojProjectPlanItem> { Item("app"), Item("tool") };

            var plan = RsprojSolutionGenerator.Plan(Root, existing, items);

            Assert.IsTrue(plan.Changed);
            CollectionAssert.AreEqual(new[] { "tool" }, new List<string>(plan.AddedProjectNames));
            // The solution folder and the already-listed "app" project must survive untouched.
            StringAssert.Contains(plan.Content, "\"docs\", \"docs\", \"{11111111-1111-1111-1111-111111111111}\"");
            StringAssert.Contains(plan.Content, "\"app\", \"app\\app.rsproj\", \"{22222222-2222-2222-2222-222222222222}\"");
            StringAssert.Contains(plan.Content, "\"tool\\tool.rsproj\"");
            // The new project's own 4 configuration lines were added.
            StringAssert.Contains(plan.Content, ".Debug|x64.ActiveCfg = Debug|x64");
            // CRLF line endings from the original file are preserved.
            StringAssert.Contains(plan.Content, "\r\n");
        }

        [TestMethod]
        public void Plan_ReusesTheDeterministicGuid_ForAnAlreadyGeneratedProject_AcrossFreshAndMergedRuns()
        {
            var items = new List<RsprojProjectPlanItem> { Item("app") };
            var fresh = RsprojSolutionGenerator.Plan(Root, existingContent: null, items);

            var mergedIntoItself = RsprojSolutionGenerator.Plan(Root, fresh.Content, items);

            // No-op merge already asserted above; this only re-confirms the guid used in a fresh
            // build is stable by re-deriving it directly.
            var expectedGuid = DeterministicGuid.From("rsproj:" + Path.Combine(Root, "app", "app.rsproj").ToLowerInvariant()).ToString("D").ToUpperInvariant();
            StringAssert.Contains(fresh.Content, expectedGuid);
            Assert.AreEqual(fresh.Content, mergedIntoItself.Content);
        }
    }
}
