# Kubuno VS MCP server

Phase 5 of `docs/ARCHITECTURE.md`: an MCP server so Claude Code sees what the developer sees in
Visual Studio right now - active document, selection, Error List, open documents, solution/folder
root, debugger state, an output pane - without the developer having to paste any of it.

## Why a standalone process + a named-pipe bridge

Claude Code (and Claude in general) speaks MCP to **server processes** it manages itself, over
stdio (`command`/`args` in `.mcp.json`, or `claude mcp add`) or over HTTP. It does not, and
cannot, reach into an arbitrary host process's memory.

`Kubuno.VisualStudio` is a classic VSSDK extension: in-proc inside `devenv.exe`, `.NET Framework
4.8` (see `docs/ARCHITECTURE.md` "Extension model" for why that model was chosen over
`VisualStudio.Extensibility`). That process is not, and should not become, an MCP server itself:

- Claude Code launches and owns the lifecycle of the MCP servers it talks to; it cannot launch
  `devenv.exe` for that purpose, and `devenv.exe` is not something a background MCP transport
  should be piggy-backed onto (stdio would fight with VS's own console-less lifecycle; HTTP would
  mean an extension listening on a port).
- One Visual Studio instance's context should be reachable independently of whether Claude Code
  itself is running inside that same IDE window, another window, or a plain terminal.

So the shape is two processes:

```
Claude Code  <--- MCP (stdio) --->  kubuno-vs-mcp.exe  <--- named pipe --->  devenv.exe (VSIX)
              src/Kubuno.Mcp                              src/Kubuno.Mcp.Bridge, loaded in-proc
```

- **`kubuno-vs-mcp.exe`** (`src/Kubuno.Mcp/`, net8.0, standalone exe) is the actual MCP server.
  It has no VS SDK dependency at all - it only knows how to find and talk to a bridge over a
  local pipe. This is what `claude mcp add` points at.
- **The bridge** (`src/Kubuno.Mcp.Bridge/`, multi-targeted `net48;net8.0`) is a plain class
  library, not a process. Its net48 build is loaded in-proc by `Kubuno.VisualStudio` (the VSIX
  package, at package load - see "Integration" below) and hosts the pipe server
  (`VsMcpBridgeHost`) plus a DTE-based `IVsContextProvider` implementation
  (`DteVsContextProvider`). Its net8.0 build is referenced by `Kubuno.Mcp` and by the tests, and
  contains everything *except* the DTE-specific code (the wire contract, the pipe client, the
  framing, the discovery-file reader/writer) - so the two processes share the contract as source,
  never as a duplicated hand-maintained copy.

## Why the `ModelContextProtocol` SDK (not a hand-written JSON-RPC layer)

`src/Kubuno.Mcp/Kubuno.Mcp.csproj` takes a direct dependency on the official C# MCP SDK,
[`ModelContextProtocol`](https://www.nuget.org/packages/ModelContextProtocol) (published by the
Model Context Protocol project; version `2.2.0` here, targeting `net8.0`/`net9.0`/`net10.0`/
`netstandard2.0`). A hand-written implementation was considered and rejected:

- MCP is not just "JSON-RPC" - it is a specific initialization handshake, capability
  negotiation, tool/prompt/resource listing with pagination, cancellation propagated through
  `notifications/cancelled`, structured content, and a growing set of SEPs (e.g. the `x-mcp-header`
  discovery mechanism visible in the SDK's `McpClient.CallToolAsync` docs). Re-implementing and
  then keeping that current by hand is exactly the kind of "regenerate what the ecosystem already
  maintains" this repo's `CLAUDE.md` warns against for anything *other* than Kubuno's own domain
  code.
- The SDK's attribute-based tool model (`[McpServerToolType]` / `[McpServerTool]` +
  `[System.ComponentModel.Description]`) generates the JSON input schema from the C# method
  signature, keeps the schema and the implementation from drifting apart, and gives every tool a
  `ReadOnlyHint`/`IdempotentHint`/`OpenWorldHint` annotation slot - used here to declare every
  tool read-only in the protocol itself, not just in this document (see "Security" below).
