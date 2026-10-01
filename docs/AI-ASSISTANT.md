# Kubuno Dev Assistant — an AI pair programmer for building Kubuno, inside Visual Studio (design study)

Status: **design, 2026-10-01**. Nothing below is built yet. Product owner request (2026-10-01, with a screenshot of
Visual Studio's GitHub Copilot Chat window): *"something similar that lets us use an AI such as Claude or another one,
but specialised for Kubuno"*. Clarified the same day: **a developer-only tool** to help build Kubuno (core, modules,
desktop apps, views), **not** the end-user `assistant` module, with no dependency on any Kubuno server.

Working name: **Kubuno Dev Assistant** (French UI: **« Assistant de développement Kubuno »**). The name keeps it
apart from the end-user *Assistant* module (see §1.3). Final name: open question Q1 (§12).

Contents: 1. What already exists · 2. Provider options · 3. How Copilot does it in VS 2026, and whether to plug into it ·
4. Recommendation in one page · 5. UX · 6. Kubuno specialisation (knowledge pack, commands, tools) · 7. Agent mode ·
8. Security and privacy · 9. Architecture · 10. Lots · 11. Risks · 12. Open questions · 13. Sources.

---

## 1. What already exists

### 1.1 The MCP bridge (pillar 5, `docs/MCP.md`)

Two processes: `kubuno-vs-mcp.exe` (`src/Core/Kubuno.Core.Mcp`, net8.0, official `ModelContextProtocol` C# SDK 2.2,
stdio) and an in-proc bridge (`src/Core/Kubuno.Core.Mcp.Bridge`, `net48;net8.0`) that `KubunoPackage` starts. They talk
over a named pipe (length-prefixed JSON) found through `%LOCALAPPDATA%\Kubuno\vs-mcp\<pid>.json`. The bridge's
`IVsContextProvider` is implemented with EnvDTE (`Dte/DteVsContextProvider.cs`). It exposes **seven read-only tools**
(`ReadOnly = true, Idempotent = true, OpenWorld = false`, in `Tools/KubunoVsTools.cs`):

| Tool | Returns |
|---|---|
| `vs_active_document(startLine?, endLine?)` | path, language, unsaved flag, text or range, line count |
| `vs_selection` | file, 1-based range, selected text |
| `vs_error_list(maxItems?)` | severity, file, line, column, message, project (`code` is always null: EnvDTE gap) |
| `vs_open_documents` | every open tab, active and unsaved flags |
| `vs_solution_or_folder` | solution or folder, root, detected `Cargo.toml` roots (bounded scan) |
| `vs_debugger_state` | mode, call stack (location on the current frame only), locals |
| `vs_output_pane(paneName?, maxLength?)` | text of an Output pane (`Build`, `Kubuno`…) |

The two tools planned "later" (`kubuno_view_selection`, `kubuno_view_preview_png`) are not built. `docs/MCP.md`
explains why **the bridge is read-only on purpose**: the pipe is trusted only as far as "same Windows user", and
external agents already have their own edit tools. The Dev Assistant keeps that rule for the external MCP surface (§9.5).

**Reuse.** `IVsContextProvider` and its DTOs are exactly the context the `#` references need (§5.2). The Dev Assistant
calls the provider **in-proc**; it does not go through the pipe.

### 1.2 Layers and conventions to follow (`docs/ARCHITECTURE.md`)

- Layers whose dependencies only go down: `Kubuno.Core*` → `Kubuno.Rust*` → `Kubuno.Desktop*` / `Kubuno.Web*` /
  `Kubuno.Mobile*`. Targets never reference each other. This is enforced by `tests/Kubuno.Architecture.Tests`. Pure
  logic lives in `*.Logic` assemblies, which have no VS SDK and are tested with `dotnet test`.
- Extension points between layers are MEF `[ImportMany]` contracts. Precedents: `IRustEmbeddedLanguage`,
  `IRustReferenceParticipant` and `ISolutionSymbolProvider` in `Kubuno.Rust.Extensibility`. There are also
  `DialogGallery.Register`, `KubunoHost` and `KubunoLog`.
- **Themed UI rules**: every modal dialog derives from `Kubuno.Core.UI.ThemedDialog` (`ShowModal()`). Tool-window
  content calls `ThemedControls.AddImplicitStyles(Resources)` and uses `EnvironmentColors` brushes. Only theme keys are
  used, never literal colours. Message boxes go through `VsShellUtilities.ShowMessageBox`. Every dialog is listed in the
  Dialog Gallery and checked in the dark and light themes.
- UI strings are French or English, chosen by `Kubuno.Core.Logic.Localization.UiLanguage`. Code and comments are in
  English.
- Tool windows already built: `CrateManagerToolWindow` (multi-instance, document well), `OutlineToolWindow`, Data
  Explorer, Query window, Data Sources (all registered by `[ProvideToolWindow]` on `KubunoPackage`).
- **Sidecar precedent**: `kubuno-data-tool` (`docs/DATA.md` §17). It speaks JSON lines over stdio and handles
  concurrent requests and `cancel {id}`. A redactor runs on every message, so secrets never leave the process. The
  VSIX side logs requests with secrets masked (`DataToolProtocol.ForLog`). Connection strings are stored only in the
  Credential Manager (`Kubuno:DataExplorer:ConnectionStrings:<name>`). The Dev Assistant's host process follows the
  same pattern.
- **`.kbview` language server edit APIs** (`docs/DESIGNER.md` §8): `kubuno/applyEdit` takes ops (`setAttribute`,
  `removeAttribute`, `insertChild`, `removeElement`, `moveElement`, `renameElement`) on stable ordinal element ids and
  returns minimal `TextEdit`s. There are also `kubuno/elementAtOffset`, `kubuno/rangeOfElement`, `kubuno/registry`
  (the live element registry, §5 of DESIGNER.md) and normal LSP diagnostics. The designer applies edits to the
  **VS text buffer** inside one undo transaction (`Designer/Editing/CompoundEditCoordinator`, `IUndoTransactionHost`).
  The designer selection is synchronised by `Designer/Selection/SelectionSyncService` + `StableElementId`.
- **Development database guard**: `Kubuno.Web.Logic.DevDatabase.DevDatabaseGuard` (`KUBUNO_DEV_DATABASE_URL`, name must
  look like a dev database). It is reused as-is to refuse any database action on a non-dev database (§7.4).
- `kubuno-secrets` (desktop repo, `common/kubuno-secrets`) is the Rust OS secret store. On Windows it uses the
  Credential Manager with `CRED_PERSIST_LOCAL_MACHINE`, deliberately so that credentials do not roam. The VSIX side
  uses the same store and the same persistence choice (§8.1).

### 1.3 The `assistant` module (`Z:\src\assistant`, port 3107) — out of scope, and why

The module is an **end-user product feature** of a Kubuno instance. It offers self-hosted multi-model chat (Ollama,
OpenAI, Anthropic, Google), agents, conversations synced through the delta protocol, administrator policies, and a
server-side agentic loop. That loop calls tools through the core's MCP gateway (`/internal/mcp`). The module was read to
avoid confusion, and it is **not** a backend for the Dev Assistant, for these reasons:

- **Different audience and purpose.** The module serves people who *use* a Kubuno instance. The Dev Assistant serves
  people who *build* Kubuno, on their own machine.
