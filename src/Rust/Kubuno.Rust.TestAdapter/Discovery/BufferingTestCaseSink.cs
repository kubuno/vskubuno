using System.Collections.Generic;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;

namespace Kubuno.Rust.TestAdapter.Discovery
{
    /// <summary>
    /// An <see cref="ITestCaseDiscoverySink"/> that just buffers every <see cref="TestCase"/> it
    /// receives. Used by <see cref="Execution.KubunoTestExecutor"/>'s source-only
    /// <c>RunTests(IEnumerable&lt;string&gt;, ...)</c> overload to re-run discovery synchronously
    /// (see its doc comment for why that path exists), and by tests exercising
    /// <see cref="KubunoTestDiscoverer"/> directly.
    /// </summary>
    public sealed class BufferingTestCaseSink : ITestCaseDiscoverySink
    {
        private readonly List<TestCase> _testCases = new List<TestCase>();

        public IReadOnlyList<TestCase> TestCases => _testCases;

        public void SendTestCase(TestCase discoveredTest) => _testCases.Add(discoveredTest);
    }
}