- It ships both a server (`ModelContextProtocol.Server`) and a client
  (`ModelContextProtocol.Client`) surface, including `ModelContextProtocol.Protocol.StreamClientTransport`
  and `McpServerBuilderExtensions.WithStreamServerTransport` - built exactly for wiring a client
  and a server together over a pair of in-memory streams. `tests/Kubuno.Mcp.Tests/McpProtocolTests.cs`
  uses this to run the *real* client and the *real* server against each other, so the protocol
  round-trip tests exercise the actual wire format, not a mock of it.

## Transports and discovery

### Claude Code <-> `kubuno-vs-mcp.exe`

Standard MCP stdio transport (`WithStdioServerTransport()` in `src/Kubuno.Mcp/Program.cs`):
newline-delimited JSON-RPC over stdin/stdout. All logging is routed to **stderr**
(`builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace)`), because
stdout is reserved for protocol frames - anything else written there corrupts the stream.

### `kubuno-vs-mcp.exe` <-> the VSIX bridge

A private, much simpler request/response protocol over a local named pipe
(`\\.\pipe\<name>`, `System.IO.Pipes.NamedPipeServerStream`/`NamedPipeClientStream`,
`PipeOptions.Asynchronous`):

- **Framing** (`Kubuno.Mcp.Bridge.PipeProtocol.PipeMessageFraming`): one JSON document per
  message, as a 4-byte little-endian length prefix followed by that many UTF-8 bytes. Chosen over
  Windows message-mode pipes so the exact same code path also works over the in-memory streams used
  by tests (`System.IO.Pipelines.Pipe.Reader/Writer.AsStream()`), which have no concept of pipe
  message boundaries.
- **Envelope** (`Kubuno.Mcp.Bridge.Contracts.BridgeRequest`/`BridgeResponse`): `{id, method,
  params}` / `{id, success, result, error}`. `method` is one of the seven tool names below
  (`Kubuno.Mcp.Bridge.BridgeMethods`); `Kubuno.Mcp.Bridge.PipeProtocol.BridgeDispatcher` routes a
  request to the matching `IVsContextProvider` method and turns any exception into an `error`
  response instead of tearing down the connection.
- **Discovery**: when `VsMcpBridgeHost.Start()` runs (VSIX package load), it picks a pipe name
  (`KubunoVsMcp.<pid>.<random>`) and writes a discovery file to
  `%LOCALAPPDATA%\Kubuno\vs-mcp\<devenv pid>.json` (`Kubuno.Mcp.Bridge.Discovery.BridgeDiscoveryFile`):
  ```json
  {
    "pid": 12345,
    "pipeName": "KubunoVsMcp.12345.9f3a...",
    "processName": "devenv",
    "startedAtUtc": "2026-09-25T08:00:00Z",
    "visualStudioVersion": "18.0....",
    "solutionOrFolderPath": "Z:\\projects\\kubuno\\drive"
  }
  ```
  `kubuno-vs-mcp.exe`'s `PipeBridgeConnector` enumerates every file in that directory, keeps the
  ones whose `pid` is still a live `devenv.exe` process (deleting stale files it finds along the
  way), and connects to the pipe named there. With exactly one live instance this is fully
  automatic. With more than one, it fails with a clear error listing the candidates and asking for
  the `KUBUNO_VS_PID` environment variable to disambiguate (set it in `.mcp.json`'s `env` if a
  particular VS window should always be targeted). `%LOCALAPPDATA%` is already scoped to the
  signed-in user by Windows - no ACL widening needed for "current user only".
- Each tool call opens a short-lived connection (connect, one request/response, disconnect)
  rather than keeping one open across calls - simple and self-healing if the bridge restarts
  (extension reload) between calls. See "Known limits" for the obvious follow-up optimization.