- **No server dependency.** The Dev Assistant must work with no Kubuno instance at all, offline when local models are
  used, and before the code under development even compiles.
- **Wrong shape.** The module's API is conversation-centric, and tools run server-side. Client-side ("UI") tools are
  fire-and-forget (`services/agentic.rs` answers "Action transmise à l'interface…" without the client's result), so it
  could not drive Visual Studio tools in a loop.

Nothing in this design calls, extends or links the module. Two side notes surfaced while reading it, for its own
maintainers only: its hard-coded Anthropic model list is outdated (`claude-opus-4-5`, `claude-3-5-*`…), and its agentic
loop sends the final answer as one delta rather than streaming it.

---

## 2. Provider options

### 2.1 Anthropic Claude (primary)

- **Messages API** (`POST /v1/messages`) with **streaming**, **client tools** and **prompt caching**. Current models:
  `claude-opus-5-5` (default, 1M context, $4 / $20 per MTok, cache reads $0.20), `claude-sonnet-5-5` (1M, $2 / $10),
  `claude-haiku-4-5` (alias of `claude-haiku-4-5-20251001`, 200K, $1 / $5). The picker fills itself from the **Models
  API** (`GET /v1/models`, which returns `max_input_tokens`, `max_tokens` and capabilities). A static list is kept only
  as an offline fallback, so new models appear without a VSIX release.
- API points that shape the design (check them again against the docs at implementation time):
  - Thinking on Opus 5.5 cannot be disabled. Depth is set with `output_config.effort`, whose default is `medium`, so we
    always set it explicitly: `medium` for chat, `high` for `/vue`, `/migrer` and agent mode.
  - `thinking.display: "summarized"` shows a reasoning summary on request. The `"updates"` display (beta) gives short
    progress notes between tool calls in agent mode.
  - **Forced `tool_choice` (`any`/`tool`) returns 400** on Opus 5.5 / Sonnet 5.5. We always use `auto`, `strict: true`
    on tool schemas, and instructions in the prompt.
  - **Preserved thinking**: editing earlier turns invalidates thinking blocks. **The conversation must be
    append-only**: "regenerate" and "edit my message" create a *branch* (a new conversation from a prefix) and never
    rewrite history. Secret masking must be deterministic so the same input is masked identically on every turn (§8.3).
  - `eager_input_streaming: true` on client tools streams large tool inputs (whole file contents). The client then
    validates each input against its schema before running anything, and checks `max_tokens` / `refusal` stop reasons
    before running tools.
  - `stop_reason: "refusal"` is handled. The server-side `fallbacks: "default"` option (beta header
    `server-side-fallback-2026-07-01`) is on by default and shown in the settings.
  - Long agent sessions use **context editing** (`clear_tool_uses_20250919`) and, beyond that, **compaction** (beta).
    Both keep the append-only rule. **Task budgets** (beta) let the model pace itself; our own hard cost cap stays the
    enforced limit (§7.3).
  - Caching: render order is `tools` → `system` → `messages`. The tool list is sorted and deterministic, the system
    prompt is frozen, and volatile context goes after the last breakpoint (§9.4). We check
    `usage.cache_read_input_tokens` and show it.
- **Which client?**
  - *Official Anthropic C# SDK* (NuGet `Anthropic` 12.53, 2026-09-30, **netstandard2.0** + net8/net9). It would load
    in-proc on net48, but it pulls `System.Text.Json` ≥ 10, `Microsoft.Extensions.AI.Abstractions` ≥ 10 and
    `System.Net.ServerSentEvents` into `devenv.exe`. `devenv.exe` binds its own versions of these, and this repo already
    works hard to avoid binding conflicts (private SDK packages, ThemedDialog compiled as source). **Rejected in-proc;
    used in an out-of-proc net8 host** (§9.1), where it is the obvious choice.
  - *Claude Agent SDK* is Claude Code as a library (Python/TypeScript). It brings its own Read/Write/Edit/Bash tools that
    bypass VS buffers, VS undo and our approval gates, needs a Node/Python runtime, and is Anthropic-only (no
    local/sovereign mode). **Rejected** for the in-VS assistant. Developers who prefer Claude Code keep using it
    *alongside* VS through `kubuno-vs-mcp` (§3.3).
  - *Rust sidecar*: there is no official Rust SDK, so it would mean raw HTTP and re-implementing streaming, tool-use
    and caching shapes by hand. The repo rule "everything that knows Rust/Kubuno is in Rust" is about Kubuno domain
    logic. Here the Kubuno knowledge stays in Rust (the `.kbview` language server, the registry, `kubuno-data-tool`).
    The host only does provider plumbing, and `kubuno-vs-mcp.exe` is already a net8 C# process. **Recommended: C#
    net8 host** (Q2).

### 2.2 OpenAI-compatible endpoints and local models (« local / souverain » mode)

- One **OpenAI-compatible provider** (`/v1/chat/completions` with `tools` + `stream`) covers OpenAI itself, Mistral,
  vLLM, LM Studio, **llama.cpp `llama-server`** and **Ollama** (`http://localhost:11434/v1`). Configuration: base URL,
  optional key, model id, and capability flags (tools, vision, context size) probed with `/v1/models` or entered by hand.
- In local mode **nothing leaves the machine or the LAN** (§8.4). Tool-calling quality varies a lot between local
  models. The Kubuno commands compensate with validation loops (the `.kbview` language server and `cargo check`
  diagnostics fed back to the model), and the UI warns when the selected model reports no tool support.
- Anthropic-specific features (caching breakpoints, thinking, fallbacks) live only in the Anthropic provider. The
  provider abstraction (§9.3) exposes capabilities and does not reduce everything to the lowest common denominator.

---

## 3. How Copilot does it in VS 2026, and whether to plug into it

### 3.1 What Copilot Chat offers (reference UX)

- A Chat tool window with history, a model picker (with bring-your-own-key for Anthropic, OpenAI and others), `#`
  references (files, solution, errors…), `/` commands, `@` participants (e.g. `@Profiler`), inline chat in the editor,
  and apply-with-diff-preview.
- **Agent mode**: tool calls with a **Confirm** dropdown (this time / this session / this solution / always). Approvals
  can be reset in Options. Copilot Edits-style multi-file change sets are kept or undone.
- **MCP servers**: `%USERPROFILE%\.mcp.json`, `<SOLUTIONDIR>\.vs\mcp.json`, `<SOLUTIONDIR>\.mcp.json` (also
  `.vscode/` and `.cursor/`), stdio or HTTP. MCP tools are **off by default**. The tool list is refetched on
  `notifications/tools/list_changed`, and approvals are reset at that point (a rug-pull guard). A server trust dialog
  appears when a server changes (18.7+), and there are organisation allow-lists.
- **Custom agents** (`.agent.md` files in the repository) and **agent skills** (reusable instruction sets, in the repo
  or the user profile), added in the March 2026 update.

### 3.2 Public extensibility for third-party *extensions*

As of this study, Visual Studio offers **no public API for an extension to add a chat participant or a language-model
tool** to Copilot Chat. VS Code has both (Chat Participant API, Language Model Tools API). VS has `@` participants, but
the documented ones are Microsoft's own. What a VS extension *can* do is ship or register **MCP servers**, and the
repository can carry **custom agents, skills and instructions** files.

### 3.3 Decision

**Build our own tool window** (the Dev Assistant), and **also meet Copilot and Claude Code where they are** at low
cost. Reasons for our own window:

- **Provider freedom**: Claude with our own key and local models, with no Copilot subscription required and a real
  sovereign mode that Copilot cannot guarantee.
- **Deep integration that MCP alone cannot do**: the designer's selected element, live preview, structured `.kbview`
  edits through the language server, our approval policy and command allowlist, and our secret masking *before*
  anything is sent.
- **Kubuno commands** (`/vue`, `/migrer`…) with validation loops are product features of the extension.

The "meet them where they are" part (lot DA-8):
1. The tool registry is shared. The **read-only** Kubuno tools (registry lookup, docs search, `.kbview` validation,
   designer selection) are added to `kubuno-vs-mcp`, so Copilot agent mode and Claude Code see them too.
2. A command **"Kubuno: Register the tools with Copilot / Claude Code"** writes `<SOLUTIONDIR>\.vs\mcp.json`. That file
   is per-user and not committed, so the command writes nothing into the repository.
3. Optional and opt-in: generate a `kubuno.agent.md` custom agent from the knowledge pack's rules digest. This writes
   into the repository, so it is shown as a diff and never committed by us.

---

## 4. Recommendation in one page

- **What**: a "Kubuno Dev Assistant" tool window in VS (Copilot-like UX) for developers building Kubuno. **Claude**
  (Anthropic API) is the primary provider. **OpenAI-compatible and local models** (Ollama, llama.cpp) cover the
  « souverain » mode. It runs **entirely on the developer's machine**, with **no Kubuno server** involved.
- **Process model**: the VSIX (net48) owns the UI, context gathering, tool execution, approvals, diff review and undo.
  A **net8 host process `kubuno-dev-assistant.exe`** owns the providers (official Anthropic C# SDK +
  OpenAI-compatible HTTP), the streaming agent loop, caching and context management, and API-key retrieval. They talk
  over **JSON-RPC on the child's stdio** with MCP-shaped tool descriptors. Same pattern as `kubuno-data-tool`, with no
  binding conflicts in `devenv.exe`, crash isolation and real cancellation.
- **Kubuno specialisation**: a curated **rules digest** (always sent, cached) plus **per-command digests** plus **local
  search over the vskubuno docs**. Live facts come from the tools themselves: the **live element registry** from
  `kubuno-views-ls`, `.kbview` validation, the Error List, `cargo`/`tsc` diagnostics, and the designer selection.
- **Safety by construction, not by prompt**:
  - there is **no shell tool**, only an allowlist of named, parameterised commands;
  - reads are auto-approved and writes go through a diff review with per-hunk accept/reject and VS undo;
  - every execution needs approval, with scopes;
  - push, tag, publish and release are **not expressible** by any tool;
  - databases go through `DevDatabaseGuard`;
  - a secret scanner masks secrets before anything is sent;
  - keys are kept in the Credential Manager and never in logs.
- **MVP (DA-1)**: tool window, Claude provider, `#fichier` / `#sélection` / `#élément` references, `/vue` and
  `/expliquer`, diff review with per-hunk apply and VS undo, secret masking, cost display, Stop.

---

## 5. UX

### 5.1 The tool window

**View › Other Windows › Kubuno Dev Assistant** (French UI: « Assistant de développement Kubuno »). It docks with
Solution Explorer by default (like Copilot Chat) and is a single-instance `ToolWindowPane` registered on
`KubunoPackage`. A keyboard shortcut is chosen against VS defaults and Copilot's bindings.

```
┌ Assistant de développement Kubuno ─────────────────────────────── [+] [⏱] [⚙] ┐
│ ● Claude Opus 5.5 ▾   effort: moyen ▾   [Cloud: api.anthropic.com]  0,18 $ · 41 k↑ (cache 92 %) │
├─────────────────────────────────────────────────────────────────────────────┤
│ Vous    /vue ajoute une ligne « Notifications » avec un interrupteur sous « Langue »  │
│         #élément Grid "settingsGrid" (settings_view.kbview)                    │
│ Claude  ▸ 3 outils (registry, validate, applyEdit)                            │
│         Je propose 2 modifications : settings_view.kbview (+6 −0),            │
│         resources.kbres (+2) …                    [Examiner les modifications] │
├─────────────────────────────────────────────────────────────────────────────┤
│ [#] [/]  Décrivez ce que vous voulez…                         [Envoyer] [■ Stop] │
│ Références : #fichier settings_view.kbview ×   #élément Grid ×               │
└─────────────────────────────────────────────────────────────────────────────┘
```

- **Toolbar**: new conversation, **history** (conversations of this solution: title, date, model, cost; search,
  rename, delete), settings. **Model picker** grouped by provider. **Effort** (low/medium/high) where supported. A
  **destination badge** (« Cloud : api.anthropic.com » or « Souverain : localhost:11434 »), always visible. Live
  **tokens and cost** of the conversation, including the cache hit rate.
- **Transcript**: messages rendered from Markdown. Code blocks are read-only VS editor views (`ITextEditorFactoryService`)
  with the right content type (Rust, `.kbview`, TypeScript, TOML), which gives VS colouring and theming for free, with
  **Copy** and **Insert at cursor**. Tool calls appear as collapsible cards (name, arguments, result summary,
  duration). Approvals and change sets appear as cards with buttons.
- **Input**: multi-line box, `#` and `/` completion popups, reference chips (removable), Ctrl+Enter / Enter to send
  (configurable), ↑ to recall. **Stop** cancels the stream *and* any running tool process.
- **Theming**: native WPF only. `ThemedControls.AddImplicitStyles`, `EnvironmentColors` and `ThemedDialogColors` keys,
  no literal colours, and `VsTheme` for code-block backgrounds. Every dialog (consent, key entry, approval details)
  derives from `ThemedDialog` and is added to the Dialog Gallery. Dark, light, blue and high-contrast themes are
  checked. WebView2 was considered: it works in a VS pane (WV-9a), but its keyboard routing and theme mapping cost more
  than a native transcript (Q6).
- **Accessibility**: transcript items are UIA list items, and streaming text is announced at the end of each message
  rather than token by token.

### 5.2 `#` references

| Reference | Source (reused) | Content sent |
|---|---|---|
| `#fichier[:chemin]` | active document or a picked file (`IVsContextProvider.ActiveDocument`, RDT for unsaved buffers) | path + text, or a slice if large |
| `#sélection` | `IVsContextProvider.Selection` | file, range, text, plus ±N surrounding lines |
| `#élément` | designer selection (`SelectionSyncService`, `StableElementId`) → `kubuno/rangeOfElement` | view path, element id, its XML, ancestor chain (tag + `x:Name`), code-behind handler names |
| `#erreurs` | Error List (`ErrorList`) | items (+ source lines around each), capped |
| `#débogueur` | `DebuggerState` | mode, stack, locals |
| `#solution` / `#projet` | `SolutionOrFolder` + `cargo metadata` (`Kubuno.Rust.Cargo`) + `package.json` | layout, crates, targets, frontend presence |
| `#sortie[:volet]` | `OutputPane` | tail of the pane |
| `#diff` | `git diff` (read-only, in the solution root) | working-tree diff, capped |
| `#ressources` | `.kbres` of the project/view | keys and values, by culture |
| `#doc:<sujet>` | knowledge pack (§6.1) | a doc section |

References are **resolved when the message is sent**, masked (§8.3) and attached to that user message. They are never
injected into the system prompt, so caching keeps working. The model can also pull the same data itself through the
read tools (§6.3).

### 5.3 `/` commands (French names, English aliases)

| Command | Purpose | Main tools | Default effort |
|---|---|---|---|
| `/vue` (`/view`) | Create or modify a `.kbview` from a description | registry, validate, applyEdit, kbres | high |
| `/handler` | Write or complete the code-behind handler of an event (Rust desktop, TS web) | element, rust-analyzer symbols, file edit | medium |
| `/migrer` (`/migrate`) | Migrate a TSX screen (Web) or a legacy desktop screen (Rust painter code) to `.kbview` + code-behind + `.kbres` | registry, validate, codemod (when available), file edit | high |
| `/expliquer` (`/explain`) | Explain an error (Error List item, panic, failing test) and propose a fix | error list, `rustc --explain`, file read | medium |
| `/test` | Write tests for the selection, run them, iterate | file edit, cargo test / Test Explorer | high |
| `/ressource` (`/resource`) | Move literal UI strings to `.kbres` (fr + en), wire up `{Res}` | kbres, applyEdit | medium |
| `/release` | **Read-only** release checklist: version audit (`Kubuno.Web.Logic.Versions.VersionAudit`, `check_versions.py`), CHANGELOG `[Unreleased]` present, build/clippy/tsc green. **It prints the commands for the user to run; it never runs them.** | version audit, changelog read, build | medium |
| `/multi-os` | Review a diff or files against `MULTI-OS-AUDIT.md` rules (paths, FHS hard-coding, `\\?\` canonicalisation, line endings, case) | file read, docs | high |
| `/changelog` | Draft the `[Unreleased]` entry (English, Keep a Changelog) for the current diff | git diff, file edit | low |
| `/aide` | List commands and references | — | — |

Each command is **owned by the layer that knows the domain** (§9.6). `/vue`, `/handler` and `/ressource` belong to
Desktop (and Web for web views). `/migrer` belongs to Desktop (legacy screens) and Web (TSX). `/test` and `/expliquer`
belong to Rust (and Web for TS). `/release`, `/multi-os` and `/changelog` belong to Core.

### 5.4 Inline actions

- **Designer**: element context menu **« Décrire un changement… »** opens a small themed prompt next to the selection.
  It runs `/vue` with `#élément`. The result opens as a change set (§5.5), with an option to show the proposed state on
  the design surface before accepting (DA-6).
- **Editor**: context menu **« Kubuno : demander à l'assistant… »** (with the selection) opens an inline peek-style
  prompt. The answer comes back as a change set or opens in the window. Error List context menu: **« Expliquer avec
  l'assistant »** runs `/expliquer` on that item. Light bulb: no Copilot-style suggestion engine in v1.

### 5.5 Multi-file edits: change sets, diff review, undo

- Every write the model proposes builds up a **change set**: a list of file edits expressed as anchored replacements
  (`old_text` → `new_text`, with a uniqueness check), whole new files, or structured `.kbview` ops. Line numbers are
  never used: they go stale.
- **« Modifications proposées »** document window: the file list (status, +/−), and for each file VS's own inline or
  side-by-side diff viewer (`IDifferenceBufferFactoryService` / `IWpfDifferenceViewer`), with **Accept / Reject per
  hunk**, per file and for all.
- **Apply** writes into the **VS text buffers**, never behind VS's back on disk. Files that are not open are opened
  invisibly (`IVsInvisibleEditorManager`), so dirty tracking, the RDT and file-change notifications stay right. All
  buffers of one apply are wrapped in **one linked undo transaction** (`IVsLinkedUndoTransactionManager`), so one
  Ctrl+Z undoes the whole change set.
- `.kbview` changes go through `kubuno/applyEdit` and the designer's `CompoundEditCoordinator`. This keeps the rule of
  surgical edits that preserve comments, order and formatting, never a regenerated file.
- **Staleness**: the change set records the snapshot it was computed on. If the buffer changed since, hunks whose
  anchor still matches uniquely are kept, the others are marked **conflict** (re-ask the model or edit by hand), and
  nothing is applied silently.

### 5.6 Tool approval

- Each tool declares a class: **read** (auto-approved by default), **write** (always goes through the diff review
  above), **execute** (build, tests, F5, LS-independent processes: approval required), or **forbidden** (not exposed
  at all, §7.4).
- The approval card shows the exact command line or arguments, the working directory and why the model asked. Choices:
  **Autoriser une fois / pour cette session / pour cette solution**, or **Refuser** (with an optional reason sent back
  to the model). Settings let users reset approvals and change per-class defaults; read can be made "ask" as well.
  Approvals are reset when a tool's descriptor changes (Copilot's rug-pull rule).

---

## 6. Kubuno specialisation

### 6.1 Knowledge pack

The relevant vskubuno docs total **≈ 680 KB (≈ 170 k tokens)**: VIEWS-SPEC 41 KB, DESIGNER 140 KB, PROGRAMMING-MODEL
29 KB, EVENTS 170 KB, RIBBON 36 KB, ICONS 6 KB, RESOURCES 18 KB, DESKTOP-OFFLINE-SYNC 76 KB, MULTI-OS-AUDIT 49 KB,
WEB-VIEWS 84 KB, DESKTOP-MIGRATION 7 KB, plus the CLAUDE.md files. Sending all of it every turn is too expensive even
with caching, and much of it (DESIGNER.md especially) is design history rather than rules. So the pack has three tiers:

1. **Rules digest** (≈ 4–6 k tokens, hand-written, versioned in the repo at
   `src/Core/Kubuno.Core.DevAssistant/Knowledge/rules.md`, reviewed like code). Always in the system prompt and
   cached. It contains:
   - UI strings in French (via `.kbres`, never literals), code and comments in English;
   - CHANGELOG `[Unreleased]` in English for every shipped change, one entry per repo touched;
   - Rust: no `unwrap()` outside tests, DB errors logged before return, inputs validated, `clippy -D warnings`;
   - **module isolation** (no cross-module imports; extension points only; `@kubuno/*` singletons external);
   - frontend: no browser dialogs (`useConfirm`/`prompt()`), `MenuDropdown` from `@ui`, the `kubuno-module` cascade
     layer, the `@ui` ≠ `@kubuno/ui` mapping, no "google" references;
   - VS: ThemedDialog rules, never regenerate what the developer owns, surgical XML edits;
   - outbound actions (push/publish/tag/release) are the user's, and commits carry no AI attribution;
   - always test for real, and say what could not be tested.

   It is a **digest**, not the raw files. In particular the workspace `CLAUDE.md` is never sent: it contains
   credentials (§8.3).
2. **Command digests** (1–4 k tokens each), added only when a command is used, at a second cache breakpoint. `/vue`
   gets a VIEWS-SPEC summary of the grammar, values, layout, `{Binding}`, `{Res}` and events. `/migrer` gets the
   WEB-VIEWS §3/§6 mapping and DESKTOP-MIGRATION patterns. `/multi-os` gets the MULTI-OS-AUDIT rule list. `/release`
   gets CLAUDE.md §6/§8 as a checklist. `/ressource` gets the RESOURCES essentials.
3. **Retrieval on demand**: `kubuno_docs_search(query)` and `kubuno_docs_read(doc, section)` tools over the docs cut
   into sections by heading, with a **local BM25 index** built at VSIX build time from `docs/*.md` (shipped as VSIX
   content), plus the opened repository's own `CLAUDE.md`/`README.md`/`docs/` (secret-scanned, §8.3). No embeddings:
   the index is offline, deterministic and needs no model or network, which fits the sovereign mode.

**Live sources beat docs.** The element registry comes from the running `kubuno-views-ls` (`kubuno/registry`), the
same binary the designer uses. Diagnostics come from the LS, the Error List and `cargo`/`tsc`. The digest says this
explicitly: "if docs and the registry disagree, the registry wins".

### 6.2 How `/vue` works (the reference flow)

1. Context: the target view (buffer text), `#élément` if any, the **registry slice** for the elements involved (and
   the full component list as a compact table), the view's `.kbres` keys, and the code-behind's handler signatures.
2. The model answers with **structured ops**: `kubuno/applyEdit` ops for a modification (`insertChild` with an XML
   fragment, `setAttribute`…), or a whole file for a new view. New user-facing strings become `.kbres` entries (fr + en)
   referenced by `{Res key}`.
3. The host applies the ops to a **scratch copy** (LS `applyEdit` on a virtual document) and validates (parse +
   diagnostics + registry check of element, property and enum names). On errors it **loops** with the diagnostics,
   bounded at 3 rounds.
4. The result is a change set (view + `.kbres` + optional handler stubs). In DA-6 it is also previewed on the design
   surface.

`/migrer` follows the same pattern. For TSX it starts from the `@kubuno/views-migrate` codemod output once WV lots
deliver it (WEB-VIEWS §6.2), and the model resolves the codemod's report items. For a legacy desktop screen it maps
painter code to elements according to DESKTOP-MIGRATION patterns. `/expliquer` fetches the error, the source around
it and, for `rustc` codes, `rustc --explain <code>` (an execute-class tool marked "safe", auto-approved, §6.3), then
proposes a fix as a change set.

### 6.3 Tool catalogue (v1 target)

Names are snake_case with a family prefix. Descriptors are MCP-shaped (`name`, `description`, `inputSchema`,
annotations `readOnlyHint`/`destructiveHint` + our `approvalClass`).

| Family | Tools | Class | Owner layer |
|---|---|---|---|
| VS context | `vs_active_document`, `vs_selection`, `vs_error_list`, `vs_open_documents`, `vs_solution_or_folder`, `vs_debugger_state`, `vs_output_pane` | read | Core (existing `IVsContextProvider`) |
| Files | `fs_read(path, range?)`, `fs_list(glob)`, `fs_grep(pattern, glob?)` inside solution roots, secret files denied | read | Core |
| Git | `git_status`, `git_diff(paths?)`, `git_log(n)` | read | Core |
| Knowledge | `kubuno_docs_search`, `kubuno_docs_read` | read | Core |
| Edits | `edit_propose(changes[])` (anchored replacements, new files) → change set | write | Core |
| Views | `kbview_registry(components?)`, `kbview_selected_element`, `kbview_element(id)`, `kbview_validate(text \| path)`, `kbview_apply_ops(path, ops[])` → change set | read / write | Desktop (Web adds the web-target profile) |
| Resources | `kbres_read(path)`, `kbres_set(path, key, values{fr,en})` → change set | read / write | Desktop |
| Rust | `cargo_check`, `cargo_build`, `cargo_clippy`, `cargo_test(filter?)` (through the existing Cargo integration, so diagnostics also reach the Error List), `rustc_explain(code)` (safe), `rust_symbols(query)` (rust-analyzer workspace symbols) | execute / read | Rust |
| Tests | `tests_list`, `tests_run(ids)` (Test Explorer / `Kubuno.Rust.TestAdapter`) | read / execute | Rust |
| Debug | `debug_start(target)`, `debug_stop` | execute | Rust |
| Web | `npm_build`, `tsc_build` (`tsc -b`), `module_toml_validate`, `version_audit` | execute / read | Web |

There is **no general shell, no network tool and no web fetch** in v1 (§7.4). Anthropic server tools (web search) are
off.

---

## 7. Agent mode

### 7.1 Loop

The **Agent** toggle in the input bar changes the system instructions through a mid-conversation system message, so
the cache prefix is kept. The loop is **plan → act → verify**:

- **Plan**: the model first returns a plan (steps, files, commands it expects to run), shown as a card. The user
  accepts, edits or rejects it. Settings can skip this step for read-only plans.
- **Act**: reads run freely. Writes are applied **to buffers inside a session checkpoint** (§7.2), because the agent
  must build and test its own changes. Executions follow the approval policy.
- **Verify**: the command's verification set runs: `cargo check`/`clippy -D warnings`/tests, `tsc -b`, `.kbview`
  validation, Error List empty for the touched files. On failure the loop goes back to Act. It stops when verification
  is green, when the model ends, or when a budget is hit.

### 7.2 Checkpoints and final review

When the agent starts, the touched files are snapshotted (in memory, plus `.vs\Kubuno\dev-assistant\checkpoints\` for
crash safety) as they are first written. At the end, the **cumulative diff against the checkpoint** opens in the
change-set review: accept all, per file or per hunk, or **revert all** (restores the snapshots, also as one undo
unit). The alternative policy "**ask before each write**" (review every change set as it comes) is a setting.

### 7.3 Visibility, cost and interruption

- A live status line shows the step, the current tool and elapsed time. Progress notes come from
  `thinking.display: "updates"` when available.
- Tokens in / out / cache-read and **estimated cost** are shown per turn and per session. A local price table ships
  with the VSIX and is editable; it is labelled as an estimate.
- **Hard cost cap** per agent session (default 5 $, setting) and a maximum number of tool rounds (default 30). The
  Anthropic task budget is set lower than the cap so the model can wrap up in an orderly way before the hard stop.
- **Stop** cancels the HTTP stream and kills running child processes. A **Pause after this step** option is also
  available.

### 7.4 Guard against destructive actions (enforced in code)

- **Allowlist, not denylist.** Only the tools of §6.3 exist. Each execute tool builds its own fixed command line from
  validated parameters (e.g. `cargo test -p <crate> -- <filter>`, with the crate taken from `cargo metadata`), so there
  is no way to express arbitrary commands.
- **Never**: `git push` / `tag` / `remote` / `commit --amend` / `reset --hard` / `clean`, `npm publish`,
  `cargo publish`, `gh`, `_tools/release.sh` / `publish_all.sh` / any `_tools` publishing script,
  `kubuno modules:install` on a non-dev core, and network calls. In v1 even `git commit` is not available: the developer
  commits.
- **Databases**: only through `DevDatabaseGuard`-approved URLs (dev-looking names, from `KUBUNO_DEV_DATABASE_URL`).
  Any other database is refused with the guard's message. The assistant never reads connection strings: tools receive
  a target name, as `kubuno-data-tool` does.
- **Paths**: writes only inside the solution or folder roots. Never under `.git\`, `%USERPROFILE%\.ssh`, `.vs\` (except
  our own folder), `target\` or `node_modules\`. Protected files (secret files, §8.3) are never written.
- **Prompt injection**: file contents and tool results are data. A file asking for a dangerous action meets the same
  gates. The rules digest restates CLAUDE.md, but the **enforcement is the code above**, never the prompt.

---

## 8. Security and privacy

### 8.1 Keys

- API keys are stored in the **Windows Credential Manager**, generic credential
  `Kubuno:DevAssistant:Provider:<provider-id>`, with `CRED_PERSIST_LOCAL_MACHINE`. This is the same choice as
  `kubuno-secrets`: credentials do not roam with a roaming profile.
- They are entered in a ThemedDialog with a password box. Settings only show « clé enregistrée » / Remplacer /
  Supprimer and **never display the key**.
- **The host reads the key itself** (P/Invoke `CredRead`) when it builds a provider. The key never crosses the stdio
  channel, never goes into an environment variable, and is never written to a file, a log or a conversation.
- Logs (Kubuno pane, optional, off by default) go through a redactor in the style of `DataToolProtocol.ForLog`: headers
  removed, file contents reduced to path + length, and masked placeholders only.

### 8.2 What is sent, where: a clear notice

- **First use of each provider** shows a consent dialog (ThemedDialog). It states the endpoint host, *what* is sent
  (your messages, attached references, the contents of files the assistant reads with tools, tool results, the rules
  digest), *what is not sent* (masked secrets, denied files), and a link to the provider's data policy. It also says
  that nothing goes to Kubuno or anyone else.
- The **destination badge** is always visible (§5.1). **« Voir la requête »** on any turn shows the exact assembled
  request as sent (masked), including the system prompt and tool list.
- **No telemetry.** The extension sends nothing anywhere except the chosen provider endpoint. A local usage journal
  (tokens and cost per day) exists only to display totals.

### 8.3 Secret scanning and masking before sending

Context: a password recently leaked in a repo, and **the workspace `CLAUDE.md` itself holds live credentials** (§9 of
that file: an admin password and an npm token). The Dev Assistant must assume secrets are present in what it reads.

- **Denied files** (never read, never attached): `.env*`, `*.pem`, `*.key`, `*.pfx`, `*.p12`, `id_rsa*`,
  `secrets.json` / user-secrets folders, `.npmrc` / `.pypirc` containing auth, `*.kdbx`, `credentials*`,
  `.git-credentials`, `config.toml` files under module folders when they hold `api_key`/`secret`/`password` values,
  and everything outside the solution roots. The list is configurable.
- **Scanner** (pure logic in `Kubuno.Core.DevAssistant.Logic`, unit-tested). It runs on **every outgoing piece of
  text**: references, tool results and the user's own message.
  - Known token formats: Anthropic/OpenAI keys, GitHub `ghp_` / `github_pat_`, npm `npm_`, AWS `AKIA…`, Slack `xox*`,
    JWTs, PEM private keys.
  - Credentials inside URLs and connection strings (`postgres://user:pass@`, `Password=`/`Pwd=`).
  - Assignments to secret-like names (`password`, `passwd`, `secret`, `token`, `api_key`, `authToken`,
    `internal_secret`…) with a non-placeholder value, plus an entropy check.
  - Values present in the Credential Manager entries owned by Kubuno (compared by hash, never by sending them).
- **Masking**: each secret becomes a stable placeholder `«secret:npm-token#1»`. It is deterministic per conversation,
  so history stays identical and caching and preserved thinking keep working. A local in-memory map
  (placeholder → value, never persisted, never sent) lets **edits that keep a placeholder restore the original value
  when applied**. Without that, a change set touching a masked line would overwrite the real secret with the
  placeholder. If the model *introduces* a placeholder elsewhere, the hunk is flagged.
- **Hard stop**: if a user message itself contains a detected secret, sending is paused with « Ce message contient un
  secret probable : masquer / envoyer quand même / annuler ».
- Conversations are **persisted masked** (§9.7), so a secret never lands in `.vs` through the assistant.

**Recommended immediately, independent of this tool**: remove the credentials from the workspace `CLAUDE.md`, rotate
them, and keep them in a password manager. The assistant's rules digest never includes that file raw.

### 8.4 « Souverain » mode

A switch (per solution, or global by policy). When on:

- **only** providers whose endpoint is loopback or a private-network address (RFC 1918 / ULA, user-confirmed host
  list) are allowed;
- Anthropic and other cloud providers are hidden and refused at the host level, not only in the UI;
- the badge turns « Souverain »;
- the consent text says that nothing leaves the machine or the LAN.

Same ideas as the end-user module's "allow cloud providers" policy, implemented independently here.

---

## 9. Architecture

### 9.1 Processes

```
devenv.exe (net48, in-proc)                                   kubuno-dev-assistant.exe (net8, child process)
┌──────────────────────────────────────────────┐  JSON-RPC   ┌────────────────────────────────────────────┐
│ Kubuno.Core.DevAssistant                      │  over the   │ Kubuno.Core.DevAssistant.Host               │
│  tool window, input, transcript, history      │  child's    │  providers: Anthropic (official C# SDK),    │
│  reference resolvers (#), commands (/)        │  stdio      │   OpenAI-compatible (HttpClient)            │
│  tool registry + executors (MEF, per layer)   │◀──────────▶│  agent loop, streaming, caching, context    │
│  approvals, change sets, diff review, undo    │             │   editing / compaction, cost accounting     │
│  secret masking (Logic), consent, settings    │             │  key read from Credential Manager           │
│ Kubuno.Core.DevAssistant.Logic (pure, tested) │             │  Markdown → block model (Markdig)           │
└──────────────────────────────────────────────┘             └──────────────┬─────────────────────────────┘
                                                                            │ HTTPS / HTTP (local)
                                                         api.anthropic.com · OpenAI-compatible · Ollama / llama.cpp
```

- **Why the loop lives in the host**: streaming and SDK features stay out of `devenv.exe`. Tool calls come back to the
  VSIX as JSON-RPC *requests* (`tool/invoke`, `approval/request`), and the VSIX answers them. VSIX → host requests are
  `session/send`, `session/cancel`, `models/list`, `provider/test`, `conversation/branch`. Host → VSIX notifications
  are `stream/delta`, `stream/toolCall`, `usage`. Everything has ids and cancellation, as in `kubuno-data-tool`.
- **Masking happens in the VSIX** before anything reaches the host, so the host only ever sees masked text and the
  placeholder map never leaves `devenv.exe`.
- The host starts on first use, restarts after a crash (the session is replayed from the persisted conversation) and
  stops with the package. It ships in the VSIX `tools\` folder (self-contained single-file publish, as planned for
  `kubuno-vs-mcp`).

### 9.2 Tool-call protocol: MCP or in-proc?

**In-proc tools, with MCP-shaped descriptors on a private channel.** The VSIX holds the tool registry (tools need DTE,
text buffers, the designer and the LS clients that live in `devenv.exe`). It sends `tools/list`-shaped descriptors to
the host at session start and executes `tools/call`-shaped requests. Running real MCP between the host and the VSIX
would bring the MCP SDK's dependencies into `devenv.exe` for no gain, and it would reuse a pipe that `docs/MCP.md`
deliberately limits to reads. Because the shapes match, a read tool can be **re-exposed unchanged** through
`kubuno-vs-mcp` (§9.5). A later option is to let the host also consume user-configured external MCP servers with the
`ModelContextProtocol` client, behind the same approval gates (not in v1).

### 9.3 Provider abstraction (host)

`IModelProvider`:
- `Capabilities`: tools, streaming, thinking/effort, prompt caching, vision, context and output limits, refusal
  fallback, local or cloud.
- `ListModelsAsync`.
- `StreamAsync(request, ct)`, which yields normalised events (text delta, thinking/progress delta, tool_use start and
  input delta / complete, usage, stop reason).
- `ToProviderMessages(history)`, which keeps provider-native blocks verbatim: thinking blocks and tool_use ids are
  stored as received so replay is exact.

Two implementations: `AnthropicProvider` (official SDK, beta endpoint when a beta feature is on) and
`OpenAICompatibleProvider` (raw `HttpClient` + SSE, `tools`). The history format stored on disk is ours
(provider-neutral turns + the provider-native payload of each assistant turn), so a conversation can be *branched*
onto another model. It is not mutated in place.

### 9.4 Context-window management and caching

- **Prompt layout**: [tools, sorted by name] → [system: identity + rules digest] **breakpoint 1** → [command digest
  when a command is active] **breakpoint 2** → [messages, append-only; references live inside the user message that
  added them] → automatic caching on the last message.
- **Budget**: the VSIX budgets references before sending (default 60 k tokens of attachments per message, configurable)
  by trimming the largest first and offering « inclure en entier ». Tool results above a cap are truncated, with a
  continuation handle (`fs_read` with a range).
- **Long sessions**: context editing clears old tool results first, then compaction (Anthropic) or a local summary
  turn (other providers) takes over near the model's limit. Both are append-only.
- Local models with small contexts get a smaller rules digest variant and stricter caps (the capability decides).

### 9.5 Relationship with `kubuno-vs-mcp`

Unchanged contract: read-only, no write or execute over the pipe. DA-8 adds the read-only Kubuno tools
(`kubuno_docs_*`, `kbview_registry`, `kbview_validate`, `kbview_selected_element` — the planned
`kubuno_view_selection`) to the bridge, so Copilot agent mode and Claude Code benefit as well. The Dev Assistant does
not depend on the pipe.

### 9.6 Layering

| Layer | Assembly (new) | Owns |
|---|---|---|
| Core | `Kubuno.Core.DevAssistant.Logic` (netstandard2.0, no VS SDK) | protocol DTOs, command parser, reference model, **secret scanner/masker**, approval policy, command allowlist model, change-set/hunk model and diff, conversation store (JSONL), BM25 docs index, price table. Tested in `tests/Kubuno.Core.Tests` (or a new `Kubuno.Core.DevAssistant.Tests`). |
| Core | `Kubuno.Core.DevAssistant.Host` (net8 exe `kubuno-dev-assistant.exe`) | providers, loop, streaming, caching, key read. Tested with a fake provider replaying recorded SSE fixtures. |
| Core | `Kubuno.Core.DevAssistant` (net48, VS) | tool window, transcript, input, review window, consent and key dialogs (ThemedDialog, gallery), unified settings page « Kubuno › Assistant de développement », Credential Manager writes, VS-context / files / git / docs / edit tools, `/release` `/multi-os` `/changelog`, **the extension contracts** |
| Rust | `Kubuno.Rust` (`DevAssistant\`) | cargo / rustc / rust-analyzer / Test Explorer / debug tools, `/test`, `/expliquer` for Rust |
| Desktop | `Kubuno.Desktop` (`DevAssistant\`) | `.kbview` / `.kbres` tools (LS edit APIs, registry, validation, designer selection), `#élément`, `/vue` `/handler` `/ressource`, `/migrer` (legacy desktop), the designer inline action |
| Web | `Kubuno.Web` (`DevAssistant\`) | npm / tsc / module.toml / version-audit tools, `DevDatabaseGuard` wiring, `/migrer` (TSX), web-view profile of `/vue` |
| Mobile | — | nothing yet. The contracts allow a Gradle/adb toolset later. |

**Extension contracts** (MEF, `Kubuno.Core.DevAssistant.Extensibility`, imported with `[ImportMany]`, so Core never
names a higher layer):
- `IDevAssistantToolProvider` → tool descriptors + executor;
- `IDevAssistantReferenceProvider` → `#` kinds + resolver;
- `IDevAssistantCommandProvider` → `/` commands: name, aliases, digest, default effort, verification set;
- `IDevAssistantKnowledgeProvider` → extra digest or docs sections.

Each target registers its parts like the existing Rust contracts do. `tests/Kubuno.Architecture.Tests` gains the new
projects automatically by name (Core layer). `KubunoPackage` gets the `[ProvideToolWindow]` and the vsct entries.

### 9.7 Persistence per solution

Conversations are stored as `<root>\.vs\Kubuno\dev-assistant\conversations\<id>.jsonl`. `.vs` is per-user and already
ignored by git. One JSON record per turn, **masked**, with provider-native payloads, usage and cost. A small index file
holds titles; titles are generated locally, or by the cheapest available model when the user allows it. Retention
setting: keep N days (default: unlimited) and « Tout effacer ». Nothing is stored outside `.vs`. Settings use the
unified settings plumbing (`KubunoDialogPage`).

---

## 10. Lots

Sizes are rough agent-days: **S ≈ 2–4**, **M ≈ 5–8**, **L ≈ 10–15**. Each lot ends with build + `dotnet test` +
an experimental-instance live check (dark and light themes) + CHANGELOG `[Unreleased]`. **No automated test calls a
real API with a real key**: providers are tested against recorded SSE fixtures and a local fake server. The live
check against Anthropic is run by the developer with their own key.

| Lot | Content | Size | Main risks | Verification |
|---|---|---|---|---|
| **DA-0 spike** | net8 host + official Anthropic SDK streaming + JSON-RPC stdio with the VSIX. WPF transcript with streaming and editor-view code blocks (perf with 200 messages). `IWpfDifferenceViewer` review with per-hunk accept on 2 buffers + one linked undo. Credential Manager round-trip from the host. | S | linked undo across invisible editors; transcript perf | measured numbers recorded in this file; a throwaway sample |
| **DA-1 MVP** | Tool window, history, model picker (Models API), effort. Claude provider, key dialog, consent, destination badge. Secret scanner + masking + placeholder restore. `#fichier`, `#sélection`, `#élément`. Read tools (VS context, `fs_*`, `kbview_registry` / `_validate` / `_element`). `/vue` (create + modify via ops, LS validation loop) and `/expliquer`. Change sets + review window + linked undo. Cost display, Stop, persistence. | L | quality of `/vue` ops; LS scratch-document validation; staleness handling | Logic tests (scanner corpus incl. the formats in §8.3, masking determinism, anchor matching, conflict detection, command parsing). Host tests on fixtures (tool loop, cancellation, refusal, caching fields). Live: a `/vue` that adds a row to a sample view, accepted per hunk, undone with one Ctrl+Z; `/expliquer` on a real `cargo` error; a planted fake token never in « Voir la requête ». |
| **DA-2 Kubuno knowledge** | Rules digest, command digests, BM25 docs index + `kubuno_docs_*`, `#erreurs` `#débogueur` `#solution` `#sortie` `#diff` `#ressources` `#doc`, `/ressource`, `/handler`, `/changelog`. Cache layout tuned. | M | digest drift from docs | index tests; `cache_read_input_tokens` > 0 from turn 2 measured live; digest reviewed by the product owner |
| **DA-3 execution & approvals** | cargo check/build/clippy/test, `rustc_explain`, Test Explorer list/run, F5/stop, `tsc -b` / npm build, approval cards with scopes and reset, allowlist, `DevDatabaseGuard`, `/test` | M | long-running processes and cancellation; Error List duplication | allowlist tests (no way to express a forbidden command); live: `/test` writes and runs a test, Stop kills `cargo` |
| **DA-4 agent mode** | plan → act → verify, checkpoints + final review / revert all, budgets (tokens, cost cap, rounds), context editing / compaction, progress notes, `/multi-os`, `/release` (read-only) | L | runaway loops; checkpoint restore with unsaved user edits | loop tests on fixtures (budget stop, verification-driven retry); live: an agent task touching 3 files ending green, then « tout annuler » restores exactly |
| **DA-5 local / souverain** | OpenAI-compatible provider (Ollama, llama.cpp, vLLM, LM Studio), capability probing, small-context digest variant, « Souverain » switch enforced in the host | M | uneven tool-calling in local models | provider tests on fixtures; live with Ollama on localhost (one coder model), « Souverain » refusing a cloud provider at host level |
| **DA-6 inline & designer** | Editor inline prompt (peek), Error List « Expliquer », designer « Décrire un changement… », proposed-state preview on the design surface before accepting | M | design-surface preview of an uncommitted buffer state | live, both themes |
| **DA-7 `/migrer`** | Legacy desktop screen → `.kbview` (DESKTOP-MIGRATION patterns), TSX → `.kbview` (with `@kubuno/views-migrate` once available) | L | depends on WV/desktop-migration lots; large inputs | a real screen from desktop and one from a web module migrated, built and pixel-checked as those lots require |
| **DA-8 Copilot / Claude Code bridge** | Read-only Kubuno tools in `kubuno-vs-mcp`; « Enregistrer les outils pour Copilot / Claude Code » (`.vs\mcp.json`); optional `kubuno.agent.md` generation (diff, opt-in) | S | Copilot config format changes | `McpProtocolTests` extended; live in Copilot agent mode |

Order: DA-0 → DA-1 → DA-2 → DA-3 → DA-4. DA-5, DA-6 and DA-8 can follow DA-2 in parallel. DA-7 waits for the migration
lots.

---

## 11. Risks (cross-cutting)

- **`devenv.exe` dependency conflicts** are avoided by the host process. The remaining in-proc additions are WPF and
  the VS SDK. If Markdown rendering is needed in-proc, it is done from a block model the host produces (Markdig runs in
  the host).
- **API drift** (betas, model behaviour): the provider isolates beta usage behind capability flags, and models come
  from the Models API. Every beta feature can be switched off in settings.
- **Cost surprises**: estimates are shown always, a hard cap applies per session, and caching is measured. Defaults
  are effort `medium` and `high` only where it pays off.
- **Secret leakage** through a format the scanner does not know: denied files, a « Voir la requête » audit, a user
  extension list, and a hard stop on detected secrets in user text. The residual risk is stated in the consent dialog.
- **Model edits clobbering developer work**: no writes outside buffers, staleness conflicts are never applied
  silently, one undo unit, checkpoints in agent mode.
- **Local model quality** (sovereign mode): validation loops, and a clear UI warning when a model lacks tool support.
- **Copilot overlap**: we do not compete on generic completion. The value is in Kubuno-specific tools, commands and
  sovereignty, and DA-8 keeps Copilot users served.

---

## 12. Open questions (each with the recommended answer)

1. **Name.** « Assistant de développement Kubuno » / *Kubuno Dev Assistant* (internal: `Kubuno.Core.DevAssistant`,
   `kubuno-dev-assistant.exe`)? **Recommended: yes.** It cannot be confused with the end-user *Assistant* module.
2. **Host language.** C# net8 host with the official Anthropic SDK, or a Rust sidecar with raw HTTP? **Recommended:
   C# net8.** Official SDK, same precedent as `kubuno-vs-mcp.exe`, and Kubuno domain knowledge stays in the Rust
   language server and tools.
3. **Default model and spend.** **Recommended:** `claude-opus-5-5` by default (effort `medium` in chat, `high` for
   `/vue`, `/migrer` and agent mode), `claude-sonnet-5-5` one click away, `claude-haiku-4-5` only for titles and
   summaries if allowed. Hard cap of 5 $ per agent session, adjustable.
4. **Agent writes.** Apply to buffers inside a checkpoint with a final review, or ask before each write?
   **Recommended:** checkpoint + final review by default (needed to build and test), « demander à chaque écriture »
   as an option. Executions ask the first time per session.
5. **Copilot.** Own window + MCP bridge, rather than trying to live inside Copilot Chat? **Recommended: yes.** VS
   offers no public chat-participant or LM-tool API to extensions today, and our sovereign mode and designer
   integration need our own window.
6. **Transcript rendering.** Native WPF (themed, VS editor views for code) or WebView2? **Recommended: native WPF.**
   WebView2 keyboard routing (WV-9a) and theme mapping cost more for a chat surface.
7. **Conversation storage.** Local `.vs` only? **Recommended: yes**, masked, with a retention setting. No sync, no
   server.

---

## 13. Sources

- Anthropic Claude API: models, thinking/effort, tool use, prompt caching, context editing, compaction, fallbacks,
  task budgets. Taken from the `claude-api` reference bundled with Claude Code (cached 2026-09-25); check it again
  against <https://platform.claude.com/docs> at implementation time.
- Official Anthropic C# SDK, target frameworks and dependencies: <https://www.nuget.org/packages/Anthropic>
  (12.53.0, 2026-09-30, netstandard2.0 / net8 / net9).
- Visual Studio MCP servers (file locations, tool lifecycle, approvals, trust dialog):
  <https://learn.microsoft.com/en-us/visualstudio/ide/mcp-servers?view=visualstudio>.
- Copilot in Visual Studio, March 2026 update (custom agents `.agent.md`, agent skills, MCP allow-lists):
  <https://github.blog/changelog/2026-04-02-github-copilot-in-visual-studio-march-update/>.
- Agent mode GA with MCP in Visual Studio:
  <https://devblogs.microsoft.com/visualstudio/agent-mode-is-now-generally-available-with-mcp-support/>.
- Bring-your-own model in Copilot Chat (VS): <https://learn.microsoft.com/en-us/visualstudio/ide/copilot-select-add-models?view=visualstudio>.
- VS Code Chat Participant API (for comparison; no VS equivalent found): <https://code.visualstudio.com/api/extension-guides/ai/chat>.
- In-repo: `docs/MCP.md`, `docs/ARCHITECTURE.md`, `docs/DESIGNER.md` §5/§8, `docs/DATA.md` §17,
  `docs/WEB-VIEWS.md` §6/§10/§11, `docs/RESOURCES.md`, `docs/VIEWS-SPEC.md`, `docs/MULTI-OS-AUDIT.md`,
  `src/Core/Kubuno.Core.Mcp/Tools/KubunoVsTools.cs`, `src/Web/Kubuno.Web.Logic/DevDatabase/DevDatabaseGuard.cs`,
  desktop `common/kubuno-secrets/src/lib.rs`; `Z:\src\assistant` (read only, to explain why it is out of scope).
