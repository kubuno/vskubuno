# Debugging Rust and Kubuno applications in Visual Studio

What a developer coming from C# gets when debugging a `.rsproj` (or Open Folder) Rust program - a Kubuno desktop
application in particular - with Visual Studio 2026's **native** (MSVC/PDB) debugger, what the extension adds for it,
and where the experience still differs from .NET. Everything marked *verified* was checked live on 2026-09-29 (Visual
Studio 18, French UI, rustc 1.98.1 `x86_64-pc-windows-msvc`) against a scratch *Kubuno Desktop Application* with typed
handlers, a panicking button, a worker thread and an `async` handler, driven through DTE and UI Automation (see
"How this was verified").

## 1. Feature matrix

| C# / .NET experience | Rust in Visual Studio with Kubuno | Status |
|---|---|---|
| Exception breaks where it is thrown, with a popup "System.X: 'message'" | A **panic** breaks at the line that panicked (`v[i]`, `x.unwrap()`, `panic!`), and the exception helper reads "**Rust panic:** *message*" with the message taken from the panic payload | verified |
| Exception Settings > "Common Language Runtime Exceptions" checkboxes | Exception Settings > **C++ Exceptions > ` ?? ::st_panic`** (the name Visual Studio gives Rust's `rust_panic`), checked ("Break When Thrown") by default | verified |
| Unhandled exception dialog of the app only after the debugger | Under the debugger the Kubuno **crash window opens only if you continue** past the break | verified |
| Locals/Autos/Watch/DataTips show readable values | `String`, `Vec`, `HashMap`, `Rc`, `RefCell`... (toolchain natvis, embedded in every PDB); **`Some("two")`, `Ok(42)`, `Err(..)`** with their payload; `Mutex<T>` as its value; `Box<dyn Trait>` as the concrete type's vtable; Kubuno types (`Value`, `Row`, `Sender`, `ElementRef`, `MouseEventArgs`, `Rect`, `Color`...) | verified |
| Conditional breakpoints, hit counts, tracepoints (`{expr}`), function breakpoints, data breakpoints | All work (C++ expression syntax, see §4) | verified |
| Breakpoint in an event handler, hit by clicking in the app | Yes (line breakpoints bind in `#[event_handlers]` impl blocks, sync and `async`) | verified |
| F11 skips framework code, stops in your code | **Step filters**: F11 never enters `core`/`alloc`/`std` or the Kubuno framework, but still stops in *your* code they call (a closure passed to `map`, an event handler behind `frame_typed`) | verified |
| Just My Code call stack (`[External Code]`) | std panic machinery, the standard library and the Kubuno framework collapse into `[External Code]` | verified |
| Threads window names | `Kubuno UI thread`, `kubuno-stdout`, `kubuno-delay`, and every thread you name with `std::thread::Builder::name` | verified |
| Debug > Attach to Process | Works on a Kubuno app started outside the debugger; breakpoints bind | verified |
| `Debugger.Break()` / `Debugger.IsAttached` | `kubuno_views::debug_break()`, `kubuno_views::debug::is_debugger_attached()`, `result.break_on_err()?` | built, unit-tested |
| Break when an exception is *returned* (no C# equivalent either) | Not possible generically for `Result` (§3.4) | by design |
| Async call stacks / Tasks window | Async handlers show as `...::async_fn$0` frames polled by the executor (collapsed); no Tasks window for Rust futures | limitation |
| Immediate window / QuickWatch evaluate C# | C++ expression evaluator on Rust debug info: fields, pointers, arithmetic, casts, indexing a `Vec`; **no Rust method calls, no `.` on a reference** (§4) | limitation |
| Edit and Continue | Not available for Rust | limitation |

## 2. What the extension installs, and where

| Piece | Where it lives | Why there |
|---|---|---|
| Toolchain natvis (`liballoc`, `libcore`, `libstd`, `intrinsic`) | **Nothing to install**: rustc links every MSVC binary with `/NATVIS:` for `lib\rustlib\etc\*.natvis`, so each PDB embeds them (checked in the PDB of an ordinary crate) | Always matches the toolchain that built the binary; highest-priority natvis source |
| Kubuno natvis | `kubuno-views/natvis/kubuno_views.natvis` in the desktop repository, embedded in every application's PDB by `#![debugger_visualizer]` in `kubuno_views` | Travels with the crate version whose layouts it describes. Being in the PDB is also what lets its `Option`/`Result`/`Value` entries win over the toolchain's generic `enum2$<*>` view (checked: the same entries loaded from the VSIX are ignored) |
| `Kubuno.natvis` (copy of the above) | VSIX `NativeVisualizer` asset (`Debugging\Visualizers\`) | Every module of every session: `kubuno_ui.dll`'s own frames, apps built on an older `kubuno_views`, `Mutex`/`PathBuf`/`Box<dyn>` for any Rust program. Keep both copies in sync |
| `Kubuno.Rust.natjmc`, `Kubuno.Rust.natstepfilter` | `%USERPROFILE%\Documents\Visual Studio 18\Visualizers` and `...\Visual Studio 2022\Visualizers`, copied from the VSIX before every Rust debug launch | Visual Studio reads `.natjmc`/`.natstepfilter` only from its installation folder or this per-user folder, near the start of each session |
| `Kubuno.Framework.natjmc`, `Kubuno.Framework.natstepfilter` | Same folder, unless Tools > Options > Kubuno > **Debugging** > "Treat the Kubuno framework as external code" is off (then removed) | For people who debug Kubuno itself |
| ` ?? ::st_panic` exception entry | `debugging.pkgdef` (default: checked), re-applied before each launch through `Debugger3.ExceptionGroups` | A registry default that was never touched was found not to reach the engine |
| Panic message in the exception helper | `Kubuno.VisualStudio.Debugger.dll` + `.vsdconfig` (Concord component, VSIX `DebuggerEngineExtension` asset) | Reads the panic payload from the debuggee |

The per-user files are named `Kubuno.*`; nothing else in those folders is touched, except the four toolchain natvis
copies earlier versions of the extension installed there (`intrinsic`/`liballoc`/`libcore`/`libstd.natvis`, removed
when their content is Rust's). Because the per-user folder applies to **C++ sessions too**, the filters never match a
bare `std::` - only the Rust standard library's own module names (`std::io::`, `std::collections::`...) and Rust-only
spellings (`impl$N`), so the C++ standard library keeps its usual behaviour.

## 3. Panics as exceptions

### 3.1 What happens

On MSVC a Rust panic unwinds as a C++ exception whose TypeDescriptor is named `rust_panic`
(`library/panic_unwind/src/seh.rs`). Visual Studio undecorates that name as if it were an MSVC one and calls it
**` ?? ::st_panic`** (the Output window prints "exception Microsoft C++ : ?? ::st_panic"; reproduced with dbghelp's
`UnDecorateSymbolName("ust_panic", UNDNAME_TYPE_ONLY)`). That is therefore the name of the Exception Settings entry the
extension registers under **C++ Exceptions** (search "st_panic" in the Exception Settings window). With it checked:

1. the debugger stops when the panic is raised, **before** anything unwinds (first chance);
2. the source window shows **your** line: `Kubuno.Rust.natjmc` marks every standard-library function, the panic
   machinery included, as `ExceptionImplementation`, so the exception is attributed to the first user frame that
   called into the library - `let value = values[index];`, not `alloc::vec::impl$14::index` (verified; without it
   the break lands in `vec\mod.rs`);
3. the exception helper reads **"Rust panic: index out of bounds: the len is 3 but the index is 4"**, followed by the
   message again and a hint (continue with F5; how to turn the break off). The Concord component
   (`RustPanicExceptionFormatter`, an `IDkmExceptionFormatter` filtered on the C++ exception category) reads
   `panic_unwind::imp::Exception { canary, data: Option<Box<dyn Any + Send>> }` at the exception object's address,
   recognizes the canary by the TypeDescriptor it points at, and decodes a `&'static str` or `String` payload by the
   size in its vtable - without relying on field order. Other payload types (`panic_any`) show `Box<dyn Any>`, like
   Rust's own hook. The same text replaces the Output window's "Exception thrown" line.

Locals at that point show the panicking frame's variables (`values = { len=3 }`, `index = 4`, `self`...), and the
Call Stack shows `[External Code]` above your frames.

The location of the panic (`src\main_view.rs:88:21`) is not in the payload; it is in the Output window just above, in
the Kubuno diagnostics line `[PANIC] main: index out of bounds: ... at src\main_view.rs:88:21` (and the backtrace), and
the editor already sits on that line.

### 3.2 Continuing, and the Kubuno crash window

Kubuno's panic hook (`kubuno_controls::host::diagnostics`) checks `IsDebuggerPresent` at the time of the panic:

- **no debugger**: unchanged - log line, crash window, exit code 101;
- **debugger attached, panic inside the application's frame** (event handlers, async handlers, the view): the hook only
  logs and remembers the crash report, and lets the panic unwind, so Visual Studio breaks first. If you continue (F5),
  the unwind reaches `diagnostics::run_frame` around the host's `on_paint` call, which shows the crash window then and
  exits with 101 (verified: a second `KubunoControlsHost` window appears after continuing);
- **debugger attached, panic elsewhere on the UI thread** (nothing could catch it: the window procedure cannot unwind):
  the hook breaks into the debugger itself (`DebugBreak`) before showing the crash window. `KUBUNO_BREAK_ON_PANIC=0`
  (Debug property page, environment) turns that break off.

A panic on a **background thread** is logged and unwinds as usual; with the exception entry checked Visual Studio
breaks on it too, like a first-chance exception in a .NET worker thread.

Unchecking the entry restores "let it run": no break, the crash window appears directly (it is still deferred to the
end of the frame under a debugger, which the user does not notice).

### 3.3 `Result` errors

A `Result::Err` is a value: `?` is an ordinary early return and nothing marks the value that becomes an error, so no
debugger can offer "break when an error is returned" generically (C# has no such thing for return values either).
Instead, for the places you care about:

```rust
use kubuno_views::prelude::*;            // brings BreakOnError

let text = std::fs::read_to_string(path).break_on_err()?;   // stops here when the read fails
let n = kubuno_views::debug_break_on_error(parse(text))?;    // same, as a function
kubuno_views::debug_break();                                 // Debugger.Break()
if kubuno_views::debug::is_debugger_attached() { /* ... */ } // Debugger.IsAttached
```

All of them do nothing without a debugger, so they can stay in shipped code. `break_on_err` also works on `Option`
(breaks on `None`). A conditional breakpoint on the `Err` arm of a `match`, or a breakpoint in the error type's
constructor (`From` impl), are the other usual options.

## 4. Variables, expressions and breakpoints

### 4.1 Reading values

Locals, Autos, Watch, QuickWatch and DataTips use the same natvis. Verified displays:

| Rust value | Shown as |
|---|---|
| `String`, `&str` | `"Hello #1 from Hello"` (text visualizer available) |
| `Vec<String>`, `&[i32]` | `{ len=3 }`, expands to `[0]`, `[1]`... |
| `HashMap<String, i32>` | `{ len=2 }`, expands to `"alice" = 3`, `"bob" = 5` |
| `Option<String>`, `Option<&str>` | `Some("two")`, `None`; expands straight to the payload |
| `Result<i32, String>` | `Ok(42)`, `Err("...")` |
| `Rc<RefCell<Vec<i32>>>` | `{ len=4 }` |
| `Arc<Mutex<u32>>` | `7` (`(poisoned)` when it is) |
| `Box<dyn Any>` | `dyn dbgapp.exe!impl$<u8, core::any::Any>::vtable$` (the concrete type, here `u8`) |
| `kubuno_views::binding::Value` | `Bool(true)`, `F32(36)`, `Str("Ada")`, `List(1 rows)` |
| `Row` | `{ fields=2 }`, expands to `Name = Str("Ada")`, `Age = F32(36)` |
| `&Sender<Button>` | `Button "hello"`, expands to Name/Element/Id/Bounds/..., `[Properties]` |
| `ElementProps` | `{ "hello" properties=5 }`, expands to `Text = Str("Hello")`, `X = Str("24")`... |
| `&MouseEventArgs` | `{ Button=Left X=80 Y=19.71 Clicks=1 }` |
| `kubuno_ui::graphics::Color` | `Color [A=255, R=51, G=102, B=255]` |
| `kubuno_ui::Rect` | `{X=10 Y=20 Width=100 Height=32}` |
| `HandlerTable`, `Event<A>`, `HandlerInfo`, `UiHandle<V>`, `PathBuf`, `RwLock<T>` | handler counts, subscriber count, `name -> method`, view open/closed, the path, the value |

### 4.2 Writing expressions (Watch, Immediate, conditions, tracepoints)

The native debugger evaluates **C++ expressions over Rust's debug information**:

- `self`, `&T` and `&mut T` parameters are **pointers**: write `self->count` or `(*self).count`. **`self.count` does not
  fail - it silently shows a wrong value** (checked: `-1604957032`). This is the one trap to remember.
- Fields, indexing a `Vec`/slice/array (`self->items[1]` gives `"two"`), arithmetic and comparisons
  (`self->status.vec.len > 3`), casts (`(int)x`), `sizeof(dbgapp::main_view::MainViewModel)`, format specifiers
  (`,x`, `,s8`, `,na`, `,!` for the raw view) all work.
- Paths use `::` and the PDB spelling: `dbgapp::main_view::MainViewModel`, `enum2$<core::option::Option<...>>`.
- **Not available**: calling Rust methods or functions (`s.len()`, `v.iter()...`), `HashMap` lookups by key
  (`map["alice"]` - expand the map instead), closures, Rust-only operators and literals (`..`, `as`, `?`), and
  reading an `enum` by variant name (use the natvis view, or `,!` then `variant1.value.__0`).
- The Immediate window evaluates the same expressions; it cannot run statements.

### 4.3 Breakpoints

| Kind | How | Verified |
|---|---|---|
| Line | F9 in a `.rs` file, including handler bodies of `#[kubuno_views::event_handlers]` impl blocks and `async fn` handlers (after an `.await` too) | yes |
| Conditional | *Conditions > Conditional expression*: `self->count == 1`, `index >= 3`, `self->status.vec.len == 0` - C++ syntax (§4.2) | yes (`self->count == 1` skipped the first click, stopped on the second) |
| Hit count | *Conditions > Hit Count*: `=`, `>=`, "is a multiple of" | yes (multiple of 2: stopped on clicks 2 and 4) |
| Tracepoint | *Actions > Show a message*: `worker tick: counter={counter} thread=$TNAME fn=$FUNCTION`; `{expr}` uses natvis | yes (`worker tick: counter=8 thread=dbgapp-worker fn=...on_thread_click::closure$0`) |
| Function | *New > Function Breakpoint*: the PDB path, `dbgapp::main_view::parse_doubled` (a method: `dbgapp::main_view::MainViewModel::on_hello_click`) | yes (stopped on the function's first line) |
| Data | *New > Data Breakpoint* (break mode only): address `&self->count`, 4 bytes | yes (stopped on the line after `self.count += 1`) |
| Dependent | *Conditions > Only enable when the following breakpoint is hit* | native engine feature, not re-verified |

**One line, several stops.** A line containing a macro (`format!`, `vec!`, `println!`, `tracing::info!`) compiles to
several non-contiguous code ranges; a breakpoint on it binds several locations (6 on a `format!` line) and F5 may stop
on the same line 2-6 times. Put the breakpoint on the next line, or use F10 to leave the line.

**Handlers and the macro.** `#[kubuno_views::event_handlers]` re-emits your methods with their own spans, so their
lines map normally. The dispatch glue it generates (`<ViewModel as EventSink>::handle_event`, whose code maps to the
attribute line) is never stepped into (step filter) and collapses into `[External Code]`.

## 5. Stepping

| Command | Rust behaviour | Verified |
|---|---|---|
| F10 Step Over | Line by line; macro lines may take several F10s (§4.3) | yes |
| F11 Step Into | Never enters `core`/`alloc`/`std` (so not `?`, `.clone()`, `.trim()`, `format!`), nor the Kubuno framework or its glue; **still stops in your code they call**: F11 on `items.iter().map(|s| s.len()).sum()` lands in the closure, F11 on `runtime.frame_typed(...)` lands in the handler a click runs | yes (`?` line: no std frame; iterator line: `on_hello_click::closure$0`) |
| Shift+F11 Step Out | Returns to the caller - which, from a closure, is the iterator adapter in `core` (source available with the `rust-src` component). Press Shift+F11 again. C++ Just My Code *stepping* needs code compiled with MSVC's `/JMC`, which rustc has no equivalent of | yes |
| Step Into Specific, Run to Cursor, Set Next Statement | Native engine features; Set Next Statement is as risky as in C++ (drops/moves are not replayed) | not re-verified |

Step filters (`.natstepfilter`, `NoStepInto`) work independently of Just My Code; the files are reloaded at the start
of each session. The standard-library filter is always installed (it is refreshed before each launch); to step into the standard
library occasionally, set a breakpoint inside it (its source is available with the `rust-src` component). The Kubuno framework filter follows the Options setting (§8).

## 6. Call stack, threads, async

- Frames show the PDB names: `dbgapp::main_view::MainViewModel::on_hello_click`, closures as `...::closure$0`,
  impl blocks as `impl$3`, generics in full (`kubuno_views::events::typed::with_args<MouseEventArgs, ...>`). Visual
  Studio cannot trim generics; with Just My Code on, the long framework and std frames are collapsed into
  `[External Code]` instead (right-click > Show External Code to see them).
- The parameter list in a frame's label can be shifted when a parameter is a fat pointer (`element_at(ref$<slice2$<i32>>
  index, unsigned __int64)`); Locals are right (`values = { len=3 }`, `index = 4`).
- A few frames of `kubuno_ui.dll` may show a raw v0-mangled name (`_RINvNtCs...catch_unwind...`) - a symbol without a
  function record in the PDB.
- **Threads window / Parallel Stacks**: the Kubuno host names the UI thread **`Kubuno UI thread`**
  (`SetThreadDescription`); Rust threads created with `std::thread::Builder::new().name("...")` show that name
  (`dbgapp-worker`); Kubuno's own helper threads are `kubuno-stdout`, `kubuno-delay`, `kubuno-crash-window`. Unnamed
  Windows thread-pool/Direct2D workers stay unnamed.
- **Async handlers** run as tasks polled on the UI thread by the views executor: a breakpoint after an `.await` shows
  `...::on_async_click::async_fn$0` (and its `closure$1` for `ui.update(|vm| ...)`) above
  `kubuno_views::events::executor::...::poll` and `run_ready` (collapsed as external). There is no "async call stack"
  (who awaited whom) and no Tasks window for Rust futures: Visual Studio's async tooling is .NET-specific.

## 7. Kubuno designer and the debugger

- **Handlers under the debugger**: press F5, click in the running application, the breakpoint in the handler is hit
  (verified), including when the click comes from UI Automation.
- **Attach to Process** (Debug > Attach to Process, *Native* code): works for a Kubuno application started with Ctrl+F5
  or from Explorer (verified: attached by PID, then a click hit a handler breakpoint). The panic behaviour follows
  `IsDebuggerPresent` at the time of the panic, so attaching later is enough.
- **Debugging the design surface** (custom-control authors): the designer renders a view in a separate process,
  `kubuno-design-surface.exe`, built from the project (`<target>\kubuno-design\debug\<hash>\`), in which your custom
  controls' code (`on_paint`, `get_preferred_size`...) runs with `design_mode()` true. Debug > Attach to Process >
  `kubuno-design-surface.exe` (Native), then set breakpoints in the control's code. Design surfaces are now built with
  full debug information in the debug profile (they were built without, so there was nothing to bind to), and each
  surface keeps its own PDB next to it. While the surface is stopped the designer pane is frozen; detach
  (Debug > Detach All) rather than stopping, or the designer restarts the surface. *Not verified live in this pass*
  (the build change is covered by unit tests only).
- `Component::design_mode()` (WinForms `DesignMode`) tells a control it runs in the designer, e.g. to paint a hint
  instead of loading data.

## 8. Settings and troubleshooting

- **Tools > Options > Kubuno > Debugging > "Treat the Kubuno framework as external code"** (on by default).
- **Tools > Options > Debugging > General > "Enable Just My Code"** (on by default) drives the `[External Code]`
  collapsing and the panic location.
- **Natvis diagnostics**: Tools > Options > Debugging > Output Window > "Natvis diagnostic messages (C++ only)" =
  Verbose, to see which natvis file applies (PDB-embedded ones are listed per module).
- **Exception Settings**: search "st_panic". If a solution's saved settings lost the entry, the next F5 adds it back
  (checked).
- A panic not stopping the debugger: check the entry is checked, and that the binary is a `-msvc` build (on `-gnu`
  panics do not use C++ exceptions).
- Values like `self.count` that look wrong: use `self->count` (§4.2).

## 9. How this was verified

A separate experimental hive (`devenv /rootsuffix KubunoDbg`, VSIX built with
`-p:VSSDKTargetPlatformRegRootSuffix=KubunoDbg` from an isolated worktree), a scratch *Kubuno Desktop Application*
(`C:\kubuno-build\rsproj-test\dbgapp`: `MainViewModel` with `String`, `Vec`, `HashMap`, `Option`, `Result`, `Value::List`,
`Color`, `Rect`, `Rc<RefCell<..>>`, `Arc<Mutex<..>>`, `Box<dyn Any>`; handlers Hello, Panic (index out of bounds), Thread
(named worker thread), Async (`delay(..).await`)). F5 through the `.rsproj` launch provider; clicks through UI
Automation (Kubuno exposes AccessKit); breakpoints, stepping, evaluation, exception settings and the Output window
through DTE (`Debugger`, `Debugger3.ExceptionGroups`, `Breakpoint2.Message`); the exception helper and Call Stack text
through UI Automation. Unit tests: `RustPanicPayloadTests` (payload decoding on a fake debuggee memory, both field
orders, `&str`/`String`/UTF-8/non-text/taken payloads, truncation, name matching), `RustDebuggerFilesTests`
(installation, idempotence, option off, legacy natvis clean-up), `kubuno_views::debug` tests.