## Tools

All seven are declared `ReadOnly = true, Idempotent = true, OpenWorld = false` on
`[McpServerTool]` (`src/Kubuno.Mcp/Tools/KubunoVsTools.cs`) - this is a protocol-level annotation
a client can see in `tools/list`, not just a comment. Every tool returns its bridge DTO
(`Kubuno.Mcp.Bridge.Contracts`) serialized as indented JSON text.

| Tool | Arguments | Returns |
|---|---|---|
| `vs_active_document` | `startLine?`, `endLine?` (1-based, inclusive; full text if both omitted) | path, language, unsaved state, text (or the requested range), total line count |
| `vs_selection` | - | file, empty flag, 1-based start/end line+column, selected text |
| `vs_error_list` | `maxItems?` | total count + items: severity (`Error`/`Warning`/`Message`), file, line, column, message, project (`code` is always `null` - see "Known limits") |
| `vs_open_documents` | - | every open document: path, active flag, unsaved flag, language |
| `vs_solution_or_folder` | - | kind (`Solution`/`Folder`/`None`), root path, name, detected `Cargo.toml` roots under it |
| `vs_debugger_state` | - | mode (`Design`/`Run`/`Break`), call stack (function name per frame; file/line only on the current frame, see "Known limits"), current frame's locals |
| `vs_output_pane` | `paneName?` (default `"Build"`), `maxLength?` | pane text (tail-truncated at `maxLength`, default 200,000 chars), found flag |

Two tools named in `docs/ARCHITECTURE.md` phase 5 as "later" are intentionally **not** in this
version: `kubuno_view_selection` / `kubuno_view_preview_png` depend on phases 2-4 (the XML view
framework and embedded live preview) that do not exist yet.

## Security: read-only tools only

Every tool here only *reads* VS state; none of them can create, edit, save, close, or execute
anything, and there is no "run a command"/"send keys" tool. This is deliberate, not just "not
implemented yet":

- **Claude already has its own, reviewed, file-editing tools.** The developer's workflow (see
  `docs/ARCHITECTURE.md` "Goal") is: Claude edits files with its normal tools, the developer
  reviews/adjusts in Visual Studio, git is the shared history. A second, MCP-shaped way to mutate
  files or the IDE would create two write paths to reconcile (undo stacks, unsaved-buffer
  conflicts, VS's own dirty-tracking) for no benefit - `vs_active_document` already tells Claude
  what the developer's unsaved edits look like without needing to *make* any.
- **Smaller, auditable attack surface.** The pipe has no network exposure and is scoped to the
  current user (see "Transports and discovery"), but it is still a local IPC surface a
  misbehaving or compromised MCP client could call into. A read-only surface can, at worst, leak
  information already visible on the developer's own screen back to Claude - it cannot be used to
  make Visual Studio do something the developer didn't ask for (run a build, delete a file, attach
  a debugger to another process, etc.).
- **`/internal/*`-grade trust is not available here.** Kubuno's own backend rule (`CLAUDE.md`
  §7) is that internal routes require `X-Internal-Secret`; there is no equivalent secret exchange
  in this MVP (the discovery file has no token), so the bridge is trusted only as far as "same
  user, can enumerate `%LOCALAPPDATA%`" - appropriate for read access, not for write/execute.

