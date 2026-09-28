using System.Collections;
using System.Collections.Generic;
using Microsoft.Build.Framework;

namespace Kubuno.Cargo.MSBuild.Tasks.Tests.Fakes
{
    /// <summary>
    /// A minimal <see cref="IBuildEngine"/> that just records every logged event, so a task's
    /// <see cref="Microsoft.Build.Utilities.TaskLoggingHelper"/> (<c>Log</c>) can be exercised
    /// without a real MSBuild engine or a live cargo process.
    /// </summary>
    internal sealed class RecordingBuildEngine : IBuildEngine
    {
        public List<BuildErrorEventArgs> Errors { get; } = new List<BuildErrorEventArgs>();

        public List<BuildWarningEventArgs> Warnings { get; } = new List<BuildWarningEventArgs>();

        public List<BuildMessageEventArgs> Messages { get; } = new List<BuildMessageEventArgs>();

        public bool ContinueOnError => false;

        public int LineNumberOfTaskNode => 0;

        public int ColumnNumberOfTaskNode => 0;

        public string ProjectFileOfTaskNode => "test.rsproj";

        public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) => true;

        public void LogCustomEvent(CustomBuildEventArgs e)
        {
        }

        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);

        public void LogMessageEvent(BuildMessageEventArgs e) => Messages.Add(e);

        public void LogWarningEvent(BuildWarningEventArgs e) => Warnings.Add(e);
    }
}
