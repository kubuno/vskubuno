namespace Kubuno.Cargo.MSBuild.Tasks.Tests.Fakes
{
    /// <summary>A do-nothing <see cref="Microsoft.Build.Utilities.Task"/>, just to obtain a real, wired-up <c>Log</c> (<see cref="Microsoft.Build.Utilities.TaskLoggingHelper"/>) for tests.</summary>
    internal sealed class NoOpTask : Microsoft.Build.Utilities.Task
    {
        public override bool Execute() => true;
    }
}