A future phase could add narrowly-scoped, explicit write tools (e.g. "reveal this file in
Solution Explorer", "set a breakpoint at file:line") if a real workflow needs them, each with its
own justification - not as a blanket "give the bridge write access" change.

## Project layout

```
src/Kubuno.Mcp/                    kubuno-vs-mcp.exe (net8.0) - the MCP server Claude Code launches
  Program.cs                       host + AddMcpServer().WithStdioServerTransport().WithTools<KubunoVsTools>()
  Tools/KubunoVsTools.cs           the 7 [McpServerTool] methods
  Connectivity/IBridgeConnector.cs abstraction the tools call through (real pipe in prod, fake in tests)
  Connectivity/PipeBridgeConnector.cs  discovery + VsMcpBridgeClient wiring

src/Kubuno.Mcp.Bridge/             multi-targeted net48;net8.0 (see its csproj header comment)
  IVsContextProvider.cs            one async method per tool - what a bridge implementation supplies
  BridgeUnavailableException.cs    "no live bridge" signal, shared by client and pipe host
  Contracts/                       BridgeRequest/BridgeResponse envelope + per-tool DTOs
  PipeProtocol/PipeMessageFraming.cs   length-prefixed framing over any Stream
  PipeProtocol/BridgeDispatcher.cs     method-name -> IVsContextProvider call -> BridgeResponse
  PipeProtocol/VsMcpBridgeClient.cs    one-shot pipe client (Kubuno.Mcp side)
  PipeProtocol/VsMcpBridgeHost.cs      accept loop + discovery file lifecycle (VSIX side)
  Discovery/                       BridgeDiscoveryInfo + BridgeDiscoveryFile (read/write/enumerate)
  Dte/DteVsContextProvider.cs      net48-only: IVsContextProvider implemented with EnvDTE/EnvDTE80

tests/Kubuno.Mcp.Tests/            net8.0, MSTest (dotnet test)
  Fakes/FakeVsContextProvider.cs   canned IVsContextProvider (no VS, no DTE)
  Fakes/FakeBridgeConnector.cs     IBridgeConnector -> BridgeDispatcher directly, no pipe - the
                                    "fake bridge" the protocol round-trip tests are built against
  PipeMessageFramingTests.cs       framing over a MemoryStream: round-trips, truncation, bad length
  BridgeDispatcherTests.cs         every method name routes correctly; unknown method / provider
                                    exception both become a clean BridgeResponse.Fail, never a throw
  PipeEndToEndTests.cs             VsMcpBridgeHost + VsMcpBridgeClient over a REAL named pipe
  McpProtocolTests.cs              real McpServer + real McpClient over in-memory duplex streams:
                                    initialize, tools/list (all 7, all read-only), tools/call
                                    (success and the "bridge unavailable" graceful-error path)

docs/MCP.md                        this file
```

## Testing

```bash
# From the repo root (Z:\projects\kubuno\vskubuno). On Z: (a mapped drive), MSTest's test host
# can hit .NET's "remote sources" block - set this first, same as the other test projects.
COMPLUS_LoadFromRemoteSources=1 dotnet test tests/Kubuno.Mcp.Tests/Kubuno.Mcp.Tests.csproj
```

29 tests, all passing as of this writing: 10 framing tests, 10 dispatcher tests, 4 real-named-pipe
end-to-end tests, 5 real-MCP-protocol tests (including the "fake bridge" round-trip the task asked
for and a "Visual Studio not running" graceful-error case).

Manually verified beyond the test suite: `kubuno-vs-mcp.exe`, run standalone with no VSIX/bridge
running, answers `initialize` and `tools/list` correctly (all 7 tools, `readOnlyHint: true`) over
real stdio, and a `tools/call` for `vs_selection` comes back as `isError: true` with the exact
"Visual Studio is not running..." message from `PipeBridgeConnector` - the "Visual Studio not
running / no bridge" case from the task, confirmed end-to-end through the real process and the
real MCP wire format, not just through `McpProtocolTests`' in-memory streams.

## Integration (for whoever wires up `src/Kubuno.VisualStudio` - not done here)

This phase does not touch `src/Kubuno.VisualStudio/` (out of scope - see this task's parallel
rules). What the VSIX package needs to do at load, in outline:

```csharp
// In KubunoPackage.InitializeAsync, after the package has a DTE2:
DTE2 dte = (DTE2)await GetServiceAsync(typeof(SDTE));
var provider = new Kubuno.Mcp.Bridge.Dte.DteVsContextProvider(dte);
_bridgeHost = new Kubuno.Mcp.Bridge.PipeProtocol.VsMcpBridgeHost(provider);
_bridgeHost.Start(
    visualStudioVersion: dte.Version,
    solutionOrFolderPath: dte.Solution?.FullName);

// On package Dispose/unload:
_bridgeHost?.Dispose();
```

`Kubuno.VisualStudio.csproj` would add a `ProjectReference` to `Kubuno.Mcp.Bridge.csproj` (net48
leg picked up automatically, same `IncludeOutputGroupsInVSIX` pattern already used for
`Kubuno.Cargo`/`Kubuno.Launch` in that csproj) and a `Content` item shipping the published
`kubuno-vs-mcp.exe` under `tools\` in the VSIX, e.g.:

```bash
dotnet publish src/Kubuno.Mcp/Kubuno.Mcp.csproj -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -o path\to\Kubuno.VisualStudio\tools\kubuno-vs-mcp
```

```xml
<!-- In Kubuno.VisualStudio.csproj, once the exe is published under tools\kubuno-vs-mcp\: -->
<ItemGroup>
  <Content Include="tools\kubuno-vs-mcp\**\*.*">
    <IncludeInVSIX>true</IncludeInVSIX>
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

## Registering the server with Claude Code

Once `kubuno-vs-mcp.exe` is built (or published as above), point Claude Code at it:

```bash
claude mcp add kubuno-vs -- "Z:\projects\kubuno\vskubuno\src\Kubuno.Mcp\bin\Release\net8.0\kubuno-vs-mcp.exe"
```

or in `.mcp.json`:

```json
{
  "mcpServers": {
    "kubuno-vs": {
      "command": "Z:\\projects\\kubuno\\vskubuno\\src\\Kubuno.Mcp\\bin\\Release\\net8.0\\kubuno-vs-mcp.exe",
      "args": [],
      "env": {
        "KUBUNO_VS_PID": ""
      }
    }
  }
}
```

Once the VSIX ships the exe under `tools\` (see "Integration"), `command` would instead point at
the installed extension's `tools\kubuno-vs-mcp\kubuno-vs-mcp.exe`. Leave `KUBUNO_VS_PID` unset (or
drop it from `env`) unless more than one Visual Studio instance is running at once.

## Known limits

- **One pipe connection per tool call**, not a persistent connection - simple and self-healing
  across a bridge restart, at the cost of a connect handshake per call. Would be the first thing
  to optimize if latency ever matters (keep a connection per discovered pid, reconnect on failure).
- **`vs_error_list`'s `code` field is always `null`.** `EnvDTE80.ErrorItem` has no diagnostic-code
  member separate from its description text (see the "Known EnvDTE gaps" remarks on
  `DteVsContextProvider`) - not populated with a guess.
- **`vs_debugger_state`'s stack frames only get a `filePath`/`line` on the current frame, and
  only when the break was caused by hitting a breakpoint** (not a step or an exception).
  `EnvDTE.StackFrame` carries no location members at all; the only source for one is
  `EnvDTE.Debugger.BreakpointLastHit`, which describes the break location, not each frame.
- **`vs_solution_or_folder`'s Cargo workspace detection is a bounded directory scan** (depth 4,
  skips `target`/`node_modules`/`bin`/`obj`/dot-directories, caps at 25 results), not `cargo
  metadata` - cheap and dependency-free, but it can miss an unusual workspace layout that
  `cargo metadata` would resolve correctly. `Kubuno.Cargo` already owns the authoritative
  `cargo metadata` view of whatever workspace VS has actually opened; this tool is meant to point
  Claude at candidate roots, not to replace that.
- **Windows-only.** Named pipes (`System.IO.Pipes`) and `%LOCALAPPDATA%` are Windows concepts,
  matching Visual Studio itself - no cross-platform concern here.
- **Multiple Visual Studio instances require `KUBUNO_VS_PID`.** Discovery has no notion of "the
  window the developer is currently looking at"; see "Transports and discovery".
