using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Desktop.Logic.Data;

namespace Kubuno.Desktop.Tests.Data
{
    /// <summary>
    /// An in-memory kubuno-data-tool: each started "process" is a <see cref="FakeConnection"/> whose requests go to
    /// <see cref="Handler"/> (which may answer now, later, or never), so framing, ids, concurrency, cancel and crashes can
    /// be tested without the real exe.
    /// </summary>
    internal sealed class FakeLauncher : IDataToolLauncher
    {
        public List<FakeConnection> Started { get; } = new List<FakeConnection>();

        /// <summary>Called for every request line (on the writer's thread).</summary>
        public Action<FakeConnection, JsonObject>? Handler { get; set; }

        public bool FailToStart { get; set; }

        public IDataToolConnection Start()
        {
            if (FailToStart)
            {
                throw new DataToolException(DataToolErrorKinds.Unavailable, "not built");
            }

            var connection = new FakeConnection(this);
            lock (Started)
            {
                Started.Add(connection);
            }

            return connection;
        }

        public FakeConnection Last
        {
            get
            {
                lock (Started)
                {
                    return Started[Started.Count - 1];
                }
            }
        }
    }

    internal sealed class FakeConnection : IDataToolConnection
    {
        private readonly FakeLauncher _owner;
        private readonly BlockingCollection<string?> _output = new BlockingCollection<string?>();
        private volatile bool _closed;

        public FakeConnection(FakeLauncher owner)
        {
            _owner = owner;
        }

        public ConcurrentQueue<string> Written { get; } = new ConcurrentQueue<string>();

        public bool ShutDown { get; private set; }

        public Task WriteLineAsync(string line)
        {
            if (_closed)
            {
                throw new System.IO.IOException("broken pipe");
            }

            Written.Enqueue(line);
            _owner.Handler?.Invoke(this, (JsonObject)JsonNode.Parse(line)!);
            return Task.CompletedTask;
        }

        public Task<string?> ReadLineAsync() => Task.Run(() =>
        {
            try
            {
                return _output.Take();
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        });

        public void Respond(long id, JsonNode result) => Push(new JsonObject { ["id"] = id, ["result"] = result }.ToJsonString());

        public void RespondError(long id, string kind, string message) =>
            Push(new JsonObject { ["id"] = id, ["error"] = new JsonObject { ["kind"] = kind, ["message"] = message } }.ToJsonString());

        public void Push(string line)
        {
            if (!_output.IsAddingCompleted)
            {
                _output.Add(line);
            }
        }

        /// <summary>Simulates a crash: stdout reaches EOF, writes fail.</summary>
        public void Crash()
        {
            _closed = true;
            _output.CompleteAdding();
        }

        public void Shutdown()
        {
            ShutDown = true;
            Crash();
        }

        public void Dispose()
        {
        }

        public static long IdOf(JsonObject request) => (long)request["id"]!;

        public static string MethodOf(JsonObject request) => (string)request["method"]!;
    }

    /// <summary>A scripted <see cref="IDataToolClient"/> for service-level tests: returns canned results and records the requests.</summary>
    internal sealed class ScriptedClient : IDataToolClient
    {
        private readonly Func<string, JsonObject?, JsonNode> _answer;

        public ScriptedClient(Func<string, JsonObject?, JsonNode> answer)
        {
            _answer = answer;
        }

        public List<(string Method, JsonObject? Parameters)> Requests { get; } = new List<(string, JsonObject?)>();

        public Task<JsonElement> SendAsync(string method, JsonObject? parameters, CancellationToken cancellationToken)
        {
            Requests.Add((method, parameters));
            using var document = JsonDocument.Parse(_answer(method, parameters).ToJsonString());
            return Task.FromResult(document.RootElement.Clone());
        }
    }
}
