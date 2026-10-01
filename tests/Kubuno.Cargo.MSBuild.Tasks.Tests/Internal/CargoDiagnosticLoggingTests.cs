using System.Linq;
using Kubuno.Rust.Cargo.Diagnostics;
using Kubuno.Cargo.MSBuild.Tasks.Internal;
using Kubuno.Cargo.MSBuild.Tasks.Tests.Fakes;
using Microsoft.Build.Framework;

namespace Kubuno.Cargo.MSBuild.Tasks.Tests.Internal
{
    /// <summary>
    /// Pins <see cref="CargoDiagnosticLogging"/>'s mapping from a parsed <see cref="CargoBuildEvent"/>
    /// to MSBuild's own logging calls — the piece that turns a cargo diagnostic into a clickable
    /// Visual Studio Error List entry. Uses the same real, captured
    /// <c>cargo build --message-format=json-diagnostic-rendered-ansi</c> fixtures
    /// <c>Kubuno.Rust.Cargo.Tests</c> parses, so a regression in either the parser or this mapping
    /// would be caught the same way.
    /// </summary>
    public class CargoDiagnosticLoggingTests
    {
        private const string WorkspaceRoot = @"C:\fixture-root";

        private static (RecordingBuildEngine Engine, Microsoft.Build.Utilities.TaskLoggingHelper Log) NewLogger()
        {
            var engine = new RecordingBuildEngine();
            var task = new NoOpTask { BuildEngine = engine };
            return (engine, task.Log);
        }

        [Fact]
        public void An_error_diagnostic_becomes_one_LogError_with_file_line_column_and_code()
        {
            var (engine, log) = NewLogger();
            CargoBuildEvent[] events = CargoMessageParser
                .ParseLines(TestFixtures.ReadAllLines("Build", "error-build.jsonl"), WorkspaceRoot)
                .ToArray();
            var diagnosticEvent = events.OfType<CargoDiagnosticEvent>().Single(e => e.Diagnostic.Severity == CargoDiagnosticSeverity.Error);

            CargoDiagnosticLogging.Log(log, diagnosticEvent);

            BuildErrorEventArgs error = Assert.Single(engine.Errors);
            Assert.Equal("E0382", error.Code);
            Assert.Equal(diagnosticEvent.Diagnostic.FilePath, error.File);
            Assert.Equal(diagnosticEvent.Diagnostic.Line, error.LineNumber);
            Assert.Equal(diagnosticEvent.Diagnostic.Column, error.ColumnNumber);
            Assert.Equal("borrow of moved value: `s`", error.Message);
            Assert.Empty(engine.Warnings);
        }

        [Fact]
        public void An_error_diagnostic_also_logs_its_full_rendered_text_as_a_plain_message()
        {
            var (engine, log) = NewLogger();
            var diagnosticEvent = CargoMessageParser
                .ParseLines(TestFixtures.ReadAllLines("Build", "error-build.jsonl"), WorkspaceRoot)
                .OfType<CargoDiagnosticEvent>()
                .Single(e => e.Diagnostic.Severity == CargoDiagnosticSeverity.Error);

            CargoDiagnosticLogging.Log(log, diagnosticEvent);

            Assert.Contains(engine.Messages, m => m.Message != null && m.Message.Contains("borrow of moved value"));
        }

        [Fact]
        public void A_warning_diagnostic_becomes_one_LogWarning_not_an_error()
        {
            var (engine, log) = NewLogger();
            var diagnosticEvent = CargoMessageParser
                .ParseLines(TestFixtures.ReadAllLines("Build", "warning-build-clean.jsonl"), WorkspaceRoot)
                .OfType<CargoDiagnosticEvent>()
                .Single(e => e.Diagnostic.Severity == CargoDiagnosticSeverity.Warning);

            CargoDiagnosticLogging.Log(log, diagnosticEvent);

            BuildWarningEventArgs warning = Assert.Single(engine.Warnings);
            Assert.Equal(diagnosticEvent.Diagnostic.FilePath, warning.File);
            Assert.Equal(diagnosticEvent.Diagnostic.Line, warning.LineNumber);
            Assert.Equal(diagnosticEvent.Diagnostic.Column, warning.ColumnNumber);
            Assert.Empty(engine.Errors);
        }

        [Fact]
        public void A_diagnostic_with_no_primary_span_still_logs_an_error_at_line_zero()
        {
            var (engine, log) = NewLogger();
            var diagnostic = new CargoDiagnostic
            {
                Severity = CargoDiagnosticSeverity.Error,
                Message = "aborting due to previous error",
                Code = null,
                FilePath = null,
                Line = null,
                Column = null,
            };

            CargoDiagnosticLogging.Log(log, new CargoDiagnosticEvent(diagnostic));

            BuildErrorEventArgs error = Assert.Single(engine.Errors);
            // TaskLoggingHelper.LogError falls back to the task's own project file (here the
            // fake engine's ProjectFileOfTaskNode) when passed a null file — never a genuinely
            // empty/missing file, which is the correct MSBuild behavior for a fileless diagnostic.
            Assert.Equal("test.rsproj", error.File);
            Assert.Equal(0, error.LineNumber);
            Assert.Equal(0, error.ColumnNumber);
        }

        [Fact]
        public void A_note_or_help_diagnostic_logs_only_a_message_never_an_error_or_warning()
        {
            var (engine, log) = NewLogger();
            var note = new CargoDiagnostic { Severity = CargoDiagnosticSeverity.Note, Message = "some note" };
            var help = new CargoDiagnostic { Severity = CargoDiagnosticSeverity.Help, Message = "some help" };

            CargoDiagnosticLogging.Log(log, new CargoDiagnosticEvent(note));
            CargoDiagnosticLogging.Log(log, new CargoDiagnosticEvent(help));

            Assert.Empty(engine.Errors);
            Assert.Empty(engine.Warnings);
            Assert.Equal(2, engine.Messages.Count);
        }

        [Fact]
        public void A_build_finished_event_logs_a_message_and_never_an_error_even_on_failure()
        {
            var (engine, log) = NewLogger();

            CargoDiagnosticLogging.Log(log, new CargoBuildFinishedEvent(success: false));

            Assert.Empty(engine.Errors);
            Assert.Single(engine.Messages);
        }

        [Fact]
        public void An_unrecognized_event_with_a_raw_line_logs_it_as_a_low_importance_message()
        {
            var (engine, log) = NewLogger();

            CargoDiagnosticLogging.Log(log, new CargoUnrecognizedEvent(reason: "build-script-executed", rawLine: "{\"reason\":\"build-script-executed\"}"));

            BuildMessageEventArgs message = Assert.Single(engine.Messages);
            Assert.Equal(MessageImportance.Low, message.Importance);
        }

        /// <summary>Found by the shell's F5: a build script's event crashed the whole MSBuild process.</summary>
        [Fact]
        public void A_build_script_event_logs_a_low_importance_message()
        {
            var (engine, log) = NewLogger();

            CargoDiagnosticLogging.Log(log, new CargoBuildScriptEvent("ring 0.17.0", new[] { @"native=C:\out" }));

            BuildMessageEventArgs message = Assert.Single(engine.Messages);
            Assert.Equal(MessageImportance.Low, message.Importance);
            Assert.Empty(engine.Errors);
        }
    }
}
