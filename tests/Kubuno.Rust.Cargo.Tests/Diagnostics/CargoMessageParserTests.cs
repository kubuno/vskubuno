using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Kubuno.Cargo.Diagnostics;
using Kubuno.Cargo.Metadata;
using Kubuno.Cargo.Processes;
using Kubuno.Cargo.Tests.Fakes;
using Xunit;

namespace Kubuno.Cargo.Tests.Diagnostics
{
    /// <summary>
    /// Parses real <c>cargo build --message-format=json-diagnostic-rendered-ansi</c> output,
    /// captured for a handful of deliberately chosen cases:
    /// <list type="bullet">
    /// <item>Fixtures/Build/warning-build-clean.jsonl / -fresh.jsonl: an `unused_variables`
    /// warning with note+help children, a compiler-artifact (once not fresh, once fresh) and a
    /// successful build-finished.</item>
    /// <item>Fixtures/Build/error-build.jsonl: an E0382 "borrow of moved value" error with a
    /// primary span, two *secondary* spans that must never be picked as the diagnostic's own
    /// location, a help child, a spanless failure-note, and a failed build-finished.</item>
    /// <item>Fixtures/Build/workspace-nested-error.jsonl: the same shape, but for a package
    /// nested inside a workspace, to prove span paths resolve relative to the *workspace* root,
    /// not the package's own directory.</item>
    /// </list>
    /// </summary>
    public class CargoMessageParserTests
    {
        private const string ArbitraryWorkspaceRoot = @"C:\fixture-root";

        private static CargoBuildEvent[] ParseFixture(string fileName, string workspaceRoot) =>
            CargoMessageParser
                .ParseLines(TestFixtures.ReadAllLines("Build", fileName), workspaceRoot)
                .ToArray();

