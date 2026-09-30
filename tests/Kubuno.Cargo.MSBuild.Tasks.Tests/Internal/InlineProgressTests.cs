using System.Collections.Generic;
using Kubuno.Cargo.MSBuild.Tasks.Internal;

namespace Kubuno.Cargo.MSBuild.Tasks.Tests.Internal
{
    /// <summary>
    /// cargo's output must be handled before the task reads what it collected: System.Progress posts reports to the thread
    /// pool (no synchronization context in MSBuild), which lost the last executables of a workspace build.
    /// </summary>
    public class InlineProgressTests
    {
        [Fact]
        public void A_report_is_handled_before_Report_returns()
        {
            var seen = new List<string>();
            System.IProgress<string> progress = new InlineProgress<string>(seen.Add);

            progress.Report("a");
            progress.Report("b");

            Assert.Equal(new[] { "a", "b" }, seen);
        }
    }
}
