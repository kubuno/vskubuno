using System.Collections.Generic;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace Kubuno.Rust.TestAdapter.Tests.Fakes
{
    /// <summary>A recording <see cref="IFrameworkHandle"/>: never talks to a real Visual Studio/vstest host, just remembers what <see cref="Execution.KubunoTestExecutor"/> reported.</summary>
    internal sealed class FakeFrameworkHandle : IFrameworkHandle
    {
        public List<TestCase> Started { get; } = new List<TestCase>();

        public List<(TestCase TestCase, TestOutcome Outcome)> Ended { get; } = new List<(TestCase, TestOutcome)>();

        public List<TestResult> Results { get; } = new List<TestResult>();

        public List<(TestMessageLevel Level, string Message)> Messages { get; } = new List<(TestMessageLevel, string)>();

        public bool EnableShutdownAfterTestRun { get; set; }

        public void RecordStart(TestCase testCase) => Started.Add(testCase);

        public void RecordEnd(TestCase testCase, TestOutcome outcome) => Ended.Add((testCase, outcome));

        public void RecordResult(TestResult testResult) => Results.Add(testResult);

        public void RecordAttachments(IList<AttachmentSet> attachmentSets)
        {
        }

        public void SendMessage(TestMessageLevel testMessageLevel, string message) => Messages.Add((testMessageLevel, message));

        public int LaunchProcessWithDebuggerAttached(
            string filePath,
            string? workingDirectory,
            string? arguments,
            IDictionary<string, string?>? environmentVariables)
        {
            LaunchedFilePath = filePath;
            LaunchedWorkingDirectory = workingDirectory;
            LaunchedArguments = arguments;
            LaunchedEnvironmentVariables = environmentVariables;
            return LaunchedProcessId;
        }

        public string? LaunchedFilePath { get; private set; }

        public string? LaunchedWorkingDirectory { get; private set; }

        public string? LaunchedArguments { get; private set; }

        public IDictionary<string, string?>? LaunchedEnvironmentVariables { get; private set; }

        /// <summary>The process id <see cref="LaunchProcessWithDebuggerAttached"/> returns - defaults to the current process so a test can wait on a real, already-exited-by-the-time-it-checks process without spawning anything.</summary>
        public int LaunchedProcessId { get; set; } = System.Diagnostics.Process.GetCurrentProcess().Id;
    }
}
