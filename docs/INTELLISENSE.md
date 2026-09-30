# IntelliSense for Rust and `.kbview` - compared with C#

This note is the feature matrix of the editing experience Kubuno gives `.rs` files (rust-analyzer) and `.kbview`
views (kubuno-views-ls), side by side with C# in Visual Studio 2026, and how each piece is wired. "Built-in" means
Visual Studio's own LSP client does it once the server supports the request; "Kubuno" means the extension does it
itself (the reason is given); "Not available" says why.

## How it is wired

- **The `initialize` handshake is adjusted on the wire** (`Core/IntelliSense/LspHandshakeStreams.cs`,
  `RustAnalyzerHandshake.cs`). Visual Studio builds `initialize` itself - it never goes through a middle layer - and
  only lets an allow-list of Microsoft clients expand completion snippets. The Rust client wraps rust-analyzer's
  standard streams, rewrites the first request and its answer, then steps aside (plain pass-through):
  - client side: `snippetSupport`, `labelDetailsSupport`, only `additionalTextEdits` resolved lazily, no
    `itemDefaults`, and the client commands `rust-analyzer.triggerParameterHints` / `rust-analyzer.showReferences`;
  - server side: the semantic token legend becomes C#'s classification names (below), and `completionProvider` is
    removed so Visual Studio's generic completion stays out of Rust sessions (Kubuno's replaces it).
- **rust-analyzer's settings** go in `initializationOptions` (`RustAnalyzerHandshake.InitializationOptions`): reference
  and implementation lenses on, Run/Debug lenses off, `fill_arguments` call snippets, symbol search over all symbols,
  and the inlay hints chosen in the options (below), re-sent live with `workspace/didChangeConfiguration`.
- **Kubuno's own requests** (completion, code lenses) use the client's connection directly and first wait until
  rust-analyzer has been sent the document version they are about (`RustDocumentVersions`, fed by the middle layer's
  `didOpen`/`didChange`): Visual Studio hands its notifications to the connection asynchronously.
- **The middle layer** (`RustAnalyzerMiddleLayer`) silences Visual Studio's own hover and completion, remaps every
  semantic token response, keeps pull diagnostics fresh (unique `resultId`), and adds the `.kbview` side to Rename and
  Find All References of a view handler.

## Rust (`.rs`)

