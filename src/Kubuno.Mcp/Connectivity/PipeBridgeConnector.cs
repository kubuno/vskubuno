using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Mcp.Bridge;
using Kubuno.Mcp.Bridge.Contracts;
using Kubuno.Mcp.Bridge.Discovery;
using Kubuno.Mcp.Bridge.PipeProtocol;

namespace Kubuno.Mcp.Connectivity
{
    /// <summary>
    /// Production <see cref="IBridgeConnector"/>: finds a live Visual Studio instance via the
    /// discovery files under <c>%LOCALAPPDATA%\Kubuno\vs-mcp\</c>
    /// (<see cref="BridgeDiscoveryFile"/>) and talks to it over a named pipe
    /// (<see cref="VsMcpBridgeClient"/>).
    /// </summary>
    public sealed class PipeBridgeConnector : IBridgeConnector
    {
        /// <summary>Set to a devenv.exe process id to disambiguate when more than one Visual Studio instance is running.</summary>
        public const string PidEnvironmentVariable = "KUBUNO_VS_PID";

        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

        public async Task<BridgeResponse> SendAsync(string method, object? parameters, CancellationToken cancellationToken)
        {
            BridgeDiscoveryInfo target = ResolveTarget();

            var request = new BridgeRequest
            {
                Method = method,
                Params = parameters is null ? (JsonElement?)null : JsonSerializer.SerializeToElement(parameters),
            };

            return await VsMcpBridgeClient.SendRequestAsync(target.PipeName, request, ConnectTimeout, cancellationToken)
                .ConfigureAwait(false);
        }

        private static BridgeDiscoveryInfo ResolveTarget()
        {
            List<BridgeDiscoveryInfo> alive = BridgeDiscoveryFile.EnumerateAll()
                .Where(IsProcessAlive)
                .ToList();

            if (alive.Count == 0)
            {
                throw new BridgeUnavailableException(
                    "Visual Studio is not running, or no running instance has the Kubuno bridge loaded. Open the " +
                    "solution/folder in Visual Studio with the Kubuno extension installed, then retry.");
            }

            string? requestedPid = Environment.GetEnvironmentVariable(PidEnvironmentVariable);
            if (!string.IsNullOrEmpty(requestedPid) && int.TryParse(requestedPid, out int pid))
            {
                BridgeDiscoveryInfo? match = alive.FirstOrDefault(i => i.Pid == pid);
                if (match is null)
                {
                    throw new BridgeUnavailableException(
                        $"{PidEnvironmentVariable}={requestedPid} does not match any running Visual Studio instance " +
                        $"with a Kubuno bridge. Running instances: {DescribeCandidates(alive)}.");
                }
                return match;
            }

            if (alive.Count > 1)
            {
                throw new BridgeUnavailableException(
                    $"Multiple Visual Studio instances have a Kubuno bridge running; set {PidEnvironmentVariable} " +
                    $"to pick one. Running instances: {DescribeCandidates(alive)}.");
            }

            return alive[0];
        }

        private static bool IsProcessAlive(BridgeDiscoveryInfo info)
        {
            try
            {
                using Process process = Process.GetProcessById(info.Pid);
                return !process.HasExited
                    && string.Equals(process.ProcessName, info.ProcessName, StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException)
            {
                // No process with that id: a stale discovery file left behind by a VS instance
                // that crashed instead of disposing its VsMcpBridgeHost. Clean it up so future
                // scans don't keep tripping over it.
                BridgeDiscoveryFile.Delete(info.Pid);
                return false;
            }
        }

        private static string DescribeCandidates(IEnumerable<BridgeDiscoveryInfo> candidates) =>
            string.Join(", ", candidates.Select(c => $"pid {c.Pid} ({c.SolutionOrFolderPath ?? "no solution/folder open"})"));
    }
}
