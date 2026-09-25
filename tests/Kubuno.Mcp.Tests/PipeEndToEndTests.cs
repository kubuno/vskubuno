using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Mcp.Bridge;
using Kubuno.Mcp.Bridge.Contracts;
using Kubuno.Mcp.Bridge.PipeProtocol;
using Kubuno.Mcp.Tests.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Mcp.Tests
{
    /// <summary>
    /// Runs <see cref="VsMcpBridgeHost"/> on a real local named pipe (uniquely named per test, not
    /// the production "%LOCALAPPDATA%\Kubuno\vs-mcp\pid.json" pipe) and talks to it with
    /// <see cref="VsMcpBridgeClient"/> - the same two classes Kubuno.Mcp and the VSIX bridge use in
    /// production, wired to a <see cref="FakeVsContextProvider"/> instead of DTE. This is what
    /// proves the framing (PipeMessageFramingTests) and the dispatcher (BridgeDispatcherTests)
    /// actually work together over a real OS pipe, end to end.
    /// </summary>
    [TestClass]
    public sealed class PipeEndToEndTests
    {
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
        private static int _fakePidCounter;

        /// <summary>
        /// A negative, per-test-unique fake pid: real Windows process ids are never negative, so
        /// this can never collide with a genuine devenv.exe discovery file, and it makes the
        /// "%LOCALAPPDATA%\Kubuno\vs-mcp\&lt;pid&gt;.json" file this test's VsMcpBridgeHost writes
        /// (and deletes on Dispose) obviously test-only if a run is interrupted before cleanup.
        /// </summary>
        private static int NextFakePid() => -System.Threading.Interlocked.Increment(ref _fakePidCounter);

        [TestMethod]
        public async Task ClientReceivesHostsResponse_ForAKnownMethod()
        {
            using var host = new VsMcpBridgeHost(new FakeVsContextProvider(), pid: NextFakePid(), pipeName: UniquePipeName());
            host.Start();

            BridgeResponse response = await VsMcpBridgeClient.SendRequestAsync(
                host.PipeName,
                new BridgeRequest { Method = BridgeMethods.Selection },
                ConnectTimeout,
                CancellationToken.None);

            Assert.IsTrue(response.Success);
            SelectionInfo? info = response.Result!.Value.Deserialize<SelectionInfo>();
            Assert.AreEqual("println!(\"hi\");", info!.Text);
        }

        [TestMethod]
        public async Task ClientReceivesErrorResponse_ForAnUnknownMethod()
        {
            using var host = new VsMcpBridgeHost(new FakeVsContextProvider(), pid: NextFakePid(), pipeName: UniquePipeName());
            host.Start();

            BridgeResponse response = await VsMcpBridgeClient.SendRequestAsync(
                host.PipeName,
                new BridgeRequest { Method = "vs_not_a_real_tool" },
                ConnectTimeout,
                CancellationToken.None);

            Assert.IsFalse(response.Success);
            Assert.AreEqual("unknown_method", response.Error!.Code);
        }

        [TestMethod]
        public async Task HostAcceptsMultipleSequentialConnections()
        {
            using var host = new VsMcpBridgeHost(new FakeVsContextProvider(), pid: NextFakePid(), pipeName: UniquePipeName());
            host.Start();

            for (int i = 0; i < 3; i++)
            {
                BridgeResponse response = await VsMcpBridgeClient.SendRequestAsync(
                    host.PipeName,
                    new BridgeRequest { Method = BridgeMethods.OpenDocuments },
                    ConnectTimeout,
                    CancellationToken.None);

                Assert.IsTrue(response.Success, $"connection #{i} failed: {response.Error?.Message}");
            }
        }

        [TestMethod]
        public async Task ClientThrowsBridgeUnavailable_WhenNoHostIsListening()
        {
            await Assert.ThrowsExactlyAsync<BridgeUnavailableException>(() =>
                VsMcpBridgeClient.SendRequestAsync(
                    UniquePipeName(), // Nobody ever calls Start() for this name.
                    new BridgeRequest { Method = BridgeMethods.Selection },
                    TimeSpan.FromMilliseconds(300),
                    CancellationToken.None));
        }

        private static string UniquePipeName() => $"KubunoVsMcpTests.{Guid.NewGuid():N}";
    }
}