| Feature | C# | Rust in Kubuno | How |
|---|---|---|---|
| Completion list as you type (letters, `.`, `::`, `'` after `&`/`<`) | yes | yes, also when typed fast: `Vec::` or `v.` typed while the identifier list is still open reopens the list (`RustCompletionItemManager` re-triggers it) | Kubuno `RustCompletionSource` (rust-analyzer items) |
| Icons by kind | yes | yes, the same monikers as QuickInfo and Solution Explorer | Kubuno (`RustCompletionPresentation.IconMonikerName`) |
| Filter buttons ("chips") | yes | Modules, Structures, Enums, Traits, Methods and functions, Fields, Locals and parameters, Constants, Macros, Keywords, Snippets | Kubuno |
| Expander "items from unimported namespaces" | yes | yes: "unimported crates and modules" (auto-import items), on by default | Kubuno (`CompletionExpander`) |
| Matched characters in bold, fuzzy matching | yes | yes | Visual Studio's item manager (Kubuno wraps it) |
| List closes when a name is no longer typed (space, `=`...) | yes | yes | Kubuno `RustCompletionItemManager` |
| Commit characters | `(` `.` `;` space... | `( . ; , [ < ) ] > {` (not space: Rust declares new names too often) | Kubuno commit manager; a punctuation commit inserts the bare name (`len`, `Vec`, `println!`) |
| Snippets with Tab stops | yes | yes: argument placeholders (`push(value)`), postfix templates (`.if`, `.match`...), Kubuno's `onpaint`/`event`/`handler`/`prop`; Tab / Shift+Tab / Enter / Esc | Kubuno `RustSnippetSession` (Visual Studio would insert `$0` literally) |
| Auto-import completion adds the `use` | yes | yes (the item is asked again and resolved at commit, for the current text) | Kubuno |
| Keyword completion | yes | yes | rust-analyzer |
| Description beside the list | colorized signature + docs | the same: icon, colorized declaration (`fn len(&self) -> usize`), the import in grey, rustdoc | Kubuno, same renderer as QuickInfo |
| IntelliCode starred items | yes (ML) | yes: only rust-analyzer's top-relevance items (its `preselect`, at the list's best score) are starred. The list keeps rust-analyzer's relevance tiers (its `sortText`) and is alphabetical only inside a tier; after `Type::` the constructors (`new`, `with_capacity`, `from`, `default`...) lead right behind the starred ones | Kubuno (`RustCompletionPresentation.SortKey` / `StarredIndexes` / `IsConstructorLike`) |
| Ctrl+Space / Ctrl+J | yes | yes (never auto-inserts rust-analyzer's "preselected" item) | Kubuno |
| Parameter Info (Ctrl+Shift+Space), current parameter bold | yes | yes, also reopened after completing a call | Built-in (`signatureHelp`), Kubuno triggers it after a commit |
| QuickInfo | yes | yes, C#-style | Kubuno (commit f1aabf4) |
| Inlay hints (types, parameter names, chains) | yes, hold Alt+F1 (C# default: off) | yes, the same: off until Alt+F1 is held; Tools > Options > Kubuno > Rust > Inline hints: per-kind switches like C#'s "Inline Parameter Name Hints" / "Inline Type Hints" (see "Inline hints" below) | Built-in rendering and Alt+F1; Kubuno sets the editor option on Rust views and maps the kinds to rust-analyzer's `inlayHints.*` |
| Semantic colorization | yes | yes, with C#'s colors: structs, enums, traits = interfaces, type parameters, methods, locals, parameters, fields, constants, `keyword - control`, doc comments, `u32`/`bool`/`self` as keywords; plus "Rust - Macro", "Rust - Lifetime" (italic), "Rust - Mutable variable" (underlined), "Rust - Unsafe operation" (bold) in Fonts and Colors | Built-in tagger + Kubuno legend/remap (`RustSemanticTokenMap`) + Kubuno TextMate theme (`rust.tmLanguage.tmTheme`) for the instant, pre-analysis colors |
| Go To Definition (F12), Peek (Alt+F12), Go To Implementation (Ctrl+F12), Go To Type Definition | yes | yes | Built-in |
| Find All References (Shift+F12), grouped window | yes | yes; a view handler also lists its `.kbview` usages | Built-in + Kubuno middle layer |
| Go To All / Navigate To (Ctrl+T, Ctrl+,) | yes | yes, functions and methods included | Built-in (`workspace/symbol`), Kubuno setting |
| Highlight references under the caret | yes | yes | Built-in (`documentHighlight`) |
| Navigation bar (type / member drop-downs), Document Outline | yes | yes | Built-in (`documentSymbol`) |
| CodeLens ("3 references") | yes | yes: "N references \| N implementations", click = Find All References / Go To Implementation; Tools > Options > Kubuno > Rust > CodeLens | Kubuno `RustCodeLens` (Visual Studio's LSP client does not show code lenses); counts for the items near the visible lines |
| Go To Base | yes | Not available: rust-analyzer has no type hierarchy request; Ctrl+F12 on a trait method lists its implementations | - |
| Call Hierarchy | yes | Not available: rust-analyzer supports it but Visual Studio's LSP client has no Call Hierarchy UI | - |
| Rename (F2) inline, preview | yes | yes; a view handler is renamed in the `.kbview` too | Built-in + Kubuno |
| Quick actions (Ctrl+.) with preview, "Implement missing members" | yes | yes: rust-analyzer's assists and fixes, `add_missing_impl_members`, Kubuno's override actions | Built-in (`codeAction` + resolve) |
| Format Document (Ctrl+K, Ctrl+D) | yes | yes (rustfmt), and Format on save option | Built-in |
| Format Selection | yes | Not available: rust-analyzer only formats a range with a nightly rustfmt | - |
| Remove and Sort Usings | yes | Not available: rust-analyzer offers no organize-imports action; its "unused import" quick fix and "Merge imports" assist are in Ctrl+. | - |
| Brace matching, auto-closing pairs, folding, smart indent, Ctrl+K,C / Ctrl+K,U | yes | yes | TextMate language configuration + `foldingRange` |
| `///` continuation on Enter | yes | yes (`///`, `//!`, and `//` when Enter splits a comment) | Kubuno `RustDocCommentContinuation` |
| Squiggles by severity, Error List | yes | yes, fixed errors disappear (unique `resultId`, fdf4460) | Built-in + Kubuno middle layer |
| Error List "IntelliSense" source, quick fix from the Error List | yes | Not available: the Error List shows Visual Studio's LSP entries as they are; fixes are offered in the editor (Ctrl+.) | - |

## Inline hints (rust-analyzer's inlay hints)

Like C# in Visual Studio 2026, the hints (`: FileWatcher`, `title:`, `opts:`...) are **not shown by default**: hold
**Alt+F1** to see them (Visual Studio's own "display inline hints when pressing Alt+F1"). Tools > Options > Kubuno > Rust
> Inline hints:

- **Show inline hints**: While pressing Alt+F1 (default) / Always / Visual Studio setting (Text Editor > All Languages >
  Inlay Hints) / Never.
- **Parameter names** (C#'s "Inline Parameter Name Hints"): on/off, and whether to keep the hint for literal arguments
  (`5`, `"text"`, `true`) and for every other argument. rust-analyzer already drops the hint when the argument is named
  like the parameter or the call is obvious; the literal / other-argument split has no rust-analyzer switch, so Kubuno
  filters `textDocument/inlayHint` answers itself (`RustAnalyzerMiddleLayer`, reading the argument text from the open
  buffer; a hint is kept whenever the text cannot be read).
- **Types** (C#'s "Inline Type Hints"): `let` bindings, hide when the type is apparent (`let w = Widget::new()`), hide on
  variables holding a closure, closure parameter types.
- **Other**: types at the end of method chains, closure return types (never / with a block / always), elided lifetimes
  (never / skip trivial / always), binding modes, names after closing braces. Off by default: those that C# has no
  equivalent for.

`RustInlayHintSettings` (Core) turns the choices into rust-analyzer's `inlayHints.*` settings: they are part of
`initializationOptions` at startup and are sent again with `workspace/didChangeConfiguration` whenever an option changes,
so open editors update without restarting the server. The drawing (colors, size, Alt+F1 handling) is Visual Studio's own
inline hint adornment, the one C# uses, so it follows the theme. An installation that still had the first release's
stored default ("Always") moves to the new default once; any other stored choice is kept.

## Where the options live (Visual Studio 2026 unified settings)

The options pages are registered in Visual Studio 2026's **unified settings** (`UnifiedSettings/kubuno.registration.json`,
declared by `[ProvideSettingsManifest]`; labels in `Resources/Settings.resx` and `Settings.fr.resx`, both generated with the
manifest by `tools/gen-unified-settings.ps1`). Each `DialogPage` (`KubunoDialogPage`) is still the code's view of the
options: its `[UnifiedSetting("moniker")]` properties are read from the unified store (`ISettingsReader`), changes made in
the settings page or the JSON file reach the code live (`SubscribeToChanges`), and `[ProvideOptionPage(IsInUnifiedSettings
= true, UnifiedSettingsCategoryMoniker = ...)]` keeps the classic pages out of the legacy dialog. Values stored by earlier
versions in the classic storage are copied once to the unified store (only the ones different from the default). Visual
Studio 2022 has no unified settings: the same pages stay classic property grids.

## Views (`.kbview`) and the Rust code-behind

| Feature | XAML / C# | `.kbview` in Kubuno | How |
|---|---|---|---|
| Element, attribute and enum value completion | yes | yes | kubuno-views-ls |
| Handler names in `OnClick="..."` | yes | yes: only the code-behind handlers whose signature fits the event | Kubuno `KbviewCrossLanguageCompletion` (`kubuno/compatibleHandlers`) |
| `{Binding ...}` path completion | yes | yes: the view model's paths | Kubuno (`kubuno/bindingPaths`) |
| "New event handler" item | yes | Not in the completion list: double-click the event in the designer's Events tab instead | - |
| QuickInfo | yes | yes, C#-style | Kubuno |
| F12 from a handler attribute to the Rust method | yes | yes | kubuno-views-ls |
| F12 from a control's tag to its Rust type | yes (metadata) | yes: the project's own type (user control) first, else kubuno_ui's | Kubuno `KbviewDefinitionFallback` (rust-analyzer's workspace symbols) |
| F12 from a binding path to the view model | partial | yes: the `"Path" =>` arm of `fn get` | Kubuno |
| Find All References of a handler including the views | yes | yes (from the Rust side) | Kubuno middle layer (`kubuno/renameHandler` probe) |
| Rename a handler everywhere | yes | yes | kubuno-views-ls + Kubuno (EVT-5) |
| Colorization | XAML colors | XML colors for elements/attributes/values, handler names as methods, `{Binding Path}` as a XAML markup extension | Kubuno TextMate theme (`kbview.tmLanguage.tmTheme`) + grammar rules |

## Diagnostics and troubleshooting

- The "Kubuno" Output pane logs the handshake ("completion snippets on, semantic tokens mapped onto N C#
  classifications"), the first completion lists, CodeLens, and any failure.
- `KUBUNO_VS_LOG=<file>` (environment variable of the Visual Studio process) mirrors that pane into a file;
  `KUBUNO_COMPLETION_TRACE=1` adds a trace of the completion sessions (start, filtering, commit).
- Everything above runs only in Rust / `.kbview` editors; C# and other languages are untouched.
