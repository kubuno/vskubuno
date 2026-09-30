using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;

namespace Kubuno.Rust.TestAdapter.Tests.Fakes
{
    internal sealed class FakeRunContext : IRunContext
    {
        public bool KeepAlive => false;

        public bool InIsolation => false;

        public bool IsDataCollectionEnabled => false;

        public bool IsBeingDebugged { get; set; }

        public string? TestRunDirectory => null;

        public string? SolutionDirectory => null;

        public IRunSettings? RunSettings => null;

        public ITestCaseFilterExpression? GetTestCaseFilter(IEnumerable<string>? supportedProperties, Func<string, TestProperty?> propertyProvider) => null;
    }
}