        [Fact]
        public void Warning_diagnostic_has_primary_span_location_code_and_children()
        {
            CargoBuildEvent[] events = ParseFixture("warning-build-clean.jsonl", ArbitraryWorkspaceRoot);

            var diagnosticEvent = Assert.IsType<CargoDiagnosticEvent>(events[0]);
            CargoDiagnostic diagnostic = diagnosticEvent.Diagnostic;

            Assert.Equal(CargoDiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.Equal("unused_variables", diagnostic.Code);
            Assert.Equal("unused variable: `unused`", diagnostic.Message);
            Assert.Equal(Path.Combine(ArbitraryWorkspaceRoot, "src", "main.rs"), diagnostic.FilePath);
            Assert.Equal(2, diagnostic.Line);
            Assert.Equal(9, diagnostic.Column);
            Assert.Equal("fixture-warning", diagnostic.TargetName);

            Assert.Equal(2, diagnostic.Children.Count);
            Assert.Contains(diagnostic.Children, c => c.Severity == CargoDiagnosticSeverity.Note);
            Assert.Contains(diagnostic.Children, c =>
                c.Severity == CargoDiagnosticSeverity.Help &&
                c.Message == "if this is intentional, prefix it with an underscore");
        }

        [Fact]
        public void Rendered_text_has_ANSI_escape_codes_stripped()
        {
            CargoBuildEvent[] events = ParseFixture("warning-build-clean.jsonl", ArbitraryWorkspaceRoot);
            var diagnostic = ((CargoDiagnosticEvent)events[0]).Diagnostic;

            Assert.NotNull(diagnostic.RenderedText);
            Assert.DoesNotContain('\u001B', diagnostic.RenderedText);
            Assert.Contains("unused variable: `unused`", diagnostic.RenderedText);
        }

        [Fact]
        public void Compiler_artifact_reports_fresh_false_on_a_clean_build_and_true_on_a_no_op_rebuild()
        {
            CargoArtifact clean = Assert.IsType<CargoArtifactEvent>(
                ParseFixture("warning-build-clean.jsonl", ArbitraryWorkspaceRoot)[1]).Artifact;
            CargoArtifact fresh = Assert.IsType<CargoArtifactEvent>(
                ParseFixture("warning-build-fresh.jsonl", ArbitraryWorkspaceRoot)[1]).Artifact;

            Assert.False(clean.Fresh);
            Assert.True(fresh.Fresh);

            Assert.Equal("fixture-warning", clean.Target.Name);
            Assert.Contains(CargoTargetKind.Bin, clean.Target.Kind);
            Assert.NotNull(clean.Executable);
            Assert.EndsWith("fixture-warning.exe", clean.Executable);
            Assert.NotEmpty(clean.Filenames);
        }

        [Fact]
        public void Build_finished_event_carries_success_flag()
        {
            CargoBuildEvent[] events = ParseFixture("warning-build-clean.jsonl", ArbitraryWorkspaceRoot);
            var finished = Assert.IsType<CargoBuildFinishedEvent>(events[2]);
            Assert.True(finished.Success);
        }

        [Fact]
        public void Error_diagnostic_uses_the_primary_span_and_skips_secondary_spans_for_its_own_location()
        {
            CargoBuildEvent[] events = ParseFixture("error-build.jsonl", ArbitraryWorkspaceRoot);
            CargoDiagnostic diagnostic = Assert.IsType<CargoDiagnosticEvent>(events[0]).Diagnostic;

            Assert.Equal(CargoDiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal("E0382", diagnostic.Code);
            Assert.Equal("borrow of moved value: `s`", diagnostic.Message);

            // The message has THREE spans (two of them secondary: "value moved here" on line 3,
            // and "move occurs because..." on line 2). Only the primary one, on line 4, must be
            // used as the diagnostic's own file/line/column.
            Assert.Equal(Path.Combine(ArbitraryWorkspaceRoot, "src", "main.rs"), diagnostic.FilePath);
            Assert.Equal(4, diagnostic.Line);
            Assert.Equal(20, diagnostic.Column);

            CargoDiagnosticNote help = Assert.Single(diagnostic.Children);
            Assert.Equal(CargoDiagnosticSeverity.Help, help.Severity);
            Assert.Equal("consider cloning the value if the performance cost is acceptable", help.Message);
        }

        [Fact]
        public void Failure_note_has_no_span_and_therefore_no_location()
        {
            CargoBuildEvent[] events = ParseFixture("error-build.jsonl", ArbitraryWorkspaceRoot);
            CargoDiagnostic diagnostic = Assert.IsType<CargoDiagnosticEvent>(events[1]).Diagnostic;

            Assert.Equal(CargoDiagnosticSeverity.FailureNote, diagnostic.Severity);
            Assert.Null(diagnostic.Code);
            Assert.Null(diagnostic.FilePath);
            Assert.Null(diagnostic.Line);
            Assert.Null(diagnostic.Column);
            Assert.Contains("rustc --explain E0382", diagnostic.Message);
        }

        [Fact]
        public void Failed_build_reports_build_finished_success_false()
        {
            CargoBuildEvent[] events = ParseFixture("error-build.jsonl", ArbitraryWorkspaceRoot);
            var finished = Assert.IsType<CargoBuildFinishedEvent>(events[2]);
            Assert.False(finished.Success);
        }

        [Fact]
        public async Task Span_paths_in_a_workspace_are_resolved_relative_to_the_workspace_root_not_the_package_directory()
        {
            // The "app" package lives in <workspace_root>\app, but Cargo reports its span
            // file_name as "app\src\main.rs" — relative to the workspace root, not to the
            // package's own manifest directory. Use the real workspace root captured alongside
            // this build (same fixtures-workspace) to prove the resolved path matches.
            var metadataRunner = new FakeProcessRunner(
                new ProcessRunResult(0, TestFixtures.ReadAllLines("Metadata", "workspace-metadata.json"), System.Array.Empty<string>()));
            CargoMetadata metadata = await new CargoMetadataReader(metadataRunner).ReadAsync(workingDirectory: @"C:\irrelevant");

            CargoBuildEvent[] events = ParseFixture("workspace-nested-error.jsonl", metadata.WorkspaceRoot);

            var artifact = Assert.IsType<CargoArtifactEvent>(events[0]).Artifact;
            Assert.Equal("core_lib", artifact.Target.Name);
            Assert.False(artifact.Fresh);

            CargoDiagnostic diagnostic = Assert.IsType<CargoDiagnosticEvent>(events[1]).Diagnostic;
            Assert.Equal("E0308", diagnostic.Code);
            string expectedPath = Path.GetFullPath(Path.Combine(metadata.WorkspaceRoot, "app", "src", "main.rs"));
            Assert.Equal(expectedPath, diagnostic.FilePath);
            Assert.Equal(2, diagnostic.Line);
            Assert.Equal(18, diagnostic.Column);

            var finished = Assert.IsType<CargoBuildFinishedEvent>(events[3]);
            Assert.False(finished.Success);
        }

        [Fact]
        public void Blank_line_is_reported_as_unrecognized_without_throwing()
        {
            CargoBuildEvent evt = CargoMessageParser.Parse(string.Empty, ArbitraryWorkspaceRoot);
            var unrecognized = Assert.IsType<CargoUnrecognizedEvent>(evt);
            Assert.Null(unrecognized.Reason);
        }

        [Fact]
        public void Non_JSON_line_is_reported_as_unrecognized_without_throwing()
        {
            CargoBuildEvent evt = CargoMessageParser.Parse("warning: unstable feature enabled", ArbitraryWorkspaceRoot);
            var unrecognized = Assert.IsType<CargoUnrecognizedEvent>(evt);
            Assert.Null(unrecognized.Reason);
            Assert.Equal("warning: unstable feature enabled", unrecognized.RawLine);
        }
    }
}
