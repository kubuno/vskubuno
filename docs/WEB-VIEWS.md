# Kubuno web views — `.kbview` for the web UI, designed in Visual Studio — design note

> Status: **design study, partly built** (2026-10-01; built so far: WV-0, WV-1, WV-2, WV-3, WV-4, WV-8, WV-9a — see §12–§14; WV-5a in part and the first core pilot, the header's WaffleMenu & AccountMenu — see §15). Product-owner decision (2026-10-01): the Kubuno **web** UI
> (core host + module frontends) moves toward a declarative view format similar to the desktop `.kbview`, editable in
> the Visual Studio visual designer (Toolbox, design surface, Properties, ⚡ events, code-behind); the design surface
> renders with **WebView2** (WebKit is dropped); existing React/TSX code is migrated or transformed.
>
> Builds on: `XML_VIEWS.md` (format), `PROGRAMMING-MODEL.md` (`#[kubuno_desktop::view]`, the WinForms-like model),
> `DESIGNER.md` (design surface protocol, Toolbox, Properties, element ids), `EVENTS.md` (events, custom controls,
> metadata), `DATA.md` (data components, `{Binding Source=…}`), `RIBBON.md`, `ICONS.md`, `ARCHITECTURE.md` (layers,
> `Kubuno.Web`), `kubuno-desktop-resources` (`.kbres`, `{Res}`; `docs/RESOURCES.md` is referenced by the code but not in the
> repository yet), and `docs/WEB.md` (phases 1–2: core and modules opened, built and debugged in VS — being written in
> parallel; this note assumes its `.esproj` + `.rsproj` solution shape).

## 0. What the web code looks like today (measured)

Measured on 2026-10-01 over the 24 repositories with a `frontend/src` (a local copy of `*.ts`/`*.tsx`, tests
included in the LOC column, `.d.ts` excluded). Counts are `grep` heuristics: good for orders of magnitude, not
exact.

| Repo | `.tsx` | LOC (ts+tsx) | `@ui` tags | HTML tags | `useQuery`-family | module routes | dialog files | settings files | heavy files¹ | size <150 / <500 / ≥500 lines |
|---|---|---|---|---|---|---|---|---|---|---|
| core | 391 | 100 092 | 780 | 4 460 | 141 | (router) | 30 | 40 | 14 | 200 / 172 / 19 |
| office | 181 | 144 276 | 769 | 5 260 | 108 | 37 | 27 | 4 | 22 | 86 / 66 / 29 |
| mail | 75 | 27 438 | 177 | 1 111 | 53 | 14 | 5 | 15 | 1 | 37 / 26 / 12 |
| drive | 70 | 18 953 | 75 | 1 168 | 45 | 14 | 11 | 2 | 3 | 32 / 31 / 7 |
| calendar | 50 | 19 950 | 111 | 972 | 47 | 6 | 6 | 12 | 0 | 20 / 23 / 7 |
| forum | 48 | 8 688 | 170 | 783 | 68 | 18 | 0 | 1 | 0 | 34 / 13 / 1 |
| chat | 45 | 16 359 | 61 | 794 | 1 | 3 | 2 | 2 | 2 | 22 / 17 / 6 |
| paintsharp | 43 | 112 889 | 116 | 1 397 | 19 | 28 | 2 | 1 | 12 | 23 / 12 / 8 |
| maps | 39 | 13 416 | 22 | 539 | 1 | 2 | 2 | 1 | 8 | 27 / 10 / 2 |
| app | 38 | 12 386 | 108 | 918 | 3 | 3 | 2 | 1 | 0 | 20 / 14 / 4 |
| books | 37 | 9 326 | 60 | 328 | 28 | 13 | 5 | 2 | 1 | 22 / 14 / 1 |
| contacts | 37 | 7 086 | 51 | 508 | 1 | 6 | 3 | 1 | 0 | 30 / 7 / 0 |
| tasks | 26 | 6 974 | 37 | 285 | 32 | 4 | 1 | 2 | 0 | 16 / 10 / 0 |
| flow | 24 | 7 398 | 23 | 404 | 0 | 3 | 1 | 1 | 1 | 14 / 7 / 3 |
| media | 23 | 16 650 | 91 | 1 080 | 41 | 19 | 1 | 1 | 2 | 7 / 10 / 6 |
| code | 19 | 5 700 | 8 | 234 | 12 | 2 | 0 | 3 | 1 | 12 / 6 / 1 |
| assistant | 18 | 3 827 | 20 | 234 | 1 | 3 | 0 | 1 | 1 | 14 / 4 / 0 |
| forms | 18 | 6 414 | 57 | 555 | 16 | 4 | 2 | 2 | 2 | 7 / 8 / 3 |
| build | 15 | 9 000 | 4 | 138 | 0 | 5 | 0 | 0 | 1 | 7 / 6 / 2 |
| wiki | 12 | 1 906 | 41 | 184 | 18 | 9 | 0 | 2 | 0 | 9 / 3 / 0 |
| photos | 10 | 3 451 | 10 | 118 | 3 | 5 | 0 | 1 | 0 | 6 / 3 / 1 |
| notes | 9 | 4 264 | 9 | 130 | 2 | 3 | 0 | 1 | 0 | 5 / 3 / 1 |
| keestore | 3 | 1 936 | 5 | 98 | 0 | 2 | 0 | 1 | 0 | 1 / 1 / 1 |
| p2pnas | 3 | 1 140 | 1 | 128 | 0 | 0 | 0 | 0 | 0 | 1 / 1 / 1 |
| **Total** | **1 234** | **≈ 563 000** | **≈ 2 900** | **≈ 23 000** | **≈ 650** | **≈ 203** | **≈ 100** | **≈ 97** | **71** | **652 / 467 / 115** |

¹ files using a canvas context, Tiptap/ProseMirror, Monaco, Leaflet/MapLibre, three.js, xterm or Yjs.

What this says, and what it means for the design:

- **Layout is bespoke, not primitive.** Only ~12 % of JSX elements are `@ui` primitives; the rest is Tailwind-styled
  `div`/`span`/`button`/`p` (5 400 `className=` in core alone, 1 791 arbitrary hex colours like `text-[#202124]`,
  4 288 `style={{…}}`). A `.kbview` vocabulary made only of today's `@ui` components cannot express these screens:
  **layout and text elements have to be added** (Stack, TableLayoutPanel, Label/Text styles, ScrollArea, GroupBox…).
- **Primitives are few and concentrated**: `Button` (784), `Input`, `Dropdown`, `Callout` (215 each), `Badge`,
  `ConfirmDialog`, `MenuDropdown`, `Spinner`, `Checkbox`, `Toggle`. The long tail (DataTable: 2 uses, Accordion: 3)
  is not yet adopted.
- **Copy-pasted helpers are everywhere**: a local `SettingsRow` in **20 repos**, a local `RadioGroup` in **20 repos**,
  `Section` in 12, four separate `Ribbon.tsx` copies (office, app, build, flow — the "no cross-module import" rule
  makes modules vendor them). These are the first shared elements to create; migration removes the copies.
- **State is React-local**: 4 894 `useState`, 1 302 `useEffect`, 3 532 `.map(` list renderings, ~650 React Query
  calls, a few zustand stores per module plus the shell stores of `@kubuno/sdk`. The code-behind model must keep
  hooks usable, not forbid them.
- **i18n is i18next**: 13 languages (2 RTL: `ar`, `he`), module dictionaries in `i18n.ts` objects
  (`registerModuleTranslations`), **10 141 inline `defaultValue:`** fallbacks in French.
- **Accessibility is hand-written**: 816 `aria-*` and 229 `role=` attributes; the view format must carry them.
- **No SSR**: the host is a Vite SPA (`index.html` + inline import map); nothing hydrates or server-renders the UI
  (`hydrateRoot` is only re-exported by the shared `react-dom` shim; the `renderToString` hits are all KaTeX). **SSR is not needed** and is out of scope.
- **Modules are runtime-loaded ESM** (`loadRemoteModules.ts`): `entry.js` exports `register()` + `sdkVersion`; shared
  specifiers (`react`, `@ui`, `@kubuno/sdk`, `@kubuno/drive`, `zustand`, React Query, i18next, Radix menu) are
  `external` and resolved by the host's **import map** (`build/importmap-plugin.ts`) to unique instances. The npm
  packages `@kubuno/sdk` and `@kubuno/drive` are **type surfaces whose `index.js` throws** — only `@kubuno/ui` ships
  real code. Every `@ui` component is already registered under a string key (`themed('ui.Button', …)` in
  `ComponentRegistry`), which is a ready-made name → component indirection.
- **The desktop vocabulary is already a port of the web one**: desktop `Button` has the web variants
  (`Primary/Secondary/Ghost/Text/Danger/TextDanger`, `Sm/Md/Lg`); `DockArea`, `DockPanel`, `WorkspaceShell`, `MenuBar`
  and `FloatingWindow` were ported *from* the web (`DESKTOP-MIGRATION.md`); the desktop icon set is Lucide like
  `lucide-react`. Convergence is natural; the differences are mostly names and the children model.
- **A live-capture caveat** (found while checking the verification method): two consecutive headless Chrome
  screenshots of the live login page (`http://192.168.1.220:8080`, 1440×900) differ — the left panel is an animated
  WebGL background. Pixel-parity checks must freeze or mask animations (§7).

## 1. Goals and constraints

### 1.1 Goals

1. **One view language for desktop and web.** Same file extension (`.kbview`), same grammar (the lossless rowan
   tree of `kubuno-desktop-views`), same markup extensions (`{Binding}`, `{Res}`), same `x:Name`, same `On*` event attributes,
   same element ids for the designer, and — wherever the control exists on both sides — **the same element and
   property names and the same enum values**. On the web an element maps to an `@kubuno/ui` (or `@kubuno/sdk`)
   React component; on the desktop to a `kubuno-desktop-views` registry component. A view file belongs to **one target**
   (its code-behind is TypeScript or Rust); what is shared is the vocabulary, the tooling and the skills, not files.
2. **WinForms-like authoring in Visual Studio**: Toolbox → design surface → Properties (F4) → ⚡ double-click creates
   a handler in the code-behind, F7/Shift+F7, the `.kbview` buffer as the single source of truth, edits surgical
   (`DESIGNER.md` §2), every gesture one undo unit.
3. **Migrate the existing web UI screen by screen**, never big-bang, with visual parity checked on the live
   instance.

### 1.2 Constraints and how each is met

| Constraint | Answer (details in the referenced section) |
|---|---|
| **Code-behind in TypeScript** | A class per view extending a generated base (`ViewBase`), the TS counterpart of `#[kubuno_desktop::view]`: a typed field per `x:Name`, handlers as plain methods, `@bind accessor` fields for bindings, an optional `use()` method where React hooks are allowed (§2.2). |
| **Binding to stores and React Query** | `{Binding Path}` resolves against the view instance, then its data context. Hooks (`useQuery`, zustand selectors, `@kubuno/sdk` stores) run in `use()` and land in `@bind` fields; the common query/mutation cases are declarative non-visual components `<Query>`/`<Mutation>` (the web analogue of `DATA.md`'s `BindingSource`/`TableAdapter`) (§2.2). |
| **Events** | `On*` attributes named by the registry (`OnClick`, `OnCheckedChanged`, `OnTextChanged`, `OnSelectionChanged`…), the same names as the desktop; the runtime adapts React props (`onClick`, `onChange(checked)`) into `(sender, e)` with typed args (`MouseEventArgs`, `CheckedChangedEventArgs`…), `e.handled`/`e.cancel` mapped to `stopPropagation`/`preventDefault` (§2.2). |
| **i18n with `.kbres` / `{Res}`** | `.kbres` (neutral + satellites `name.fr.kbres`) becomes the source of module strings; the Vite plugin compiles it to i18next bundles passed to `registerModuleTranslations`, so the runtime stays i18next (language switch, RTL, the 13 languages). `{Res key}` resolves through i18next's `t(ns:key)`. Extension needed on both targets: arguments and plurals, `{Res notes_selected_count, Count={Binding SelectedCount}}` (§2.4, WV-6). |
| **Theming and module accent** | No colour literals in views (the desktop rule, `XML_VIEWS.md` §1): colours are theme tokens (`ForeColor="TextSecondary"`, `Surface="Card"`), mapped to the CSS variables of `theme.css`. The module accent keeps coming from `[data-module="<id>"]` set by `ModuleArea` — a view inside a module inherits it, nothing to do. Tailwind-only styling is allowed during migration through a web-only `Class` attribute, flagged by the language server (open question Q3). |
| **Performance** | The production path is compiled (no XML parser shipped); bindings are precompiled accessors; nodes subscribe to the paths they read (fine-grained re-render, better than many hand-written screens that re-render whole pages on every `useState`); `Repeater` keyed and optionally virtualised. Budget: runtime ≤ 15 KB gzip; no regression in React Profiler commit time on the pilot screens (§2.1). |
| **SSR** | Not needed (checked, §0). The runtime may assume `window`/`document`. |
| **Accessibility** | WinForms' own property names on every control: `AccessibleName`, `AccessibleDescription`, `AccessibleRole` → `aria-label`, `aria-describedby`, `role`; `TabIndex` keeps DOM order (a positive value is a language-server warning on the web); diagnostics for an icon-only button without `AccessibleName`, an image without `AlternateText`, a form field without a label (§5). The accessibility tree is part of the parity check (§7). |
| **Coexistence with TSX** | A compiled `.kbview` **is a React component** (default export of its code-behind) — it drops into `RouteRegistry`, `WidgetRegistry`, sidebar/toolbar/right-panel stores unchanged; a TSX screen embeds a view with `<NotesSettingsPage …/>` or `<KbView view={…}/>`; a view embeds any React component registered as a custom control, or through `<ReactHost>` (§2.3). |
| **`@kubuno/*` external singletons, import map** | The compiled module imports only `@ui`, `@kubuno/sdk`, `@kubuno/views` (new), `react/jsx-runtime`, `lucide-react` (bundled, as today). `@kubuno/views` becomes a new shared specifier in the host import map (one instance of the binding engine, design-mode hooks and control-handle registry), like `@ui` (§6.4). |
| **RTL** | `Dock="Left"` on the web means *start* (logical properties: `inset-inline-start`, grid areas mirror) — a view written for `fr` is correct in `ar`/`he` without change; the desktop keeps physical edges until it gets an RTL story. |

### 1.3 Non-goals

- Rendering web views on the desktop or desktop views on the web (each target keeps its renderer).
- Rewriting the canvas editors (office documents/spreadsheet/slides/math, PaintSharp, maps, Monaco, xterm, media
  player, flow diagrams): they become **custom controls** used from views (§6.1, class C).
- Migrating the host shell (`core/shell`: AppSidebar, header, ModuleArea, routing, module loading): it stays TSX
  until the end, if ever; it *hosts* views.

## 2. Architecture

### 2.1 Interpreter, compiler, or hybrid

| | (a) Runtime interpreter | (b) Build-time compiler (Vite plugin → TSX/JS) | (c) Hybrid (**recommended**) |
|---|---|---|---|
| What ships | XML text + parser + interpreter | JS module of `jsx()` calls | Precompiled **plan** (IR object literal) + typed accessor functions + one shared IR renderer |
| HMR | Re-parse, keep state: trivial | Vite module HMR; state lost unless carefully handled | `.kbview` change = new plan swapped into the live instance (state and control handles kept by `x:Name`/element id, like the desktop hot reload); code-behind change = prototype swap (§2.2) |
| Type-checking bindings/handlers | None (strings) | Full (generated TS) | Full: the plugin emits typed accessors and a check file per view, type-checked by `tsc -b` |
| Bundle size | + parser (~30–60 KB) per page | Smallest per view, no shared runtime | Runtime ~10–15 KB gzip once (shared chunk), plans are small; parser only in dev/designer |
| Debugging | Stack traces into the interpreter | Source maps to `.kbview` | Source maps for every accessor/handler dispatch to the attribute in `.kbview`; React DevTools shows `View(NotesSettingsPage) > Button#save` |
| Designer (unsaved buffer) | Natural: parse the buffer | Needs a compile per keystroke | The designer parses the buffer in the browser (same parser, WASM) and feeds the **same renderer** |
| Divergence risk | — | Designer would need its own renderer → two code paths | One renderer; the only difference is *how a binding is evaluated* (compiled accessor vs path walker), covered by a conformance suite rendering every test view both ways and diffing the DOM |

**Recommendation (c)**, which is the desktop's own choice transposed (`XML_VIEWS.md` §2: the interpreter is
canonical, the build step validates and adds typed sugar): **one renderer** used in production and in the
designer; the build step parses, validates against the web registry, generates types, precompiles accessors and
plans, and emits source maps.

**One grammar, compiled to WASM.** The parser is the Rust rowan parser of `kubuno-desktop-views` (lossless CST, `ast`,
`validate`, `edit`, element ids). It is factored out of the Windows-only crate (WV-1) and compiled to WebAssembly in
an npm package `@kubuno/views-compiler` (Node for the Vite plugin, browser for the design surface). A TypeScript
re-implementation would be a second grammar drifting from the first — exactly what `XML_VIEWS.md` §6 forbids. (This
is unrelated to the desktop's abandoned wasmtime components: here WASM is a build-time/designer library, never a
runtime of the product. Open question Q5.)

**What the Vite plugin produces for `NotesSettingsPage.kbview`:**

- a JS module exporting `plan` (the IR: element names resolved to `@ui` imports, literal props already converted,
  binding slots, event slots, element ids) and `ViewBase` (the generated base class, §2.2);
- precompiled accessors `(vm) => vm.prefs.font` and setters for two-way bindings, each with a source-map segment to
  its attribute;
- static imports of the Lucide icons named literally (`Icon="Save"` → `import { Save } from 'lucide-react'`, tree
  shaken); a bound icon name uses the `ICON_MAP` of `@kubuno/sdk`;
- `.kubuno/views/NotesSettingsPage.kbview.d.ts` (types of `ViewBase`: one field per `x:Name`, abstract handler
  signatures) and `.kubuno/views/NotesSettingsPage.check.ts` (binding paths written as TS expressions against the
  code-behind type) — generated, gitignored, the web equivalent of `OUT_DIR`; never committed (the `ARCHITECTURE.md`
  rule). `npm run typecheck` becomes `kbview-tsc -b`: `tsc -b` plus remapping of errors in check files back to
  `.kbview` line/column (Vue's `vue-tsc` approach).

**Specifier constraints.** The generated module imports `@ui` (never `@kubuno/ui`; the existing `tsconfig.paths`
mapping applies), `@kubuno/sdk`, `@kubuno/views` and `react/jsx-runtime` — all already in every module's `external`
set except `@kubuno/views`, which joins `SHARED` in each module's `vite.config.ts` and `SPECIFIER_TO_CHUNK` in the
host's import-map plugin (chunk `kubuno-desktop-views`). Inside the core build the same specifiers resolve by alias, as
today. No module ever bundles the renderer: one binding engine, one design-mode registry, one control-handle store.

### 2.2 The code-behind model in TypeScript

Three files, as on the desktop (`PROGRAMMING-MODEL.md` §1):

```xml
<!-- NotesSettingsPage.kbview -->
<Stack Direction="TopDown" Gap="0" DesignWidth="960" DesignHeight="640">
  <SettingsRow Label="{Res notes_pref_font}" Description="{Res notes_pref_font_desc}">
    <RadioGroup SelectedValue="{Binding prefs.font, Mode=TwoWay}">
      <RadioButton Value="sans"  Text="{Res notes_pref_font_sans}"/>
      <RadioButton Value="serif" Text="{Res notes_pref_font_serif}"/>
      <RadioButton Value="mono"  Text="{Res notes_pref_font_mono}"/>
    </RadioGroup>
  </SettingsRow>
  <SettingsRow Label="{Res notes_pref_autosave}">
    <Switch On="{Binding prefs.autosave, Mode=TwoWay}" Text="{Res notes_pref_autosave_on}"/>
  </SettingsRow>
  <Stack Direction="LeftToRight" Gap="12" Padding="0, 20, 0, 0">
    <Button x:Name="save" Text="{Res notes_settings_save_changes}" Loading="{Binding busy}" OnClick="save_click"/>
    <Button Text="{Res common_cancel}" Variant="Ghost" OnClick="cancel_click"/>
  </Stack>
</Stack>
```

```ts
// NotesSettingsPage.ts — the code-behind (Form1.cs)
import { bind, type MouseEventArgs, type Button } from '@kubuno/views'
import { useSaveShortcut } from '@ui'
import { ViewBase } from './NotesSettingsPage.kbview'
import { useModulePrefs } from './userPrefs'

export class NotesSettingsPage extends ViewBase {
  @bind accessor prefs: NotesPrefs = DEFAULT_PREFS
  @bind accessor busy = false
  private saved = DEFAULT_PREFS
  private update!: (p: NotesPrefs) => Promise<void>

  /** Runs on every render; the only place hooks are allowed (React's rules apply). */
  use() {
    const p = useModulePrefs<NotesPrefs>('notes', DEFAULT_PREFS)
    this.saved = p.prefs; this.update = p.update
    useSaveShortcut(() => void this.save_click(), !this.busy)
  }

  async save_click(_sender?: Button, _e?: MouseEventArgs) {
    this.busy = true
    try { await this.update(this.prefs); this.save.text = this.t('notes_settings_saved') }
    finally { this.busy = false }
  }

  cancel_click() { this.prefs = this.saved }
}
export default NotesSettingsPage.component()   // a React component: RouteRegistry.register('notes/settings', …)
```

| WinForms / desktop (`PROGRAMMING-MODEL.md`) | Web |
|---|---|
| `#[kubuno_desktop::view("main_view.kbview")]` on the struct | `class X extends ViewBase` where `ViewBase` is imported from `./X.kbview` |
| `Form1.Designer.cs` / the macro's in-memory expansion | the Vite plugin's generated module + `.kubuno/views/X.kbview.d.ts` |
| `initialize_component()` | done by `X.component()` on mount: the instance is created once per mounted view (`useRef`), its control handles linked by `x:Name`, initial property values read from the plan (`this.save.text === "Enregistrer…"` before the first paint) |
| a field per `x:Name` (`status: kubuno_desktop::forms::TextField`) | a typed handle per `x:Name` (`save: Button` from `@kubuno/views`): `text`, `enabled`, `visible`, `checked`, `value`, `focus()`, `click`… — setting one re-renders only that element (each handle is a slot in the view's store, the same idea as the desktop's composed `__kb.N.Text` bindings) |
| `#[bind] user_name: String` | `@bind accessor userName = ''` (a standard TC39 accessor decorator — TS 6, no `experimentalDecorators`); assignments notify the bound nodes |
| `#[data_context]` | `dataContext` property (any object, or a `<Query>` result) for paths not found on the instance |
| handler = plain method, `match` generated by name | handler = plain method; the generated `ViewBase` declares each handler the view names as **abstract**, so a missing handler is a `tsc` error at the attribute (the counterpart of rustc E0599); a method with fewer parameters is accepted (TS assignability = the desktop's several handler shapes) |
| `async fn f(ui: UiHandle<Self>)` | `async` methods; `this` stays valid (the instance outlives renders); writes after unmount are dropped with a dev warning |
| `Form_Load`, `FormClosing` | `OnLoad` (after first commit), `OnUnload` (unmount) on the root; `OnShown` on dialogs |
| `MessageBox.Show` | `MessageBox.show(...)` from `@kubuno/views`, implemented with `ConfirmDialog`/`useConfirm` (never `window.confirm` — CLAUDE.md rule) |
| `form.show_dialog(owner)` | `await Dialog.show(SomeDialogView, props)` → `DialogResult` (on top of `FloatingWindow`/`ConfirmDialog`) |

**Class rather than hooks-only — the recommendation, with the alternative.** A hooks-only model (`defineView(view,
() => ({ …handlers, …state }))`) is more idiomatic React, but: (1) the designer's "double-click → create handler"
must insert a method into a well-defined place — a class body is the same operation as the desktop's `impl` block,
while an object literal returned from a closure is fragile to locate and to rename; (2) typed fields per `x:Name`
and abstract handler checks need a nominal type to attach to; (3) the WinForms mental model (and Claude's skills) is
shared with the desktop. Hooks remain first-class through `use()`. Open question Q2.

**Declarative data (the common React Query cases):**

```xml
<Query x:Name="locations" Key="weather-locations" Fetch="load_locations"/>
<Mutation x:Name="addLocation" Run="add_location" Invalidates="weather-locations"/>
<Repeater ItemsSource="{Binding locations.data.locations}" ItemKey="id"> … </Repeater>
<Spinner Visible="{Binding locations.isLoading}"/>
```

`Fetch`/`Run` name code-behind methods (like handlers); `locations: QueryHandle<T>` and `addLocation:
MutationHandle<A, R>` are typed fields; a handler calls `await this.addLocation.run(r)`. They are non-visual
components shown in the designer's component tray, exactly like `Timer`/`BindingSource` on the desktop. A query with
reactive keys binds them: `Key="calendar-events, {Binding rangeStart}, {Binding rangeEnd}"`.

**HMR.** `.kbview` saved → the plugin re-emits the plan; the running instance swaps its plan, keeps `@bind` state and
handles (by `x:Name`, else element id); elements whose `x:Name` vanished lose their handle state (the desktop
`FocusRing` rule). Code-behind saved → the module re-executes; the host component swaps the live instance's prototype
(`Object.setPrototypeOf(instance, New.prototype)`), so new methods take effect without losing field values. A view
that does not compile keeps its last good plan and shows an overlay with file/line/column (the Vite overlay) — the
desktop's "keep the last good tree" rule.

**Expressions.** No expression language in attributes (XAML's choice, and the desktop's). Logic goes to getters in
the code-behind (`get canSave() { return !this.busy && this.dirty }` → `Enabled="{Binding canSave}"`); formatting to
`StringFormat` and `{Res}` arguments. The codemod turns `{cond && <X/>}` into `Visible="{Binding <getter>}"` plus a
generated getter (§6.2). This keeps views designable and diffs small.

### 2.3 Embedding both ways (screen-by-screen migration)

- **A view inside TSX**: a compiled view is a component — `<NotesSettingsPage noteId={id}/>`, typed props from
  the view's `Props` (declared on the root: `<Stack x:Props="NotesSettingsPageProps">`, read in code as
  `this.props`). `<KbView view={NotesSettingsPage} …/>` is the same for dynamic cases.
- **A React component inside a view (custom control)**: registered with metadata, the web counterpart of
  `#[derive(Component)]` (`EVENTS.md` §4.3):

  ```ts
  // controls.ts — scanned by the language server (Toolbox, Properties, completion) and imported by the plugin
  export const NoteCard = defineControl(NoteCardImpl, {
    category: 'Notes', icon: 'StickyNote', defaultEvent: 'OnOpen',
    props:  { Note: { kind: 'object', bindable: true }, Selected: { kind: 'Bool', default: false } },
    events: { OnOpen: { prop: 'onOpen', args: 'EventArgs' }, OnColorChanged: { prop: 'onColor', args: 'ValueChangedEventArgs<string>' } },
    children: 'None',
  })
  ```

  Used as `<NoteCard Note="{Binding item}" OnOpen="card_open"/>`. A user control is a `.kbcontrol` (VIEWS-SPEC.md §1.1) with a code-behind
  (`UserControl` root), usable as an element by its file stem, as on the desktop (`<Repeater ItemTemplate="NoteRow">`).
- **Unregistered component (migration escape hatch)**: `<ReactHost Component="./NoteEditor#NoteEditor"
  Props="{Binding editorProps}"/>` — rendered for real in the designer (it is real React), no Properties rows beyond
  `Props`. The codemod emits it for subtrees it cannot convert; the language server reports a "migration debt" count
  per repo.

### 2.4 Bindings, resources, layout on the web

- **Binding grammar** is the desktop's (`binding.rs`): `{Binding Path[, Mode=OneWay|TwoWay][, StringFormat=…]
  [, FallbackValue=…][, Source=name]}`; `Source=` names a `<Query>`/data component (`DATA.md` §7). Two-way
  bindings use the registry's per-property change event (`Checked` ↔ `onChange(checked)`, `Text` ↔
  `onChange(e.target.value)`).
- **Lists**: `<Repeater ItemsSource=… ItemKey=… [ItemTemplate=…] [Virtualize="true"]>` with the desktop semantics
  (`XML_VIEWS.md` §3 as built: row, then user control, then page scope); `DataTable`/`ListBox`/`Dropdown` take
  `ItemsSource` too.
- **`{Res}`**: `{Res key[, Source=set]}`; web extension `{Res key, Count={Binding n}, Name={Binding user.name}}` →
  i18next interpolation/plurals (the `.kbres` model gains plural forms `key_one`/`key_other` — also useful on the
  desktop). Design-time culture switch = the designer's language picker (`DesignLanguagePicker`), sent as
  `setResources` (same message as the desktop).
- **Layout**: the web is responsive, the desktop's default root is an absolute WinForms surface (`DESIGNER.md` §14).
  On the web, **flow is the default** (`Stack`, `TableLayoutPanel` over CSS grid, `Panel` with `Dock` over CSS grid
  areas), and `X/Y/Anchor` are honoured only inside an explicit absolute container (`Panel Layout="Absolute"`).
  Responsive values use attached suffixes on size classes shared with the mobile roadmap (`ARCHITECTURE.md`):
  `Direction="TopDown" Direction.Expanded="LeftToRight"`, `Visible.Compact="false"` (Compact < 640 px ≤ Medium <
  1024 px ≤ Expanded; `MOBILE_MAX_WIDTH`/`useIsMobile` of `@ui` aligned on it). Open question Q4.
- **Children → props adapters**: web components that take data arrays (`Tabs tabs[]`, `Accordion items[]`,
  `Breadcrumb items[]`, `Stepper steps[]`, `Dropdown options[]`, `MenuDropdown items[]`) are fed from child elements
  (`TabItem`, `AccordionSection`, `BreadcrumbItem`, `Step`, `Option`, `MenuItem`) by a registry-declared adapter
  (`childrenToProp: 'tabs'`), so the markup is the desktop's; slots (`Card` `actions`/`footer`) use the
  property-element syntax already adopted on the desktop (`<Card.Actions>…</Card.Actions>`).

## 3. Element mapping

Canonical `.kbview` names = **the desktop names** (they are already in the grammar, the registry, the Toolbox and the
docs); the web registry maps each to its React component (open question Q1). "—" = missing on that side.

| `.kbview` element | Web (`@ui` / `@kubuno/sdk`) | Desktop (`kubuno-desktop-views`) | Notes / work to converge |
|---|---|---|---|
| `Button` | `Button` | `Button` | Identical variants and sizes. `Text` → children, `Icon` → `icon`. |
| `IconButton` | `Button` (icon only) | `IconButton` | Web: `AccessibleName` required (diagnostic). |
| `CheckBox` | `Checkbox` | `CheckBox` | |
| `RadioButton` | `Radio` | `RadioButton` | |
| `RadioGroup` | — (20 local copies) | — (`RadioButtons` proposed in `XML_VIEWS.md` §7) | **Add on both** (`SelectedValue` two-way). |
| `Switch` | `Toggle` | `Switch` | `On` ↔ `checked`. |
| `Slider` | `RangeSlider` | `Slider` | |
| `NumericField` | `NumberInput` | `NumericField` | |
| `TextField` | `Input`, `OutlinedField` | `TextField` | Web `Variant="Outlined"` selects `OutlinedField`; `LeftIcon`/`RightIcon` → add on desktop. |
| `TextArea` | `Textarea` | `TextArea` | Web mentions → custom control. |
| `SearchField` | — (shell search bar, `Input` + icon) | `SearchField` | Add on web. |
| `MaskedField` | — (`PhoneField` partial) | `MaskedField` | Add on web. |
| `Dropdown` + `Option` | `Dropdown` (`options[]`) | `Dropdown`/`Option` | Adapter children → `options`. |
| `ComboBox` | `Combobox` | `ComboBox` | |
| `DatePicker` | `DatePicker`, `DateField` | `DatePicker` | |
| `MonthCalendar` | inline `DatePicker` panel | `MonthCalendar` | Web: expose the inline panel. |
| `ColorField`, `GradientField` | same | same | |
| `Label` | — (raw `p`/`span` + Tailwind) | `Label` | **Add on web** with `TextStyle="Body|Caption|Title|Heading|Overline"` tokens — replaces most of the 23 000 HTML tags. |
| `LinkLabel` | `Link` / `a` | `LinkLabel` | Web: `NavigateTo` (SPA route) or `Href`. |
| `Badge`, `Spinner`, `ProgressBar`, `Separator`, `Callout`, `EmptyState`, `Stepper`/`Step`, `Breadcrumb`/`BreadcrumbItem`, `Accordion`/`AccordionSection`, `Tabs`/`TabItem` | same names | same names | Adapters for the array-fed ones. |
| `Icon` | `lucide-react` via `ICON_MAP` | `Icon` | Same Lucide names (`ICONS.md`). |
| `Avatar`, `PictureBox` | Radix avatar (core), `img` | `Avatar`, `PictureBox` | Add `Avatar` to `@ui`; `PictureBox` → `img` with `AlternateText`. |
| `PaintBox` | — (canvas code) | `PaintBox` | Web: a `<canvas>` with an `OnPaint(sender, e: { ctx, width, height, dpr })` handler — small, useful for charts/previews. |
| `Panel` | `div` | `Panel` | Web: `Dock` → CSS grid areas (logical start/end), `Layout="Absolute"` for X/Y/Anchor. |
| `Stack` | `div.flex` | `Stack` | `Direction`, `Gap`, `Align`, `Wrap`, `Padding` (tokens or px). |
| `TableLayoutPanel` | `div.grid` | `TableLayoutPanel` | Web over CSS grid; responsive `Columns.Compact`. |
| `ScrollArea` | `div` overflow | `ScrollArea` | |
| `GroupBox` | local `Section` (12 repos) | `GroupBox` | Add on web. |
| `Card` | `Card` | `Card` | Slots via property elements. |
| `SettingsRow` | — (20 local copies) | — | **Add on both** (label + description + content), the single most duplicated helper. |
| `Splitter` | `ResizeHandle` + `useResizableWidth` | `Splitter` | Add element on web. |
| `Toolbar`/`ToolbarItem` | local toolbars + `useToolbarStore` | same | Add on web. |
| `Sidebar`/`SidebarSection`/`SidebarItem` | `SidebarNavItem` (sdk) + `useSidebarStore` | same | Add on web (sidebar bodies are a big migration target). |
| `StatusBar`/`StatusLabel` | `WorkspaceShell` status text | same | Add on web. |
| `Popover` | `AnchoredPopover` | `Popover` | |
| `ToolTip` (attached `ToolTip="…"`) | `Tooltip` wrapper | `ToolTip` component (extender) | One attached property on both. |
| `ContextMenu`/`MenuItem` | `MenuDropdown` (`items[]`), `useContextMenu` | same | Adapter; the CLAUDE.md rule "menus = `MenuDropdown`" is enforced by construction. |
| `FloatingWindow` | `FloatingWindow` | `FloatingWindow` | Desktop port of the web one. |
| `DockArea`/`DockPanel`/`WorkspaceShell`/`MenuBar` | `@kubuno/sdk` workspace | `DockArea`/`DockPanel`/`WorkspaceShell` | Desktop ported from web; `MenuBar` element missing from the desktop registry. |
| `ListBox`, `CheckedListBox`, `ListView`, `TreeView` | — (bespoke, 3 532 `.map`) | same | **Add on web** (virtualised, keyboard, ARIA listbox/tree). |
| `DataTable`/`Column` | `DataTable` (2 uses) | `DataTable`/`Column` | Align columns API. |
| `Repeater` | `.map` | `Repeater` | Web runtime. |
| `Timer` | `setInterval` | `Timer` | Non-visual, web runtime. |
| `Query`, `Mutation` | React Query | (`BindingSource`, `TableAdapter`) | Web-only non-visual components. |
| `Ribbon*`, `Command` | 4 local copies (office, app, build, flow) | `Ribbon` family (`RIBBON.md`) | **Promote the web ribbon to `@ui`** and align on `RIBBON.md`'s classes — removes three copies. |
| `UserControl`, `ReactHost` | component, — | `UserControl`, — | `ReactHost` web-only. |

**Web-only today (add to the desktop when needed, or keep web-only with a target flag):** `FieldGroup`,
`PhoneField`, `DateField`, `AddressField`, `LabelCombobox`, `LabelField`, `Editable`, `RichText`, `FloatCheckbox`,
`FontPicker`, `FontSizeField`, `ColorPicker`, `ColorSwatchPicker`, `GradientPicker`, `StartPage`, `KubunoLogo`,
`LabelIcon`, `HelpBubble`, `Collapse`, `ConfirmDialog`/`ConflictDialog` (dialog views on both), `Toast`,
`MobileSheet`, `SpinnerOverlay`.

**Desktop-only today:** `PaintBox` (proposed above), `MaskedField`, `SearchField`, list controls, `Timer`,
`StatusBar`, `Toolbar`, `Sidebar` — all proposed for the web above.

**Convergence mechanism**: a shared spec checked by tests, not by discipline. Both registries export the same JSON
shape (`DESIGNER.md` §5 export); a conformance test (in CI of `core` and of `desktop`) diffs the web
`kbview-registry.json` against the desktop export: same element → same property names, kinds, enum values, events;
differences must be listed in an allowlist with a reason (`target: web-only`). The language server reports a
target-only element/property used on the other target.

## 4. The VS designer for web views

### 4.1 Reuse, and the layering move first

Everything except the surface is reused: the editor factory with Design | XML | Split (`DesignerSplitView`),
`IDesignSurfaceHost` (a buffer-text-shaped seam, `setText`/selection/`EditRequested`), the editing pipeline
(`Editing/*`, one `ITextEdit` per request, compound undo), the Properties publisher (`KbviewElementObject`, an
`ICustomTypeDescriptor` built from registry JSON — no CLR type per component), the Events tab
(`KbviewEventBindingService` → `kubuno/createHandler`), the native Toolbox installer (tabs per registry family, icons
rasterised from the Lucide XAML), collection editors (`ChildCollectionPlanner`), smart tags, the resource picker
(`.kbres`), the icon picker, F7/Shift+F7, the document outline (`documentSymbol`).

All of it lived in `Kubuno.Desktop`, and the web layer may not reference the desktop layer
(`tests/Kubuno.Architecture.Tests`). Per `ARCHITECTURE.md` ("code moves DOWN"), **WV-8 moves the view designer into a
shared lower layer** — `Kubuno.Views` / `Kubuno.Views.Logic` between Rust and the targets — leaving
`RustDesignSurfaceHost`, the design build of `kubuno_ui.dll` and the desktop templates in `Kubuno.Desktop`; the web
layer adds `WebDesignSurfaceHost`. The same move is already planned for the mobile renderer.

**Done 2026-10-02 (WV-8).** `src/Views/Kubuno.Views` holds the designer (`Designer`), the `.kbview` language client
(`LanguageService`, `Locating`, `Options`...) and the `.kbres` editor (`Resources`), started by its own `ViewsLayer`
(listed before the targets); `Kubuno.Views.Logic` (netstandard2.0) the view file kinds and the `.kbres` model; their
tests are `tests/Kubuno.Views.Tests`. The seam is `IProtocolDesignSurfaceHost : IDesignSurfaceHost` (the protocol
events and commands the designer used to take from `RustDesignSurfaceHost` by a type test: drops, edit batches,
context menus, double-clicks, unhandled keys, `select`/`selectMany`, `projectComponents`, `format`, zoom, design
culture) plus the optional `IDesignSurfaceStatusAware` / `IDesignSurfaceRuntimeAware`; the design-surface protocol
helpers (`DesignSurfaceProtocol`, `…DragDropProtocol`, `…ContextMenuProtocol`, `…ResourcesProtocol`, `…ThemeProtocol`,
`…ZoomProtocol`) moved with it, so `WebSurfaceProtocol` can reuse them. `Kubuno.Desktop` keeps `RustDesignSurfaceHost`
(+ factory, Win32 helpers, the `kubuno_desktop_ui` runtime handshake), the per-project design runtime, the Data Sources binding
drop and the Kubuno control icons (handed to the Toolbox through `NativeToolboxInstaller.IconName`/`IconAssembly`); it
plugs its factory into `DesignSurfaceHostFactoryHost`. `Kubuno.Web` references `Kubuno.Views` (never `Kubuno.Desktop`,
checked transitively by `tests/Kubuno.Architecture.Tests`). Left for WV-9b: a factory chosen per document instead of
the one `DesignSurfaceHostFactoryHost.Current`, and a runtime description that is not a `kubuno_ui` DLL hash.

### 4.2 The WebView2 design surface

`WebDesignSurfaceHost : IDesignSurfaceHost`, a WPF `WebView2` control (Microsoft.Web.WebView2.Wpf; the Evergreen
runtime ships with Windows 11 and Visual Studio), user data folder under `%LOCALAPPDATA%\Kubuno\vs-webview2\<VS
instance>`. No cross-process `SetParent` dance as with `view_embed.exe` (DSG-7): WebView2 owns its child process and
its HWND hosting.

**What the page is.** The desktop rule "the surface renders with the project's own `kubuno_ui.dll`"
(`DESIGNER.md` §15) becomes: **the surface renders with the project's own packages**. The page is served by the
project's **own Vite dev server** in design mode (the `.esproj` already knows how to start it, `docs/WEB.md`): the
kbview plugin adds a route `/__kubuno_design__/` serving a design entry that imports the module's controls
(`controls.ts`), code-behinds and views, `@kubuno/views` in design mode, and an import map that resolves the shared
specifiers to **a real host runtime**. Because `@kubuno/sdk`/`@kubuno/drive` on npm are type-only stubs, the real
runtime must come from somewhere:

- **recommended**: a new npm package `@kubuno/host-runtime`, published with each core release, containing the
  host's shared chunks (`kubuno-shared` = real `@ui` + `@kubuno/sdk`, `drive-shared`, vendor React/zustand/Query/
  i18next/Radix) and a minimal shell context (stores initialised, no routing), version-locked to the `@kubuno/sdk`
  types the module builds against;
- alternative: load the shared chunks from a configured dev core (`http://192.168.1.220:8080/assets/…` through its
  import map): always matches a live server, but the designer then needs the network and a running core (open
  question Q4).

Fallback when the project is not set up (no `node_modules`, dev server failing): a design host bundled in the VSIX
renders built-in elements only, with an info bar "Aperçu : runtime intégré — exécutez `npm ci` …" (the desktop's
*NotBuilt* state and **Générer** button, same UX).

**Handshake.** First message from the page: `{"type":"surfaceInfo","version":1,"target":"web","views":"0.1.0",
"ui":"0.1.12","sdk":1,"hostRuntime":"0.1.12"}` — the host refuses a surface whose `views` ABI differs from the
compiler's, as `CheckSurfaceInfo` does for the DLL hash.

### 4.3 Design mode in the browser

Implemented in `@kubuno/views` (design build only, tree-shaken from production):

- **Element ids** are the desktop's (`Element::stable_id`: dot path of element-child ordinals, `""` = root),
  computed by the same parser; every rendered element's root DOM node gets `data-kb-id`. The registry declares how to
  reach a component's root node (`domRoot: 'ref' | 'wrapper'`); components that do not forward refs get a
  layout-neutral wrapper in design mode only.
- **Layout map** = `getBoundingClientRect()` of every `data-kb-id`, rebuilt on commit, `ResizeObserver` and scroll;
  hit-test = deepest id under the point (`document.elementsFromPoint`), same reverse-paint-order rule as
  `LayoutMap::hit_test`.
- **Input neutralisation**: an overlay layer captures every pointer and key event while design mode is on (the
  desktop's `neutralize_frame`): widgets never receive clicks, focus or typing; handlers never run.
- **Adorners** drawn in the overlay: hover stroke, selection frame 3 DIP outside the bounds, grab handles (filled
  when resizable — only absolute children and sized elements), parent dashed outline, faint outlines around
  invisible containers (`setDesignOptions.containerOutlines`), insertion markers per container kind: a line between
  `Stack` children (perpendicular to `Direction`), a cell highlight in `TableLayoutPanel` (drop sets `Row`/`Column`
  attached properties), dock bands for `Panel Dock`, free XY with snaplines only in `Layout="Absolute"`.
- **Viewport toolbar** (web-specific): width presets (390 / 768 / 1280 / free), size class, `ltr`/`rtl`, Kubuno light
  /dark theme, module accent (`data-module`), design-time language; the canvas around the page follows the VS theme
  (`setCanvasBackground`).

### 4.4 The C# ↔ JS protocol

Exactly the desktop wire shapes (`kubuno_desktop_views::protocol` / `DesignSurfaceProtocol`), carried over
`CoreWebView2.PostWebMessageAsJson` (host → page) and `window.chrome.webview.postMessage` (page → host) instead of
stdin/stdout lines, so `DesignSurfaceEditingCoordinator` is shared unchanged:

| Direction | Messages (unchanged) | Web additions |
|---|---|---|
| host → surface | `setText {text, baseDir}`, `setDesignMode`, `select`, `selectMany`, `format`, `dragEnter/dragOver/drop/dragLeave`, `projectComponents`, `setDesignOptions`, `setCanvasBackground`, `setResources {culture, sets}` | `setViewport {width, height, sizeClass, dir}`, `setKubunoTheme {mode, module}`, `setDataMode {mode: "sample"\|"live"}` |
| surface → host | `surfaceInfo`, `selectionChanged {id, bounds, ids}`, `editRequest {op}`, `editRequests {ops, gesture}`, `dropTargetChanged`, `contextMenu {x, y, screenX, screenY, elementId, menu}`, `command`, `doubleClick` | `unhandledKey {key, modifiers}` (from `AcceleratorKeyPressed` + a page key hook: Ctrl+Z/Y/S, F4, F7 reach VS), `surfaceError {file, line, column, message}` |

Every edit still goes **surface → intent (`EditOp`) → VS → `kubuno/applyEdit` in the language server → `{range,
newText}` → the buffer** (`DESIGNER.md` §3: "designer = gesture → intent; language server = intent → text"); the page
never writes text. `contextMenu` screen coordinates come from `window.screenX` + DIP scale, converted by the host.

**Toolbox drag into WebView2 (spike first, WV-9a).** OLE drags with the private format `Kubuno.Views.ToolboxItem` do
not surface in HTML `DataTransfer`. Plan: the Toolbox data object also carries `CF_UNICODETEXT` =
`kubuno-toolbox:Button`, which Chromium exposes as `text/plain` in `dragover`/`drop`; the page converts it to the
same `dragEnter/dragOver/drop` flow. Fallback if Chromium filters it: an `IDropTarget` registered on the WebView2's
child window (as the desktop surface did when OLE skipped `devenv`'s target, `DESIGNER.md` §11). Double-click insertion
(`ToolboxInsertionPlanner`) needs no drag and works from day one.

### 4.5 Registry export from the web side

- Metadata is authored next to the components, one table per family (`core/frontend/src/ui/kbview/*.meta.ts`), the
  counterpart of the desktop's `component!` family tables, with a **compile-time link the desktop lacks**: each table
  `satisfies` a type derived from the component's `Props`, so a renamed prop breaks `tsc`.
- `npm run build:registry` (core) emits `kbview-registry.json` into `@kubuno/ui` (primitives) and `@kubuno/sdk`
  (workspace, sidebar…), in the **exact JSON shape of `kubuno/registry`** (`registry/export.rs`: snake_case,
  `PropKind` as serde enum, `layout_kind`, `allowed_children`, `doc_fr`, `category`, `bindable`, `localizable`,
  `editor`, `default_event`) plus a `web` block per element (`module: "@ui"`, `export: "Button"`, `propMap`,
  `eventMap`, `childrenToProp`, `domRoot`) that only the compiler and runtime read.
- Project controls (`defineControl` in `controls.ts` and user-control `.kbview`s) are scanned by the language server
  from source (like EVT-7b's `scan_source`), so they appear in the Toolbox before any build; `projectComponents` sends
  them to the surface, which renders the real component (it is loaded by the dev server) or a labelled placeholder
  while it fails to compile.
- VS asks the language server `kubuno/registry` exactly as today — Toolbox tabs, Properties rows, ⚡ events and
  smart tags need no web-specific code.

### 4.6 Sample data vs live data

- **Sample (default)**: design-time attributes in the `d:` namespace, ignored by the compiler in production (XAML's
  `d:DataContext`/`d:DesignData` precedent): `d:DataContext="{SampleData NotesSettingsPage.sample.json}"`, `d:Text`,
  `d:ItemsCount="5"` on a `Repeater`. `<Query>` in design mode returns its `d:Sample` (or an empty, loaded state).
  Deterministic, offline, safe.
- **Live (opt-in toggle)**: the dev server proxies `/api/v1/*` to the configured core (Tools > Options > Kubuno >
  Web), the WebView2 profile keeps the session cookie after a one-time sign-in in the surface; `<Query>` and `use()`
  hooks fetch real data. Because input is neutralised, no handler or mutation can run in design mode; `Mutation.run`
  throws in design mode as a second guard.
- **Run mode** (design mode off) makes the surface interactive for a quick try, like the desktop surface.

## 5. Language server: one `kubuno-views-ls`, two target profiles

**Recommendation: share `kubuno-views-ls`.** Same grammar, element ids, `applyEdit`/`elementAtOffset`/
`rangeOfElement`, completion, hover, outline, `{Res}` support (`kubuno-desktop-resources-model`), diagnostics framework — a
second server would duplicate 7 200 lines and drift.

- **Profile selection** per document: walk up from the `.kbview` — `Cargo.toml` depending on `kubuno-desktop`/`kubuno-desktop-views`
  → `desktop`; `package.json` depending on `@kubuno/views-compiler` → `web`; an explicit `kubuno.views.json`
  (`{"target": "web"}`) wins.
- **Registry provider**: `desktop` = the compiled-in registry (today); `web` = `node_modules/@kubuno/ui/
  kbview-registry.json` + `@kubuno/sdk`'s + `@kubuno/views`' + scanned project controls. Requires the registry *model*
  (types + JSON loader) to be split from the Windows-only `kubuno-desktop-views` (WV-1).
- **Code-behind provider**: `desktop` = `syn` (today: form classes, `#[bind]`, `impl` methods); `web` = an
  `oxc_parser`-based reader of the same-stem `.ts` (class extending `ViewBase`, its methods, `@bind accessor` fields,
  `defineControl` calls). Features ported to it: handler set, missing-handler and incompatible-signature warnings,
  ⚡ compatible-handlers dropdown, `kubuno/createHandler` (inserts `save_click(sender: Button, e: MouseEventArgs):
  void { }` into the class, adds the type imports from `@kubuno/views`), rename/remove of untouched stubs,
  go-to-definition from `OnClick="save_click"` / `{Binding prefs.font}` / an element name into the TS sources,
  `kubuno/bindingPaths` for the Properties binding picker (fields, accessors and getters of the class).
- **Diagnostics added for the web profile**: element/property not available on this target; colour literal or
  `Class` (warnings, counted); accessibility rules (§1.2); `X/Y/Anchor` outside an absolute container; positive
  `TabIndex`; `Query` without `Key`.
- **Types stay with TypeScript**: the Rust server does structural checks; type errors in bindings and handlers come
  from `kbview-tsc` (generated check files remapped to `.kbview` positions), surfaced in VS's Error List through the
  TypeScript project system. The `.kbview.d.ts` files are regenerated by the language server on change (debounced)
  so `this.save.` completes in the `.ts` editor without a running dev server — the same generator code as the Vite
  plugin (shared Rust, WASM on the Node side).
- **Limit to accept**: a rename started from the TS editor (tsserver's F2) does not update `.kbview` attributes; the
  rename started from the designer or the `.kbview` does both. A tsserver plugin can close this later.

## 6. Migrating the existing web code

### 6.1 Classification

| Class | What | Volume (estimate from §0) | Treatment |
|---|---|---|---|
| **A — straightforward** | Settings pages, admin panels, dialogs, forms, simple lists, sidebar bodies, filter panels, widgets | core admin (189 files) + settings (≈ 97 files across repos) + ≈ 100 dialogs + most of contacts, tasks, forum, wiki, notes, keestore, p2pnas, photos | Codemod + review; needs only the core element set (WV-5a). |
| **B — needs new elements** | Lists with templates and drag-reorder, master/detail, navigation trees, toolbars, workspaces, ribbons, data tables, popovers | drive and mail lists, chat lists, calendar side panels, forms builder, media library, books, app/build/flow chrome | Codemod after WV-5b elements exist; `ReactHost` for leftovers. |
| **C — custom control** | Canvas/editor surfaces and third-party widgets | 71 heavy files: office editors (22), PaintSharp (12), maps (8), Monaco (code), xterm, Tiptap composers (mail, notes), media player, flow canvas, calendar Day/Week/Month/Year grids, 3D | Stay TSX, wrapped with `defineControl` (Toolbox entry, properties, events); the chrome around them (ribbons, panels, dialogs, settings) migrates as A/B. |
| **Host shell** | `core/shell`, routing, module loading, theme engine, `@ui` primitives themselves | ≈ 33 shell files + `src/ui` | Not migrated (the primitives *are* the controls). |

### 6.2 Codemod (`@kubuno/views-migrate`, ts-morph)

Input: one `.tsx` file. Output: `X.kbview` + `X.ts` code-behind + `.kbres` entries + a report.

Automated:
- `@ui` elements → `.kbview` elements by reverse registry lookup (`propMap`, lower-case enums → PascalCase,
  `children` → `Text` or content);
- Tailwind layout on intrinsic elements → layout elements by a token table (`flex flex-col gap-2` → `<Stack
  Direction="TopDown" Gap="8">`, `grid grid-cols-2 gap-4` → `<TableLayoutPanel Columns="2" Gap="16">`, `text-sm
  text-text-tertiary` → `<Label TextStyle="Caption">`, `p-5` → `Padding="20"`); unmatched classes go to `Class=`
  and are counted; arbitrary hex colours mapped to the nearest token when exact, else left in `Class` and reported;
- `t('key', { defaultValue })` → `{Res key}`; the default value is added to the neutral `.kbres` when missing;
  `registerModuleTranslations` dictionaries → `.kbres` satellites (key-preserving, so nothing else changes);
- `useState` → `@bind accessor`; `setX(v)` → `this.x = v`; hooks (`useQuery`, stores, custom hooks, `useEffect`) →
  moved verbatim into `use()` (semantics preserved; turning them into `<Query>` is a later, manual improvement);
- inline arrow handlers → methods named `<x:Name or role>_<event>` with the body copied; `{cond && …}` → `Visible`
  + getter; `{list.map(x => …)}` → `<Repeater>` with an inline template when the body converts, else an
  `ItemTemplate` custom control;
- local helper components (`SettingsRow`, `RadioGroup`, `Section`) → the shared elements when the shapes match.

Manual (reported, file by file): refs and imperative DOM access, portals, render props, `dangerouslySetInnerHTML`,
context providers, complex derived JSX, drag-and-drop (`@dnd-kit`), animation code. These subtrees are extracted as
a component and referenced with `<ReactHost>` so the file still converts and renders identically.

Each converted file goes through the parity check (§7) before its TSX is deleted; the report gives per repo:
converted elements %, `Class` count, `ReactHost` count, TODOs.

### 6.3 Order

1. **Foundations in core** (WV-1…WV-7): the runtime is served by the host, so core comes first, but only its
   plumbing — no core screen yet.
2. **Pilot: notes** (9 `.tsx`, 4 264 LOC): `NotesSettingsPage`, `NotesFilterPanel`, `NotesSidebarBody`,
   `NotesMiniPanel`, `NotesRecentWidget` (A/B), `NoteCard` as a custom control first, `NoteEditor` kept as
   `ReactHost`. Small, exercises the full chain (module build, externals, import map, `.kbpkg`, registries, i18n with
   13 languages, the designer on a real module).
3. **Calendar settings** (12 settings files) then **contacts** and **tasks** (CRUD lists): validate the codemod's
   automation rate on class A at scale and the list elements.
4. **Core admin + core settings** (≈ 230 files, the largest class-A block): bulk codemod, reviewed by section
   (`adminNav.ts` order); core changes ship in core releases, so batch them.
5. **Remaining modules by increasing difficulty**: forum, wiki, books, photos, keestore, p2pnas, assistant → mail,
   drive, chat, media, forms → maps, code, flow, app, build (chrome only around custom controls) → office and
   PaintSharp chrome (ribbon promoted to `@ui`, dialogs, panels), editors untouched.

### 6.4 Versions, compatibility, publishing

- **New packages**: `@kubuno/views` (types; runtime served by the host through the import map, its `index.js` a
  throwing stub like `@kubuno/sdk`), `@kubuno/views-compiler` (real code: Vite plugin, WASM parser, `kbview-tsc`,
  codemod CLI; a devDependency), `@kubuno/host-runtime` (designer only, §4.2). `@kubuno/ui` gains new elements and
  `kbview-registry.json` (minor bumps); `@kubuno/sdk` gains the sidebar/toolbar elements' metadata.
- **Handshake**: a module using views exports `viewsAbi` next to `sdkVersion`; `loadRemoteModules` rejects a
  mismatch and records it (`reason: 'views-mismatch'`) like `sdk-mismatch`. `SDK_VERSION` does not change — the
  additions are additive — unless an `@ui`/`sdk` export is renamed or removed (existing rule).
- **Ordering**: a host providing `@kubuno/views` must be deployed before modules using it. A module built against a
  newer runtime on an older host fails at ES link time (unknown specifier) — the loader already records that as
  `import-error` and the bell shows it. `module.toml` gains an optional `[frontend] views_abi = 1` so the core can
  refuse to install a `.kbpkg` the running host cannot load (better than a runtime failure).
- **Flow (mode publié)**: core release (tag + `npm publish` of the four packages — **by the user**, `!` prefix, ≤ 3
  tags per push) → `_tools/bump_npm_floors.sh` in the modules → module migration commits → `release.sh` per module.
  `_tools/check_versions.py` learns the new packages and the `viewsAbi`/`views_abi` agreement. Every touched repo gets
  its `CHANGELOG.md` `[Unreleased]` entry (English).
- **Desktop side**: new shared elements (`RadioGroup`, `SettingsRow`, plurals in `.kbres`) are added to the desktop
  registry in the same lot as the web, keeping the conformance test green.

## 7. Lots

Sizes: S ≤ 2 days, M ≤ 1 week, L ≤ 3 weeks (one agent). "Visual" = must be checked on screen, compiling is not
enough.

| Lot | Content | Size | Depends on | Main risk | Verification |
|---|---|---|---|---|---|
| **WV-0** | Freeze the shared spec: canonical names (Q1), code-behind model (Q2), styling rule (Q3), layout/size classes (Q4); `kbview-registry.json` schema = `kubuno/registry` + `web` block | S | — | Decisions reopened later | Review of this note |
| **WV-1** | Split platform-neutral crates out of `kubuno-desktop-views`: `kubuno-desktop-views-syntax` (lexer, rowan CST, `ast`, `edit`, element ids, validator core) and `kubuno-desktop-views-model` (registry types + JSON loader); `kubuno-desktop-views` re-exports, no behaviour change | M | WV-0 | Churn in a crate other agents edit daily (`git status` shows concurrent work) — do it in one short window, re-export everything | `cargo test` of `kubuno-desktop-views` and `kubuno-views-ls` unchanged; desktop designer smoke test |
| **WV-2** | `@kubuno/views-compiler`: WASM build (Node + browser), Vite plugin (plan + accessors + source maps + `.d.ts` + check files + HMR), `kbview-tsc` | L | WV-1, WV-4 (fixture registry is enough to start) | WASM toolchain in module CI (offline `.kbpkg` builds: the package ships the `.wasm`, no Rust needed downstream) | Golden tests (view → plan), `tsc` errors remapped to `.kbview` lines, HMR keeps state (vitest + browser) |
| **WV-3** | `@kubuno/views` runtime in core: renderer, binding engine (one/two-way, `StringFormat`, `{Res}` via i18next), events → args, `ViewBase`/`component()`/`@bind`/handles, `Repeater`, `Query`/`Mutation`, `Timer`, `KbView`, `defineControl`, `ReactHost`, `MessageBox`/`Dialog`; import-map entry, `viewsAbi` handshake | L | WV-2 | Re-render granularity and React 19 semantics (StrictMode double mount, concurrent rendering) | Conformance suite (compiled vs interpreted DOM equal), React Profiler budget, bundle ≤ 15 KB gzip |
| **WV-4** | Web registry: `*.meta.ts` per family for the existing `@ui`/sdk components, `build:registry`, conformance test vs desktop export + allowlist | M | WV-0 | Metadata drifting from props — mitigated by `satisfies Props` | Test in core CI; JSON diff report |
| **WV-5a** | Core element set on web: `Label`/text styles, `Stack`, `TableLayoutPanel`, `Panel` (dock grid, absolute), `ScrollArea`, `GroupBox`, `SettingsRow`, `RadioGroup`, `Icon`, `PictureBox`, `LinkLabel`, children→props adapters (Tabs, Accordion, Breadcrumb, Stepper, Dropdown, ContextMenu); same names added on the desktop where missing | M | WV-3 | Visual drift from the existing hand-written screens | **Visual**: gallery page per element, light/dark, ltr/rtl, 390/1280 px |
| **WV-5b** | List & navigation elements: `ListBox`, `CheckedListBox`, `ListView`, `TreeView` (virtualised, ARIA), `DataTable` alignment, `Toolbar`, `Sidebar*`, `StatusBar`, `Splitter`, `Popover`, `SearchField`, `MaskedField`, `Avatar`, `PaintBox`; Ribbon promoted to `@ui` on `RIBBON.md`'s classes | L | WV-5a | Ribbon unification touches 4 modules | **Visual** + keyboard/screen-reader pass (NVDA) on lists and trees |
| **WV-6** | i18n: `.kbres` → i18next bundles in the plugin, `{Res}` arguments and plurals (both targets), converter `i18n.ts` → `.kbres` | M | WV-2 | 13 languages × keys: a lossy conversion would be silent | Round-trip test: every key/language identical before/after; RTL screenshot |
| **WV-7** | Language server web profile: profile detection, registry from JSON, `oxc` code-behind reader, `createHandler`/rename/remove/compatible handlers for TS, go-to-definition into `.ts`, web diagnostics, `.d.ts` generation | L | WV-1, WV-4 | `oxc` version churn; TS edits must respect user formatting (insert, never reformat) | Unit + round-trip tests (as the 127 + 18 existing); live in VS |
| **WV-8** | VS: move the designer down to `Kubuno.Views*` (shared layer), desktop keeps its surface host; architecture tests updated | M | — (can start now) | Concurrent edits in `Kubuno.Desktop/Designer` (uncommitted work today) — coordinate, move files without changing behaviour | Designer test suite (343) green; desktop designer smoke test in the experimental instance. **Done 2026-10-02** (section 4.1) |
| **WV-9a** | **Spike**: WebView2 in a VS document pane — focus/keyboard routing with VS accelerators, Toolbox drag into WebView2 (`text/plain` channel vs child-window `IDropTarget`), DPI 100/175 %, VS theme switch | S–M | — | Highest risk of the plan (like DSG-7 for desktop) | **Visual**, real mouse in the experimental instance (outside the agent sandbox; synthetic input is dropped inside it) |
| **WV-9b** | `WebDesignSurfaceHost`: protocol bridge, `surfaceInfo`, design host via the project's dev server, bundled fallback + info bar, `@kubuno/host-runtime` package | L | WV-8, WV-9a, WV-3 | Dev-server lifecycle inside VS (ports, crashes, restarts) | **Visual**: open a view, select, edit property, undo; kill the dev server → fallback |
| **WV-10** | Design mode in the browser: overlay, hit-test, adorners, insertion markers per layout, absolute move/resize, viewport toolbar, sample/live data, run mode | L | WV-3, WV-9b | Components that do not expose a DOM root; portals (menus, popovers) outside the page tree | **Visual** per container kind; hit-test unit tests in jsdom |
| **WV-11** | Codemod `@kubuno/views-migrate` (ts-morph): conversions of §6.2, `ReactHost` extraction, report | L | WV-5a, WV-6 | Over-promising automation: measure on the pilot before committing to the bulk | Converted output compiles, renders, parity check passes |
| **WV-12** | Pilot **notes** (+ templates "Kubuno Web View" item, web module template with a `.kbview` main view, `.kbview` default editor in `.esproj` via CPS, `.ts` nested under `.kbview`) | M | WV-2…WV-11 | First real use reveals gaps — budget for fixes in earlier lots | Parity check on all notes screens; designer: open, edit, ⚡ create handler, F5 |
| **WV-13…** | Rollout per §6.3, one lot per repo (or per core admin section); each lot ends with parity check + `.kbpkg --install` test + CHANGELOG | S–L each | WV-12 | Long tail of B/C screens | Same method |

**What must be done first**: WV-0 (decisions), then **WV-1 and WV-8 in parallel** (both are refactorings with no
product change, and both collide with ongoing desktop work if delayed), **WV-9a** (the spike that can invalidate the
surface plan) in parallel with **WV-4**; WV-2/WV-3 next.

**Parity verification method (every migrated screen).**
- Before/after on the live instance `http://192.168.1.220:8080` with headless Chrome over CDP: sign in through the
  API once (admin of `Z:\src\CLAUDE.md` §9, or a dedicated test account), reuse the cookie; fixed viewports (1440×900,
  1280×800, 390×844), light and dark, `fr`, `en` and `ar` (RTL); `prefers-reduced-motion: reduce`, frozen clock
  (`Emulation.setVirtualTimePolicy` + a fixed `Date`), fonts loaded before capture, canvases/animated regions masked
  (the login page's WebGL background made two consecutive captures differ on 2026-10-01).
- Pixel diff with a small tolerance (anti-aliasing), plus a DOM-structure-independent check: the accessibility tree
  (`Accessibility.getFullAXTree`) and the computed text content must match; interaction smoke (tab order, Enter/Space
  on buttons, a form submit) scripted per screen.
- "Before" = the currently installed module; "after" = `build_kbpkg.sh --install` (the real distribution path), then
  `deploy_local.sh --frontend` while iterating.
- VS checks per lot: the view opens in the designer with the project's runtime, selection ⇄ XML sync, a property
  edit round-trips, ⚡ creates a TS handler that `kbview-tsc` accepts, F7/Shift+F7, Toolbox drop.

## 8. Prior art (what we take, what we avoid)

| System | Model | Taken | Avoided |
|---|---|---|---|
| **XAML / WinUI** | XML + `x:Class` partial class, XAML compiler generates `InitializeComponent` and named fields; `x:Bind` compiled, type-checked bindings; `d:` design-time data; designer + hot reload | Code-behind class, fields per `x:Name`, compiled bindings (precompiled accessors), `d:DataContext`, property-element syntax, no expressions | Resource dictionaries/free styles (tokens only); `Grid` star sizing on desktop (web uses CSS grid via `TableLayoutPanel`) |
| **Angular templates** | HTML templates + class, AOT compiler, strict template type-checking through generated TS, language service | Type-check by generating TS (check files) | Expression micro-language in attributes |
| **Vue SFC** | `<template>` compiled to render functions; `vue-tsc`/Volar "virtual code" for types and source maps | Error remapping to the source file, source maps per binding | Single-file blending (we keep view and code-behind separate, like the desktop) |
| **Svelte** | Compiler, no runtime VDOM | Build-time work, small runtime | A compiler-only model (the designer needs an interpreter path) |
| **Blazor `.razor`** | Markup + `@code` or `.razor.cs` partial class, compiled to C# | Code-behind file pairing and nesting in VS | Razor's inline code blocks |
| **Power Apps (YAML source)** | Declarative controls + Power Fx formulas, designer-first | Designer as a first-class editor of a text format meant for diff/review | A formula language in properties |
| **Builder.io / Plasmic** | Visual editors over registered React components with input metadata (`registerComponent`) | Custom controls = React components + metadata (`defineControl`); live React rendering in the editor | JSON content stored outside the repo; codegen that owns files |
| **Craft.js** | React editor framework: resolver map, serialised node tree, connectors for drag/select | The resolver idea (registry name → component), selection overlay over real components | A JSON tree as the source of truth (ours is the text buffer, edited surgically) |

## 9. Open questions for the user

1. **Canonical element names** — desktop names (`Switch`, `TextField`, `CheckBox`, `Slider`, `NumericField`,
   `TextArea`, `RadioButton`, `ComboBox`, `Popover`) for both targets, with the web registry mapping them to
   `Toggle`, `Input`, `Checkbox`, `RangeSlider`, `NumberInput`, `Textarea`, `Radio`, `Combobox`, `AnchoredPopover`?
   **Recommended: yes** — they already exist in the grammar, Toolbox, docs and desktop views; React component names
   stay as they are.
2. **Code-behind model** — a class extending the generated `ViewBase` (handlers as methods, `@bind accessor`, hooks
   in `use()`), or hooks-only functions? **Recommended: the class**, for parity with `#[kubuno_desktop::view]`, reliable
   handler insertion by the designer and typed `x:Name` fields.
3. **Free styling on the web** — allow a web-only `Class="…"` (Tailwind) attribute during the migration, flagged and
   counted by the language server, or tokens only from day one? **Recommended: allow it with warnings and a per-repo
   budget that must reach zero for class A screens** — tokens-only from day one would block the codemod on 23 000
   hand-styled elements.
4. **Layout and design runtime** — (a) flow by default on the web with absolute positioning only in `Panel
   Layout="Absolute"`, responsive values by size-class suffixes shared with the future mobile renderer; (b) the
   designer renders through the project's own Vite dev server plus a new npm package `@kubuno/host-runtime` (real
   `@ui`/`sdk` singletons), with live data as an opt-in toggle against a configured core. **Recommended: (a) yes,
   (b) yes** — the alternative for (b), loading the shared chunks from a running core, makes the designer depend on
   the network and a live server.
5. **WASM for the parser** — compile the existing Rust `.kbview` parser to WebAssembly (`@kubuno/views-compiler`) so
   the web uses the same grammar, rather than writing a TypeScript parser? **Recommended: WASM** — a build/design-time
   library only, never a product runtime (unlike the abandoned desktop WASM components).
6. **Scope** — editors (office, PaintSharp, maps, Monaco, xterm, media player, calendar grids) stay React custom
   controls permanently, and the host shell stays TSX? **Recommended: yes**; only their chrome (ribbons, panels,
   dialogs, settings) migrates.


## 10. Decisions (2026-10-01)

The user accepted recommendations 1–5 as written (desktop element names on both targets; class code-behind;
web-only `Class` tolerated with warnings and a per-repo budget that must reach zero; flow layout + absolute only on
request + size-class responsive values, designer through the project's Vite dev server and `@kubuno/host-runtime`;
the Rust parser compiled to WASM for build/design time).

**Scope (question 6) — rejected: everything migrates.** The host shell migrates to views as well, and so do the
editors (office, PaintSharp, maps, Monaco, xterm, media player, calendar grids): their whole UI becomes `.kbview`
views. Only the irreducible rendering/engine core of an editor (a canvas or document engine, a map renderer, a code
editor engine) may stay as code, and then strictly as a **designable custom control** registered in the element
registry (metadata, properties, events, design-time rendering), placed and configured from views like any other
element — never as an untracked TSX island. The lot plan must therefore include the shell and every editor, each
with its custom-control boundary defined explicitly.

**New components (2026-10-01).** Migrating everything implies creating new components. A dedicated lot, run before
and alongside the codemods, mines the existing TSX (pattern frequency across the 24 repos: e.g. the local
`SettingsRow` and `RadioGroup` copied in 20 repos, the 4 ribbon copies, list rows, cards, empty states, toolbars,
editor chrome) and promotes each recurring bespoke pattern to a first-class component in `@kubuno/ui` **and** its
desktop counterpart (`kubuno_desktop_ui` + `kubuno-desktop-views` element), same name and semantics on both sides, pixel-identical
to the current web rendering. Each new component lands with registry metadata (Toolbox, Properties, events,
design-time rendering), docs, and a gallery entry on both targets; publishing `@kubuno/ui` stays a user action.

**Migration order (2026-10-01).** The user set the order: the **core** web (host shell + admin + core screens) migrates
first, and the **first module ported after the core is drive** (not notes). The pilot role moves to the core; drive
follows immediately (note: the drive web UI largely lives in `core/frontend/src/drive`, alongside the `drive`
module repo). WV-12 and the rollout table must be re-ordered accordingly.

## Module isolation (user requirement, 2026-10-01) — normative

Kubuno web modules **never import functions, components or types from one another** in the classic programming sense
(no `import … from '<other-module>'`, no shared source, no project/package references between module repos). The only
shared code is the host-provided singletons resolved through the import map (`@kubuno/ui`, `@kubuno/sdk`,
`@kubuno/drive`, and — new — `@kubuno/views`), and cross-module collaboration goes exclusively through the core's
extension points (ExtensionRegistry, ModuleServiceRegistry, event bus, routes). Views must not break this:

1. **Generated code** (Vite plugin output, `.d.ts`, `ViewBase`) may only import the module's own files and the
   `@kubuno/*` host singletons. The compiler rejects (error, not warning) any element, control or code-behind that would
   resolve to another module.
2. **Element registry is layered and dynamic**: host elements (from `@kubuno/ui`/`@kubuno/views`) + the module's own
   custom controls/user controls, registered at **runtime** by the module when it loads (`registerElements()` through
   `@kubuno/views`), never statically linked from another module. A view can only use host elements and its own
   module's elements.
3. **Cross-module UI** uses an extension slot element, e.g. `<ExtensionSlot Point="drive.file-actions" …/>`, rendered
   by whatever modules contributed to that point at runtime (possibly none); the designer shows a placeholder listing
   the currently known contributors. No element of module A is ever placed directly in a view of module B.
4. **Visual Studio**: a multi-repo solution may group the core and several modules, but generates **no project or
   package reference between modules**; each module builds alone against the published `@kubuno/*` packages and the
   shared crates' git tags. The designer Toolbox for a module shows host elements + that module's own controls only
   (+ extension slots), never another module's controls.
5. **Desktop parity**: the same rule holds for desktop modules/apps — reuse goes through `kubuno_desktop_ui`/`kubuno-desktop-views`
   (the shared "host" layer) or through module services, never through one app's crate depending on another's.

## 11. WV-9a findings (2026-10-01) — WebView2 in a Visual Studio document pane

Spike, built and measured in the experimental hive `/rootsuffix KubunoWV` (Visual Studio 18, WebView2 runtime
154.0.4258.48, one monitor at 175 %), with real mouse and keyboard input (`mouse_event`/`keybd_event`: synthetic input
was **not** dropped in this session). **Verdict: the WebView2 surface plan holds**; every question has a working
mechanism, with the limits and risks listed at the end.

**Code** (all marked `SPIKE`, to be replaced by WV-9b's `WebDesignSurfaceHost`):

| Piece | Where |
|---|---|
| Protocol (desktop wire shapes + web additions), surgical markup edits, key routing table, profile folder | `src/Web/Kubuno.Web.Logic/WebDesigner/` (`WebSurfaceProtocol`, `WebSurfaceMessages`, `SpikeViewDocument`, `AcceleratorRouting`, `SpikeElementCatalog`, `WebView2Folders`), tested by `tests/Kubuno.Web.Tests/WebDesignSurfaceSpikeTests.cs` (23 tests) |
| Editor factory for `.kbwebspike` (Design = WebView2 surface, Code/F7 = code window on the same buffer), pane, WebView2 view, drop target, Toolbox tab, Properties object | `src/Web/Kubuno.Web/WebDesigner/Spike/`, registered by `KubunoPackage` (`[ProvideEditorFactory]`) and `WebLayer` |
| Test page (no network: served from `Kubuno.Web.dll` resources on `https://kubuno-surface.invalid/` through `WebResourceRequested`) | `src/Web/Kubuno.Web/WebDesigner/Spike/Assets/` (`index.html`, `surface.js`, `surface.css`, `font-probe.js`, `fonts/`) |
| Sample | `samples/web-design-spike/Sample.kbwebspike` |

**WebView2 assemblies**: Visual Studio's own `Common7\IDE\PrivateAssemblies\Microsoft.Web.WebView2.Core/Wpf.dll`
1.0.3967.48 (`devenv.exe.config` redirects 0.0.0.0–1.0.3967.48 to them), referenced with `HintPath` + `Private=False`:
never shipped in the VSIX, no binding conflict possible. One `CoreWebView2Environment` per process, profile in
`%LOCALAPPDATA%\Kubuno\webview2\<hive>` (here `18.0_dc9e2338KubunoWV`).

### Q1 — focus and keyboard routing

- **Mechanism.** Keys typed in the WebView2 go to the browser process; Visual Studio's message loop
  (`PreTranslateMessage`, `IVsWindowPane.TranslateAccelerator`, the key bindings) never sees them. WebView2 reports
  every *accelerator* key (Ctrl/Alt chords, F-keys, Del, arrows, Tab, Esc…) before the page (`AcceleratorKeyPressed`),
  which the WPF control raises as `PreviewKeyDown`/`KeyDown` (`Handled` = the page does not get it). The pane decides
  with `AcceleratorRouting` (the page keeps Tab/arrows/Enter/Esc/F2 for its own navigation, and every text-editing key
  while one of its text editors has the focus — the page reports that with `focusState`), then asks Visual Studio which
  command the key is bound to: `IVsFilterKeys2.TranslateAcceleratorEx` with `VSTAEXF_NoFireCommand` on a WM_KEYDOWN
  built from the key (hwnd = the WebView2 control's window), marks the key handled, and runs the command after the
  handler returned with `IVsUIShell.PostExecCommand` (the browser is blocked while the handler runs: no modal UI inside
  it). The commands then reach the pane's `IOleCommandTarget` (its `OleMenuCommandService`: Undo, Redo, Delete,
  ViewCode). Modifiers read with `GetKeyState` are right (the browser's windows are children of a devenv window, Windows
  attaches the input queues); WPF's `Keyboard.Modifiers` agreed in every case.
- **Measured** (log lines `key … -> Visual Studio: <command> posted`): Ctrl+S → `File.SaveSelectedItems` (file saved),
  Ctrl+Z → `Edit.Undo`, Ctrl+Y → `Edit.Redo` (the **buffer's** history: the pane gets it from
  `ITextBufferUndoManagerProvider`, every surface gesture is one `ITextBuffer` edit in one undo transaction, the page
  re-renders from the buffer), F4 → `View.PropertiesWindow`, F7 → `View.ViewCode` (opens the code window on the same
  buffer: the pane must handle `ViewCode` itself and open the Code logical view — the shell does not), Ctrl+Shift+B →
  `Build.BuildSolution`, Del → `Edit.Delete` (the pane removes the selected element, one undo unit), Ctrl+A →
  `Edit.SelectAll`. **Typing** reaches the page (plain characters are never accelerators): inline text editing typed
  "titrex", Backspace, Left, Del and Ctrl+A all handled by the page's editor, Enter committed one `setAttribute`.
- **Frame activation (bug found and fixed).** A click inside the WebView2 lands in a browser-process window, so Visual
  Studio's window manager does not activate the document frame: commands posted from the page went to the previously
  active window (Del did nothing, Ctrl+Z undid the Properties window's text box). Fix: `IVsWindowFrame.Show()` when the
  page reports a user gesture (`selectionChanged` on pointer down) or an accelerator arrives. **Not** on the
  controller's `GotFocus`: the browser re-takes the focus while Visual Studio switches windows (F7 opened the code
  window, then the design frame was re-activated over it).
- **Tab in and out.** Tab past the page's last element raises `MoveFocusRequested`, which the WPF control turns into a
  WPF `MoveFocus`; with `KeyboardNavigation.TabNavigation=Cycle` on the pane content, Tab leaves the page for the pane's
  toolbar (measured: focus on the first toolbar button), Tab ×4 comes back into the page, Shift+Tab leaves it the other
  way (`MoveFocusRequested Previous`). Frame activation (tab click, Ctrl+Tab) gives the WPF focus to the pane, which
  hands it to the WebView2.
- Not measured: `CoreWebView2ControllerOptions.AllowHostInputProcessing` (switch left in the spike:
  `KUBUNO_WEBVIEW2_HOST_INPUT=1`), which would route input through Visual Studio's loop first; multi-key chords
  (Ctrl+K, Ctrl+C): `TranslateAcceleratorEx` reports a chord start, the spike does not follow it.

### Q2 — Toolbox drag and drop into WebView2

Three channels tried with real mouse drags from the Visual Studio Toolbox:

| Channel | Result |
|---|---|
| OLE `IDropTarget` registered on the WebView2 control's own window (and on the in-process `Chrome_WidgetWin_0` under it), `AllowExternalDrop=false` | **Never called.** Registration succeeds (`hr=0`, `OleDropTargetInterface` prop present, the browser's windows then have none), but OLE does not look past the browser's cross-process windows under the cursor. Rejected. |
| HTML5 drag and drop: the Toolbox data object also carries `CF_UNICODETEXT` `kubuno-toolbox:<Name>`, seen as `text/plain` | **Works**: insertion marker during `dragover`, `drop` inserts the element. Limits: the text is only readable at drop time (protected mode), so the marker cannot depend on the item; needs `CF_UNICODETEXT` on every Toolbox item (the desktop installer's items only carry `Kubuno.Views.ToolboxItem`). |
| **Hybrid (chosen)**: Chromium only *detects* the drag (`dragenter` with `text/plain` → the page posts `toolboxDragDetected`); the host raises a layered (alpha 1/255, still hit-tested), `SS_NOTIFY` child window over the browser holding Visual Studio's `IDropTarget`, which takes the rest of the drag | **Works and robust**: overlay up ~12 ms after detection; `DragEnter` gets the real data object (formats seen: `Kubuno.Views.ToolboxItem`, `System.String`, `CF_UNICODETEXT`, `VSToolBoxItemInfo`, `VSToolboxItemPackageGuid`, drag-image formats), so the item is known during the drag; the page is driven with the desktop protocol's `dragEnter/dragOver/drop/dragLeave` (cursor → `ScreenToClient` → ÷ `devicePixelRatio` = page CSS px), it answers `dropTargetChanged` (drop effect) and posts one `editRequest insertChild`; the overlay hides on drop/leave (plus a timer safety net). |

Airspace: an **auto-hidden** tool window's flyout (the Toolbox) is drawn under the WebView2's window (its list was
invisible over the surface); docked/pinned tool windows are fine. Not exercised live: Toolbox double-click insertion
(`IVsToolboxUser.ItemPicked`, implemented).

### Q3 — selection sync and property edits

Click → `selectionChanged {id, ids, bounds}` → an `ICustomTypeDescriptor` built from the element's markup and the
catalog, published with `ITrackSelection` → the Properties window shows (Element), (Id), Text, Variant, Enabled…
(choices as drop-downs, booleans with their default when absent). Editing Text in the Properties window → one
`setAttribute` → one surgical buffer edit (`edit applied: Set Text (6 -> 0 chars at 165)`) → `setText` → the page
re-renders and keeps the selection; Ctrl+Z restores it. Edits keep every other byte (comments, quotes, indentation,
self-closing style): the inserted `<Button Text="Button"/>` took its siblings' indentation (F7 screenshot). The
protocol is the desktop's (`setText`, `select`, `setDesignMode`, `setCanvasBackground`, `selectionChanged`,
`editRequest` with `setAttribute|removeElement|insertChild|moveElement`, `dropTargetChanged`, `dragEnter…`,
`surfaceInfo` handshake) over `PostWebMessageAsJson` / `postMessage`, with web additions `setVsTheme`, `focusState`,
`unhandledKey`, `surfaceError`, `toolboxDragDetected` (and spike-only `setComponents`, `setDropChannel`,
`showFontSpecimen`, `metrics`, `log`). Messages whose source is not the page's origin are ignored.

### Q4 — DPI

At 175 %: page `devicePixelRatio` 1.75, page viewport × dpr = WPF control DIP × scale to ±1 physical px (2790 vs 2789,
3409 vs 3408), adorners on the elements. Per-monitor DPI change, **simulated** (one monitor; changing the system scale
would have disturbed the user's session): the controller's `RasterizationScale` forced to 1.0
(`ShouldDetectMonitorScaleChanges=false`) → the page sees dpr 1 (`matchMedia` change), re-lays out, adorners follow;
back to 1.75 likewise. The WPF control does not expose its controller (`m_webview2Base.CoreWebView2Controller`,
reflection — spike only). Not tested: a real 100 % session, a real move between monitors.

### Q5 — Visual Studio theme

`VSColorTheme.ThemeChanged` → `setVsTheme {mode, colors}` (canvas, text, accent, border, panel from
`EnvironmentColors`) + `CoreWebView2Profile.PreferredColorScheme`: switching Dark → Light in Tools > Options re-coloured
the open surface at once (canvas `#1F1F1F` → `#F5F5F5`), no reload.

### Q6 — lifecycle

- 40 open/close cycles through DTE: GDI objects flat (170), USER objects flat (85–87), private bytes 664 → 672 MB
  (~0.2 MB/cycle), process handles +7–8 per cycle (not attributed: no forced GC inside devenv); the WebView2 browser
  processes of the profile all exit when the last surface closes (0 left), so every open starts a browser: surface
  ready in 750–950 ms warm (the 85 s of the very first open were the frame waiting behind the start window).
- Hive restart: the document reopens with its surface. Runtime missing (`KUBUNO_WEBVIEW2_BROWSER_FOLDER` pointed at an
  empty folder — `WebView2RuntimeNotFoundException`): an info bar on the document plus the reason in the pane, no crash.

### Fonts (user requirement, 2026-10-01)

- **Production fonts** (`core/frontend/src/index.css`, `theme.css`, `ui/kbview/typography.ts`): Plus Jakarta Sans
  (variable 200–800, normal + drawn italic), Outfit (100–900), Roboto (100–900), DM Mono (400); stacks
  `--font-family-sans: "Plus Jakarta Sans", Outfit, Roboto, Arial, sans-serif`, `--font-family-mono: "DM Mono",
  "Fira Code", monospace`; body 13.5 px weight 500, `font-medium` → 600, buttons 500, roles 10.5/11.5/13.5/15.5/21.5 px,
  antialiased.
- **The spike ships byte-identical copies** of `core/frontend/public/fonts` (SHA-256 checked) for Jakarta (both),
  Outfit and DM Mono, declared exactly as `index.css`, with the production stacks, weights and sizes — no system UI
  face. Licences: `PlusJakartaSans-OFL.txt`, `Outfit-OFL.txt` (copied), `DMMono-OFL.txt` (OFL 1.1 text with the
  copyright line read from the font's own `name` table: core ships no licence text for DM Mono nor for Roboto). Roboto
  (Apache 2.0, third in the stack) is not shipped until core has its licence/NOTICE.
- **Verified identical**: the same probe block (`font-probe.js`) built in the live app (`http://192.168.1.220:8080`,
  its own CSS and `/fonts/`, injected over CDP into headless Edge 154.0.4258.37, nothing changed on the server) and in
  the surface: the 8 text lines' widths and heights equal to 0.01 px, every face `loaded`, and the rendered pixels
  **identical** (mean and max difference 0) at 175 % (676×302 px) and at 100 % (384×169 px, surface at
  RasterizationScale 1).
- **WV-9b / `@kubuno/host-runtime`** must serve exactly `WEB_FONT_FACES` (files + licence texts) at `/fonts/` with the
  host's CSS; the project's dev server does it for core (its `public/fonts`), the host runtime for modules; the VSIX
  fallback host bundles the same files. The designer page must load the host CSS, never declare its own stack.

### Module isolation and WV-9b

Per the module isolation rule (above): the surface of a module loads only the host singletons (`@kubuno/*`, the future
`@kubuno/views`) and that module's own controls, registered at runtime; the Toolbox lists host elements, the module's
own controls and `ExtensionSlot` placeholders, never another module's controls. The spike's catalog is host-only.

### Risks left for WV-9b

1. Airspace: auto-hide flyouts and any WPF popup over the surface are hidden by the WebView2 window — accept (docked
   tool windows) or evaluate `WebView2CompositionControl` (no HWND; cost: capture-based rendering, input forwarding).
2. Activation must follow every page gesture (pointer down, context menu, drag start), never the browser's focus events.
3. Multi-key chords and `AllowHostInputProcessing` unexplored.
4. Drops: every Toolbox item needs a format Chromium surfaces (`CF_UNICODETEXT`) for the detection step — a one-line
   change in the desktop installer; the overlay hides OLE's drag image while it is up.
5. The WPF control hides its controller (DPI handling, focus events): own `HwndHost` over
   `CreateCoreWebView2ControllerAsync`, or keep the reflection.
6. Start cost ~0.8 s per open since the browser exits with the last surface: keep a warm hidden controller if it matters.
7. Handle growth of ~8 per open/close to explain; a real 100 % session and a real monitor change to check.
8. Seen once before the activation fix and not re-checked: the first click on an inactive design frame selected the
   root instead of the clicked element.

## 12. WV-1 as built (2026-10-02)

Two platform-neutral crates were split out of `kubuno-desktop-views` (`desktop/windows/src/crates/`), both in the workspace
and in `Kubuno.Desktop.slnx` under `/Libraries/` (`.rsproj`, `Kubuno.Rust.Sdk/1.1.1`). Neither depends on
`kubuno_desktop_ui`, `kubuno_desktop_controls`, `windows` or any C library.

| Crate | Depends on | Holds |
|---|---|---|
| `kubuno-desktop-views-model` | `serde`, `serde_json` | `meta`: `PropKind`, `PropertyMeta`, `EventCategory`, `Routing`, `EventMeta`, `ArgsChain`, `LevelMeta`, `ChildrenModel`, `LayoutKind`, `DesignTimeAttribute`/`DESIGN_TIME_ATTRIBUTES`; `schema`: the export's wire types (`ComponentJson`, `PropertyJson`, `EventJson`, `PropKindJson`, `ChildrenModelJson`, `LayoutKindJson`, `RegistryExport`, `fnv1a_hex`); `json`: **new** loader `load_registry` → owned `RegistryDocument`/`ComponentEntry`/`PropertyEntry`/`EventEntry` (desktop export and web file, unknown fields ignored, `schema` > 1 rejected, `web`/`typography` kept raw); `editor`: **new** `EditorKind`; `binding_sources`: the binding source schema (`Shape`, `MemberKind`, `Member<L>`, `Context<L, U>`, `ConverterInfo<L>`, `Schema<L, U>`, `Resolution<'a, L>`), generic over the location types |
| `kubuno-desktop-views-syntax` | `rowan`, `kubuno-desktop-views-model` | `syntax` (lexer, parser, rowan CST, `Diagnostic`, `LineIndex`), `ast` (typed layer, `stable_id`/`resolve_id`), `edit` (all surgical edits, `match_line_endings`); `binding`: the `{Binding}` grammar (`BindingMode`, `UpdateSourceTrigger`, `BindingFormat`, `BindingPart`, `BINDING_KEYS`, `BindingIssue`, `binding_parts`, `canonical_key`, `is_binding_expr`, **new** `BindingSyntax`/`parse_binding_syntax`/`binding_issues`); `res`: the `{Res}` grammar (`RES_PREFIX`, `parse_res_path`, `res_reference`, `is_res_expr`); `ids`: `parent_id_of`, `is_ancestor_id`, `top_level_ids`; `markup`: `x:`/`d:` attributes; `view_kind`: `.kbview`/`.kbcontrol` rules (from `kubuno-views-ls`); `validate`: the registry-independent validator core (`COMMON_ATTRIBUTES`, `check_value`, `is_binding_expression`, `is_root_element`, `distance`/`closest`/`with_suggestion`) |

**What stayed in `kubuno-desktop-views`, and why.** `ComponentMeta` (its `build` field is a runtime `fn` returning a
`ViewNode`, and its methods read the runtime class chain `controls::class_of`), the registry *tables* (`COMMON_EVENTS`
and `VIEW_EVENTS` name runtime args types, `LEVELS`, the families), `BindingSpec` (it carries a run-time
`BindingState`; it is now built from `BindingSyntax`), the validator's registry walk (`validate`, `warnings`, `hints`,
`contrast_warnings`: they read `ComponentMeta`, the colour/font grammars of `style` and the icon set), and everything
runtime, rendering, Windows and design mode. A web consumer reads the registry through `json` (the export repeats
every inherited and root-only member on each element, so `ComponentEntry` needs no class chain).

**Paths kept.** `kubuno-desktop-views` re-exports `syntax`, `ast`, `edit` as modules and every moved item at its old path
(`registry::PropKind`, `events::ArgsChain`, `registry::export::ComponentJson`, `binding::BindingMode`,
`resources::RES_PREFIX`, `design::parent_id_of`…); `kubuno-views-ls` re-exports `view_kind`'s items and aliases the
binding source types to their LSP instantiation (`type Member = model::Member<lsp_types::Location>`…). No
downstream source changed (the facade `kubuno-desktop`, the macros, chat, shell, documents, drive, the gallery,
`view_embed`).

**Verified.** Tests, before → after: `kubuno-desktop-views` lib 745 → 673 (the 73 tests of `syntax`, `ast` and `edit` moved with their modules, +1 new: the model loader reads the real export back), its integration tests 7/16/7/1 and 24 doc-tests unchanged; `kubuno-desktop-views-syntax` 83 (73 moved + 10 new), `kubuno-desktop-views-model` 8 + 1 doc-test (new); `kubuno-views-ls` 156 + 1 + 18 unchanged; `kubuno-desktop-views-macros` 25 + 28 doc, `kubuno-desktop-views-meta` 14, `kubuno-desktop` 25 + 15 doc, `kubuno-chat` 19 unchanged. `kubuno-desktop` (shell) was being edited by another agent during this lot (78 → 82 tests; its new `signout_dialog::the_dialog_says_how_many_changes_wait` fails identically with the original `kubuno-desktop-views`, checked on a copy of the tree): 81 pass. Cross-checks of the two new crates (`cargo check --all-targets`): `wasm32-unknown-unknown`,
`x86_64-unknown-linux-gnu`, `x86_64-apple-darwin`, `aarch64-apple-darwin` all clean; `clippy -D warnings` clean on
Windows and on `wasm32-unknown-unknown`. Not done: the desktop designer smoke test in Visual Studio (no VS session in
this lot; the export and `view_embed` compile unchanged, and the export is now also read back by the model loader in a
test).

## 13. WV-2 as built (2026-10-02) — `@kubuno/views-compiler`

### 13.1 Where things live (repository placement — decision)

This note did not say how the core consumes the Rust grammar; the choice follows CLAUDE.md §3 ("published"
mode, tagged git dependencies, like `kubuno-seccomp` / `kubuno-storage`):

| Piece | Repository / path | Consumed by |
|---|---|---|
| Grammar (unchanged) | desktop `windows/src/crates/kubuno-desktop-views-syntax`, `-model` | everything below |
| **Web compiler** `kubuno-web-views-compiler-core` (new, platform-neutral: validation against a web registry, plan, `.d.ts`, check files, handle types) | desktop `windows/src/crates/kubuno-web-views-compiler-core` (workspace member, `.rsproj`, `Kubuno.Desktop.slnx`) | the WASM shim (git tag); later the language server's web profile (path, WV-7) — §5's "one generator, shared Rust" |
| WASM shim (C ABI, JSON in/out, no wasm-bindgen) | core `frontend/packages/views-compiler/wasm/` (own `[workspace]`, not a member of the core's Rust workspace) | `kubuno-web-views-compiler-core = { git = "https://github.com/kubuno/desktop", tag = "views-web-v0.1.0" }` |
| Built `.wasm` (≈ 474 KB) + `BUILD-INFO.json` | committed in `frontend/packages/views-compiler/wasm/`, shipped in the npm package | module builds: **no Rust toolchain, offline-able** |
| `@kubuno/views-compiler` (Vite plugin, `kbview-tsc`, browser entry) | core `frontend/packages/views-compiler/` (`dist/` committed, like `@kubuno/ui`'s) | modules (devDependency), the core host |

The compiler lives in the desktop repository next to the grammar it extends, and because the language server
(desktop repository) must reuse its TypeScript generators; the core only consumes a tag. Cargo's `[patch]`
cannot replace a git dependency whose tag does not exist yet, so `scripts/build-wasm.mjs --desktop <checkout>`
(or `KUBUNO_DESKTOP_DIR`) builds a copy of the shim with a path dependency; without it the shim builds from the
tag. Release flow: commit + tag `views-web-vX.Y.Z` on the desktop repository (user) → `npm run build:wasm` in
`frontend/packages/views-compiler` → commit the `.wasm` (and the shim's `Cargo.lock`) → publish
`@kubuno/views-compiler` and `@kubuno/views` (user).

### 13.2 What the compiler produces

`kubuno_web_views_compiler_core::compile(text, registry, options)` (Rust; the same through WASM) returns: diagnostics
(1-based line / **UTF-16** column, the unit of TypeScript, source maps and editors; codes `syntax`,
`unknown-element` with « did you mean », `unknown-attribute`, `value`, `children`, `item-placement`,
`property-element`, `x-name`, `handler-name`, `binding`, `binding-path`, `root-only`, `not-on-web`,
`view-file-kind`, `registry` for isolation violations…), the **plan**, `X.kbview.d.ts`, `X.kbview.check.ts`
and its span map, the `x:Name`s and the handler uses. The registry is the host file (`host: true`: may only
name `@ui`, `@kubuno/sdk`, `@kubuno/drive`, `@kubuno/views`) plus project registries (`host: false`: also
project-local modules `./x`, `../x`, `/x`) plus the `.kbcontrol` user controls (element = file stem, rendered by
the code-behind's default export). A project element never shadows a host one; any other module is an
**error** (module isolation rule 1).

**Plan format (ABI 1)** — JSON, the contract between compiler, runtime and designer:

- `Plan {abi, file, kind: view|control, root, tray[], names{x:Name → id}, handlers[], design_size?}`;
  non-visual elements (`ContextMenu`, `ToolTip`, `Timer`, `Query`…) go to `tray`.
- `Node {id (stable_id), el, name?, at, m/x (import specifier + export; absent for items), dom, kind?, fixed,
  props[], events[], children[], content? (the prop receiving the children), slots{prop → nodes},
  items? {prop, shape, content, key, nested, list[]} (children → prop adapter), template? (Repeater),
  sc{class → props} (size-class values), design[] (design builds only)}`. An alternate component
  (`TextField Variant="Outlined"` → `OutlinedField`) is chosen at compile time.
- `Prop {n, to (the registry's prop_map entry, plus change_from: the source of the change event of a two-way
  binding), v (literal typed by kind and mapped through values) | b (binding) | res {key, set}, kind, at}`.
- `Binding {path, mode, trigger?, conv?, param?, fallback?, format?, null?, culture?, depth (enclosing
  templates), at (position of the path)}`; `Event {n, h, from (event_map entry), args_type, at}`.

The **Vite plugin** emits that plan as a module where components are imported (`import { Button as __c7 }
from "@ui"`), Lucide icons are imported by name (aliases `trash` → `Trash2`, `close` → `X`; a path becomes an
`?url` asset), every binding gets `g: (o) => o.prefs?.font` / `s: (o, v) => { o.prefs.font = v }` and every
event a dispatcher `f: (vm, s, e) => vm.save_click(s, e)`, each with a source-map segment to its attribute
(`sourcesContent` = the view). It exports `plan`, `ViewBase = createViewBase(plan, import.meta.hot &&
import.meta.url)` and, for a view without a code-behind, `default`. Imports are only host singletons,
`lucide-react` and project files (checked by test).

**Types.** `.kubuno/views/<path>.kbview.d.ts` declares `plan` and `abstract class ViewBase extends View<Props>`
with one `readonly` handle per `x:Name` (type named like the element, from `@kubuno/views`) and one
`abstract handler(sender, e): void | Promise<void>` per handler (unions when a handler serves several events).
The **check file** writes each binding against the code-behind class (`import type { X as __V } from './X'`,
resolved through `rootDirs`): `__bool(vm.busy)`, `__num(vm.tab)`, `__str(…)`, `__arr(…)`, `__any(…)` (with a
converter or a format), `vm.path = __w` for two-way (catches read-only members), `__fn(vm.method)` for
method-valued properties, `__r(__rowN, vm).path` inside templates (row, then page). Paths not found on the
instance may live on a typed `dataContext`. Projects add `"rootDirs": [".", ".kubuno/views"]` and include
`.kubuno/views` (gitignored).

**`kbview-tsc`** compiles every view (`src/**`, or `kubuno.views.json` → `sources`), prints the view
diagnostics as `file(line,col): error KBV-<code>: …`, writes the generated files, runs the project's own
TypeScript with the given arguments, and rewrites: an error inside a check file → the `.kbview` position
(column one-to-one inside the path text); `TS2515` (missing abstract handler) in a code-behind → every
attribute naming that handler. The output keeps tsc's MSBuild format (VS Error List). Exit 2 on view errors,
else tsc's code.

**HMR.** A `.kbview` edit re-evaluates only its module (self-accepting); `createViewBase` finds the live cell by
module URL, swaps the plan, re-defines the handles and re-renders the instances (state, `@bind` values and
handles kept). In dev the plugin makes every code-behind self-accepting; `X.component()` called again moves the
live instances of the previous class onto the new prototype. A view that does not compile keeps the last good
one on screen with the Vite overlay (`Counter.kbview(4,4): error KBV-unknown-element …`). Standard
`@bind accessor` decorators are lowered with the project's TypeScript (`transpileModule`) before Vite's own
transform, which leaves them as written (oxc does not lower TC39 decorators, and no browser runs them yet).

### 13.3 Verified

- Rust `kubuno-web-views-compiler-core`: 3 unit + 6 integration tests (4 golden views → plan, `.d.ts`, check file, span map,
  diagnostics; design builds; isolation; no shadowing; handle types); `clippy -D warnings` on Windows and on
  `wasm32-unknown-unknown`; `cargo check --all-targets` for `x86_64-unknown-linux-gnu` and
  `aarch64-apple-darwin`. `kubuno-desktop-views-syntax` (83) and `-model` (8 + 1) untouched and green.
- `@kubuno/views-compiler` (vitest, **12 tests, the same results on Linux .220 and on Windows**): golden plans,
  declarations, check files and modules with the real host registry; error positions; isolation; source map of
  accessors and dispatchers; compile time; `kbview-tsc` on a fixture project (5 errors at the exact `.kbview`
  line/column: missing member, wrong type, read-only two-way, nested path, missing handler; nothing from the
  generated files or the valid view); a Vite library build like a module's (externals kept, decorators lowered,
  source map reaching the `.kbview`, no HMR code) and a failing build with file/line/column; **HMR in headless
  Chrome** (Vite dev server, React StrictMode): two clicks, edit the view (the new element appears, the count is
  kept, no reload), edit the code-behind (the new method runs on the live instance), break the view (overlay
  with `Counter.kbview(4,4)`, last good view kept), fix it (recovered, still no reload). The `kbview-tsc` CLI
  was also run by hand on both fixtures.

## 14. WV-3 as built (2026-10-02) — `@kubuno/views` runtime

Sources: core `frontend/src/views/` (`plan.ts`, `view.ts`, `render.tsx`, `binding.ts`, `events.ts`,
`resolve.ts`, `controls.tsx`, `handles.generated.ts`); package `frontend/packages/views/` (types built by
`packages/build.sh`, a throwing `index.js` stub, like `@kubuno/sdk`).

- **Host integration**: alias `@kubuno/views` (Vite + `tsconfig.app.json`), build entry and stable chunk
  `kubuno-desktop-views`, import-map entry (`build/importmap-plugin.ts`); `src/core/viewsHost.ts` (imported by
  `main.tsx`, in the shared chunk) registers `@ui` and the sdk workspace components for interpreted plans, the
  icon lookup (`ICON_MAP`) and `{Res}` through i18next (`languageChanged` re-renders every view). The runtime
  depends only on React, `react-dom/client` and React Query. `loadRemoteModules` refuses a module whose
  `viewsAbi` differs from `VIEWS_ABI` (`views-mismatch`, shown in the bell).
- **Model**: `View` (props, `dataContext`, `use()` run on every render with notifications deferred to the
  commit, `t()`, `invalidate()`, `static component()`), `@bind` (storage on the instance, not in the class's
  private slots, so a prototype swap keeps it; equal writes ignored; writes after unmount dropped with a dev
  warning), `createViewBase(plan, hotKey)`, element handles (a Proxy: camelCase property → current value or
  override, `element`, `focus()`, `click()`). The handle interfaces are generated from the registry by the Rust
  generator (`npm run build:views-handles`, `--check` fails when stale).
- **Renderer**: one memoised `KbNode` per element, subscribed with `useSyncExternalStore` to its view and
  re-rendered only when one of the values it reads changes; the root re-renders to run `use()`. The same code
  renders compiled and interpreted plans. It implements prop/field/runtime targets; the registry converters
  (`invert`, `icon-node`/`icon-component` with `IconSize`/`IconColor`, `null-when-false`, `equals-value`,
  `index-to-key`, `items-source` with `DisplayMember`/`ValueMember`, `element-ref`, `binding-cell`, plus
  `method`); children → prop adapters (array/record, item keys, item content, nested items, `selected-after`
  with a local selection when unbound); slots; `Repeater` rows with stable row scopes; two-way bindings through
  the change event's source (`LostFocus` / `Explicit` pending values, `OneWayToSource`, `OneTime`);
  converters, formats, fallbacks; `{Res}`; DOM-root runtime targets (aria, role, tabindex, inert, cursor, dir,
  theme colours as `var(--color-…)`, `Class`, size/margin/padding/min/max, `Stack.Fill`); `ToolTip` (host
  `Tooltip`); `ContextMenu` / `DropDownMenu` (host `MenuDropdown`); DOM events; the view's `OnLoad` /
  `OnShown` / `OnUnload`; size classes (ResizeObserver on the root); design-mode `data-kb-id`, and no handler
  ever runs in design mode. Handler errors are reported with `file:line:col`, never thrown into React.
- **Runtime elements**: `Repeater`, `Timer`, `Query` / `Mutation` (React Query; their state on the handle),
  `ReactHost`, `defineControl`, `registerControls`, `KbView`, `MessageBox.show` (host `ConfirmDialog`),
  `Dialog.show` (host `FloatingWindow`).

**Verified**: runtime unit tests (core `src/views/runtime.test.tsx`, 10: resolution order, converters both ways,
formats, args, StrictMode rendering, handles, plan swap, prototype swap, ABI refusal, missing handler); the
**conformance suite** (2 tests, Linux and Windows): `Form.kbview` with the host's real `@ui` components,
rendered compiled and interpreted in React StrictMode through 7 steps (click, typing in a two-way field, an
object-field event, a check box mirrored by a switch, a tab change, a slot button writing a handle) — the same
DOM at every step (React `useId` values normalised); HMR in Chrome (§13.3, StrictMode). Size: the runtime
alone, bundled and minified with rolldown, **24.5 KB raw / 9.8 KB gzip** (budget 15 KB). Core
`tsc -p tsconfig.app.json` and `tsconfig.node.json` clean; a host build emits `shared/kubuno-views-*.js` and its
import-map entry.

**Not done / open**: the runtime elements (`Repeater`, `Timer`, `Query`, `Mutation`, `ReactHost`) are not yet
in the web registry (they need meta tables and the desktop conformance allowlist; until then a project
registry can declare them, as the tests do); `Dock`/`Anchor`/`X`/`Y`/table cells wait for the WV-5a containers;
item events raised through a parent adapter (`AccordionSection.OnToggled`, `MenuItem.OnCheckedChanged`,
`ContextMenu.OnItemClicked`) and the `gradient-css` / `status-text` / `workspace-theme` converters are
pass-through; a one-way bound controlled input logs React's read-only warning; the React Profiler budget is not
measured (no pilot screen yet); the `module.toml [frontend] views_abi` install check (core Rust) and
`check_versions.py` knowledge of the two new packages are not done; modules add `@kubuno/views` to their
`SHARED` externals when they adopt views; `x:Props` needs a code-behind; user-control attributes pass through
untyped until WV-7 reads their code-behind; the core-level runtime tests run on Linux only (the host's Vite
config needs native Tailwind bindings).

## 15. Core pilot: WaffleMenu & AccountMenu (2026-10-02)

User requirement (2026-10-02): « AccountMenu et WaffleMenu doivent être des usercontrol dans les versions web et
desktop », then « ils ne doivent pas rester enfermés dans le conteneur qui les contient ». The first real core screen
on views: the header's app launcher and account panel are user controls (`.kbcontrol` + TypeScript code-behind),
compiled by `@kubuno/views-compiler` inside the core build and rendered by `@kubuno/views`; the running web uses them.
Shared contract with the desktop: `SHELL-CONTROLS.md` (names, properties, events, data shapes; §7 = the web as built).

### 15.1 What was built

| Piece | Where (core/frontend) | Notes |
|---|---|---|
| `WaffleMenu` user control | `src/core/shell/menus/WaffleMenu.kbcontrol` + `.ts` | `UserControl` › `ScrollArea` (`ScrollbarGutter="StableBothEdges"`) › `Panel` › `AppTileGrid` (its header band — « Vos favoris » + pencil, or « Annuler » / « OK » + help line — is the `<AppTileGrid.Header>` property element) + `MenuLink` « Plus de modules ». Code-behind: `Apps`, `Favorites`, `Editing` (controlled when the host passes it), the draft, the contract's events. |
| `AccountMenu` user control | `src/core/shell/menus/AccountMenu.kbcontrol` + `.ts` | `UserControl` › header `Stack` (`Dock="Top"`) + `ScrollArea` (`Dock="Fill"`): hero (`Avatar` 96 + camera `IconButton`, greeting `Label`, « Gérer votre compte » `LinkLabel`), the folding accounts card (a `Repeater` over the rows, three row shapes chosen by `Visible`), the actions card (four clickable `Stack`s: two native buttons, two links). |
| Irreducible part | `AppTileGrid.tsx` (custom control, `defineControl`) | The tile grid with drag-and-drop reorder/add/remove, the auto-scroll near the edges and the scroll bar's gutter measure — the same name and events as the desktop's `AppTileGrid`. |
| Menu semantics | `menuItemHost.tsx`, `MenuLink.tsx` | The host decides that the content is a menu: `WaffleButton` provides `DropdownMenu.Item asChild` through `MenuItemHostContext`; the tiles and « Plus de modules » become menu items (roles, arrow keys, close on select); anywhere else they stay links. |
| Hosts (platform chrome) | `WaffleButton.tsx`, `AccountButton.tsx` | Custom controls with the contract's names. The popover is not part of the user control: a Radix menu in a `<body>` portal for the launcher; for the account panel a `<body>` portal placed by `placePanel` (under the button, end edges aligned — start in RTL —, flipped above when the room below is shorter, clamped 8 px into the viewport); Escape and a click outside close, the focus goes back to the button. They own the data and act on the events. `HeaderActions` and `MobileFab` place them; the former `WaffleMenu.tsx` and `UserPanel.tsx` are removed. |
| Project registry | `controls.meta.ts` → `kbview-controls.json` (`npm run build:registry` writes it next to the host registry; `kubuno.views.json` lists it) | `AppTileGrid`, `MenuLink`, `WaffleButton`, `AccountButton`: the core project's own controls (module isolation rule 2). |
| Core build | `vite.config.ts` (`kbview()` before React), `tsconfig.app.json` (`rootDirs` + `.kubuno/views`), `kubuno.views.json` | `tsc -b` type-checks the bindings and handlers of the two controls (generated check files). |

### 15.2 WV-5a elements added (host registry; conformance with the desktop export: 0 failures)

| Element | Renders | Members (desktop names; web-only ones allowlisted with their reason) |
|---|---|---|
| `UserControl` | `@kubuno/views` `UserControl` | dock container filling its host (`flex: 1 1 auto; min-height: 0`), `Surface`, web `Layout` |
| `Panel` | `@kubuno/views` `Panel` | `Dock` bands on one CSS grid (document order, logical columns: `Left` = inline start), `Layout="Absolute"` (X/Y, logical), `Surface`, web `Href` |
| `Stack` | `@kubuno/views` `Stack` | `Direction`, `Gap` (default 8), `CrossAlign`, `WrapContents`, `Surface`, `Stack.Fill`; web `Justify`, `Href` |
| `ScrollArea` | `@kubuno/views` `ScrollArea` | `Corner`; web `ScrollBars`, `ScrollbarGutter` |
| `Repeater` | the runtime's template | `ItemsSource`, `ItemKey`, `DesignItemCount` (web block `template: true`) |
| `Label` | `@ui` `Label` (new) | `Text`, `Role` (Micro / Meta / Body / Heading / Title), `TextAlign`, `Overflow` (Ellipsis / Clip / Wrap) |
| `LinkLabel` | `@ui` `LinkLabel` (new) | `Text`, `Role`, `OnClick`; web `Href` |
| `Avatar` | `@ui` `Avatar` (new, Radix avatar) | `DisplayName`, `Initials`, `Image`, `Tint`, `Shape`, `AvatarSize` |
| `Icon` | `@ui` `IconGlyph` (new) | `Name`, `Size`, `Disc` |
| `IconButton` | `@ui` `IconButton` (new: round) | `Icon`, `Diameter`, `Glyph`, `Filled` — the desktop's members, replacing WV-4's mapping onto `Button` with web-only `Variant` / `Size` |

Web rendering rules added to the runtime: a container with `AccessibleRole="PushButton"` renders a native `<button>`,
one with `Href` an `<a>` (a plain click stays in the app and raises `OnClick`, a modified click opens the address);
on the new elements `Class` is given as the element's own `className` (kept by React, merged with the element's
classes; a size utility written there wins over the `Role`'s step).

### 15.3 Runtime and compiler changes this pilot needed

- Runtime: an event raised inside a `Repeater` template carries its row (`e.row`, `e.rowIndex`); new props from the
  host reach the elements that read them through `this.props` or a getter (the root defers a notification to the
  commit); the layout containers above.
- Compiler (`@kubuno/views-compiler`): compiled plans read their components through getters (`get c()`), so a view
  that sits in an import cycle — any core view: `@ui` (DatePicker) → the core's stores → `@kubuno/sdk` → the shell →
  the view — finds its components at render time. Before, the components were `undefined` there and the host's
  registry fallback (`viewsHost`) hid it.
- Compiler (`kubuno-web-views-compiler-core`, desktop repository; the WASM rebuilt from the local checkout): a bound property whose
  registry `editor` is `list` / `object` is type-checked as an array / any value (a custom control's `Apps`,
  `Favorites`), not as a string.
- The compiler package's test fixtures use the host's `Stack` / `Repeater` (their project stand-ins were removed: a
  project element may not shadow a host one).

### 15.4 Parity (§7 method)

Before = the core frontend of the same working tree with the former TSX components; after = the same tree with this
lot. Both served by a dev core built from the core's HEAD on Windows (`kubuno-core.exe`, its own folder, port 18090)
on the **local MySQL development database** (`KUBUNO_DEV_DATABASE_URL`, `kubuno_dev`); headless Chrome (own profile,
`--disable-gpu`, one tab), signed in through the API with two dev accounts (the active one and one other), never on
the login page; module apps and favourites injected identically in both builds (no module process runs on that dev
core); transitions off, reduced motion, fonts loaded. 60 states per build: light (`kubuno-reference`) / dark
(`kubuno-dark`) × fr / en / ar × 1440×900 / 390×844 × {launcher open, edit mode, dragging (the first favourite over the
third), account panel expanded, collapsed}; at 390 the launcher is the mobile FAB's.

| Measure | Result |
|---|---|
| Pixel diff of the panels alone (tolerance 24/255 per channel) | **≤ 0.162 %** on all 60 (0.000 % on 52; the highest are the RTL account panels, whose anti-aliasing changes because the panel moved, see below) |
| Pixel diff of the whole page, LTR | **≤ 0.273 %** (the differing pixels are the home dashboard's live clock) |
| Accessibility tree of the panels (roles, names, focusable / disabled / expanded) | **equal on 60/60** |
| Keyboard smoke (Enter opens, ArrowDown walks the tiles, Escape closes; Tab through the account panel) | the same sequence, with two intended differences: Escape now gives the focus back to the avatar button, and the camera button's name comes from `aria-label` instead of `title` (same accessible name) |
| Not clipped (« pas enfermés »): the launcher's wrapper shrunk to 36 px and the header cluster made `overflow: hidden` + `transform` + `contain: paint` | both panels portalled to `<body>`, wholly in the viewport, 5/5 probe points hit the panel, at 1440 and 390 |
| Flip and clamp | the header moved to the bottom-left of a 1440×600 viewport: the launcher and the account panel open above their buttons, clamped 8 px from the left edge |

Intended differences: in RTL the account panel now opens under the avatar (the start side), where the former panel
stayed at the right edge of the screen, away from its button (whole-page diffs 4–23 % in `ar`, the panel's content
≤ 0.16 %); Escape returns the focus to the button; an « OK » keeps the favourites the launcher does not know (the
contract's rule; the former web dropped them).

Captures: `C:\kubuno-build\agent-webmenus\captures\{before,after,diff}\` (`<state>-<theme>-<lang>-<width>.{full,panel}.png`),
`parity.json`, `popup-after\` (clipping, flip). Tests: core `src/views/layout.test.tsx` (5),
`src/core/shell/menus/menus.test.tsx` (4), `src/views/runtime.test.tsx` (10), `src/kbview/kbview-registry.spec.ts`
(106); `@kubuno/views-compiler` vitest 12 + conformance 2; `kubuno-web-views-compiler-core` cargo tests 9; `tsc -b`, `kbview-tsc -b`,
the Vite build and the registry checks (`build:registry --check`, conformance 0 failures, `build:views-handles
--check`) pass (Linux, .220, in a copy under /tmp).

### 15.5 Left to do

- `Class` budget of the two controls: **35** attributes (5 in `WaffleMenu`, 30 in `AccountMenu`) — hover states,
  off-scale sizes (`text-base`, `text-2xl`), the white cards' radius, the absolute corners of the close and camera
  buttons, the hero's ring. To reach zero: hover / pressed states on containers, a `CornerRadius` on `Panel`, `Label`
  weights, end-anchored absolute placement.
- The rest of WV-5a: `TableLayoutPanel`, `GroupBox`, `SettingsRow`, `RadioGroup`, `PictureBox`, the missing
  children → props adapters; `Panel` absolute `Anchor` (right / bottom) and responsive dock values.
- `@kubuno/ui` gains `Label`, `LinkLabel`, `Avatar`, `IconGlyph`, `IconButton`: its `types/` are regenerated and the
  package published at the next core release (user); `@kubuno/views` and `@kubuno/views-compiler` likewise.
- The desktop repository's `kubuno-web-views-compiler-core` crate (still untracked there) carries the `editor` rule: commit and tag
  it with the WV-2 work, then rebuild the `.wasm` from the tag.
- Design-time data for the two controls (WV-10), `HeaderActions` as a view, the desktop's `Popup…` properties on the
  web buttons.

## 16. WV-7 as built (2026-10-05) — the language server's web profile

`kubuno-views-ls` (desktop repository, crate `kubuno-desktop-views-ls`, module `src/web/`) answers web views with the
same LSP methods and payloads as desktop views; nothing changed for desktop views (their 166 + 1 + 18 tests unchanged).

- **Profile detection** (`web/mod.rs`, mirrored in Visual Studio by `Kubuno.Views.Logic.ViewTargets`): walking up from the
  view's folder, the first marker decides — `kubuno.views.json` (its `target`; without one it is the web compiler's
  configuration, so web), a `package.json` depending on `@kubuno/views-compiler` or a `*.esproj` → web, a `Cargo.toml`
  naming `kubuno-desktop` → desktop; nothing → desktop. The web project root is the marker's folder (`core/frontend`).
- **Registry** (`web/project.rs`): read from JSON exactly as `ViewProject` does — `hostRegistry` of `kubuno.views.json`
  (else `node_modules/@kubuno/ui/kbview-registry.web.json` upwards), the project registries with their `./x` modules
  rewritten to `/dir/x`, and one user control per `.kbcontrol` of the `sources`. The files are re-read when they change
  (stamps, checked every 1.5 s): elements added by WV-5a/5b appear without restarting.
- **Diagnostics**: `kubuno_web_views_compiler_core::compile` itself (same codes and messages as the Vite plugin and
  `kbview-tsc`), plus module isolation at each element and in the code-behind's imports, handlers, binding members,
  `{Res}` keys and the editor rules of §5 (`Class`, colour literals, positive `TabIndex`, `X`/`Y`/`Anchor`,
  `IconButton` without `AccessibleName`, `Query` without `Key`) and the `xmlns` information (`docs/INTELLISENSE.md`).
- **Code-behind** (`web/ts.rs`, `oxc_parser` 0.153): imports, classes (the one extending `ViewBase`), members (methods,
  `@bind accessor` fields, fields, getters, their parameter types), exported names (`defineControl`), interfaces. Spans only:
  every TS edit is an insertion or an identifier replacement.
- **`.d.ts`**: the compiler's `dts` / `check` / span map written to `.kubuno/views/<path>.kbview.{d.ts,check.ts,check.json}`
  (byte-identical to `writeGenerated`), on open and 400 ms after the last edit, only when the content changed.

**Handler protocol for the web designer (WV-9b / WV-10)** — the desktop's methods, called with a web view's URI:

| Method | Params | Result |
|---|---|---|
| `kubuno/compatibleHandlers` | `{ uri, elementId, event, openFiles? }` (`event` = `OnClick`…, `elementId` = stable id, `""` = root) | `{ handlers: string[] }` — the class's methods whose parameters fit, in declaration order |
| `kubuno/createHandler` | `{ uri, elementId, event, suggestedName?, openFiles? }` | `{ handlerName, location?, edit? }` — `location` (no edit) when the event is already wired; else `edit` (`WorkspaceEdit.changes`: the attribute in the view, the method + missing `@kubuno/views` type imports in the `.ts`); a `suggestedName` naming an existing method only binds it |
| `kubuno/renameHandler` | `{ uri, old?, new, position?, rustRenamed?, openFiles? }` (`uri` may be the `.ts`, with `position` on the method; `rustRenamed: true` = the TS side was already renamed) | `{ edit?, oldName?, reason? }` |
| `kubuno/removeHandler` | `{ uri, elementId, event, openFiles? }` | `{ edit?, handlerName?, removedStub }` |
| `kubuno/bindingPaths` | `{ uri, openFiles? }` | `{ paths: string[] }` |
| `kubuno/registry` / `kubuno/registryVersion` | `{ uri }` (without `uri`: the desktop registry, as before) | the web registry in the `kubuno/registry` shape (`schema`, `target: "web"`, `version`, `components` with their `web` block) / `{ version }` |

`openFiles` maps `file://` URIs of the open `.ts` / `.tsx` buffers to their text, so offsets match unsaved editors. The
default name is `<x:Name>_<event>` in snake_case (the element's name without `x:Name`, the file stem for the root's own
events: `account_menu_load`); the stub is `name(sender: Handle, e: Args): void { // TODO: implement name }` with `_sender`
/ `_e` when the project's tsconfig sets `noUnusedParameters`.

**Visual Studio**: the same `kbview` content type and language client; `Kubuno.Web/LanguageService` adds the TypeScript
cross-language completion (`WebKbviewCodeBehindCompletion`, `WebKbviewNewHandlerCommitManager`); the desktop's Rust
completion and Rust Go To Definition fallback step aside for web views. `KbviewValueContext` moved to
`Kubuno.Views.Logic.IntelliSense` (shared).

**Verified**: LS tests 178 + 1 + 18 + 4 (new: profile detection, oxc reader, import/method insertion, naming, stubs,
JSON resources, project registries, and four round trips through the server — a fixture project, the desktop profile
unchanged, and the core's pilot `AccountMenu` / `WaffleMenu` copied with their registries: no error, completion,
`{Res}` keys, compatible handlers, F12 to the exact TS line, `createHandler` inserting `avatar_click(_sender: Avatar, _e:
MouseEventArgs)` and `, type Avatar` only); `clippy -D warnings`; vskubuno tests (Views 458, Desktop 232 against this
server, Architecture 9, Web 66). Live in Visual Studio 2026 (hive `KubunoWebLs`, VSIX built from HEAD + this lot) on a
copy of the core's `AccountMenu.kbcontrol`: completion on `<`, space, `Variant="`, `OnClick="` (the class's methods and «
Nouveau gestionnaire d'événements »), `{Binding `; warnings and errors in the editor; F12 from `camera_button_click` to
the method; « Nouveau gestionnaire » created `button_click(_sender: Button, _e: MouseEventArgs)` and `type Button` in the
`.ts`. Not done: the caret is not moved to the new method by the HandlerCreationService of HEAD (it looks for a Rust
`fn`; the web designer's pending change handles TS stubs); creating a missing `.ts` code-behind; completion of a user
control's own properties (from its `x:Props`).

## 17. WV-5a / WV-5b as built (2026-10-05)

Core `frontend/`, commits on `main` (GitLab): the WV-5a remainder, the pilot's `Class` attributes, the WV-5b elements,
an element gallery. Every element has its `*.meta.ts` table (`satisfies ElementMeta<ComponentProps<typeof X>>`), the
desktop's canonical name and members, a gallery page (light / dark, LTR / RTL, 390 / 1280 px) and passes the desktop
conformance (`build:registry --check`, `kbview-conformance`: **0 failures, 0 unused allowlist entries**); a web-only or
missing member is allowlisted with its reason (`allowlist.ts`, `allowlist.nav.ts`, `allowlist.lists.ts`).

### 17.1 WV-5a: interaction states, outlines, text, anchoring (runtime `@kubuno/views`)

| What | Members | Web rendering |
|---|---|---|
| States and outline of **any** element (`Control`, web-only, proposed for the desktop) | `HoverBackColor`, `PressedBackColor`, `CornerRadius`, `BorderBrush`, `BorderThickness` (1), `Elevation` (`None/Sm/Md/Lg`) | runtime targets `hover-color` / `pressed-color` (a class + `--kb-v-hover-bg`, one unlayered rule injected once by `views/style.ts`, Tailwind's 150 ms colour transition), `corner-radius`, `border-brush`, `border-thickness`, `elevation` (the host's `shadow-sm/md/lg`). `BorderColor` was not usable: it is the `View`'s window border on the desktop (root-only). |
| Colours | theme tokens (desktop `THEME_TOKENS` names) | `views/style.ts` `tokenColor`: token → the host variable (`Surface1` → `--color-surface-1`, `OnPrimary` → `--color-white`, `Divider` → `--color-border`…; before, a token with a digit resolved to a missing variable); web extension `Token/NN` = NN % opacity (`color-mix(in oklab …)`, Tailwind's `border-primary/40`); `#RRGGBB[AA]` and CSS colour names pass through. |
| Containers (`Panel`, `Stack`, `UserControl`, `ScrollArea`) | `CornerRadius` (clips the children), web `DividerColor` (`divide-y`, `divide-x` across a row), `ScrollArea` web `ScrollBarStyle="Inset"` | `AccessibleRole="PushButton"` containers fill their line like the `<div>` they replace (`width: 100%`, except along a row or under a non-stretching `CrossAlign`), keep the button's centred text, are drawn at 60 % when disabled; `Stack.Fill` also sets `min-width: 0`. |
| `Label`, `LinkLabel` | web `FontWeight` (`Default/Regular/Medium/SemiBold/Bold` → `font-normal/medium/semibold/bold`, the host renders `Medium` at 600), web `FontStyle` (`Italic`) | an explicit `TextAlign` Left is now `text-start` (unset inherits, e.g. a button's centring). |
| `Panel Layout="Absolute"` | `X`, `Y`, `Width`, `Height`, `Anchor`, `Dock` | docked children keep their bands (dock grid, in flow); the others are absolutely placed with the desktop's `panel_child_rects` rules: an anchored edge keeps its distance, both edges stretch (the child fills its wrapper), neither edge recentres. Reference size = the panel's literal `Width` × `Height`, else its **first layout** (the padding box; `DesignWidth/DesignHeight` reach only design-build plans). `X` counts from the inline start: `Right` is the end edge, mirrored in RTL. |
| Item events through the parent's adapter | `AccordionSection Open` (two-way) / `OnToggled`; `MenuItem CheckOnClick`, `RadioGroup`, `Checked` (two-way) / `OnCheckedChanged`; `ContextMenu ItemsSource` / `OnItemClicked` (key) | `render.tsx` `wireOpenItems`, `renderMenu` (check marks kept per view); an item with `Visible="false"` is left out of the parent's list; an empty children adapter no longer hides an `ItemsSource` list. |

New elements: **`TableLayoutPanel`** (runtime: CSS grid, `ColumnStyles`/`RowStyles` `Absolute N` → px, `Percent N` →
`minmax(0, Nfr)`, `AutoSize` → `auto`, missing column = `Percent 1`, missing row = `AutoSize`; children in reading order or at
`TableLayoutPanel.Row/Column`, `RowSpan/ColumnSpan`; aligned in their cell by `Anchor`/`Dock Fill`; `GrowStyle`, `CellBorderStyle
Single`, `CellSpacing`), **`GroupBox`** (`@ui`: the modules' `Section`; web `Description`), **`SettingsRow`** (`@ui`, new on both
targets — web first: 240 px label column beside the control, stacked ≤ 640 px; `Label`, `Description`, `Orientation`,
`ShowDivider`), **`RadioGroup`** (`@ui`, new on both targets: `Option` children or `ItemsSource`, two-way `SelectedValue`,
`Orientation`; one native radio group — `Radio` gained `name` — so Tab enters once and the arrow keys move the choice;
`role="radiogroup"`), **`PictureBox`** (`@ui`: `SizeMode` → `object-fit`, `CornerRadius`, `BorderStyle`, `AccessibleName` = alt text).
Also two desktop members the web lacked: `Badge MaxWidth`, `FloatingWindow ShowClose`.

### 17.2 The pilot's `Class` attributes (§15.5)

35 → **8** (`WaffleMenu` 5 → 1, `AccountMenu` 30 → 7). Replaced: hover backgrounds (`HoverBackColor`), the white cards
(`Margin` + `CornerRadius="20"` + `BackColor="Surface"`), lines between rows (`DividerColor="Border/50"`), outlines and the
camera button's shadow (`BorderBrush`, `Elevation`), weights (`FontWeight`), the buttons' paddings (`Padding`), the inset scroll
bar (`ScrollBarStyle`), the close and camera buttons' corners (`Panel Layout="Absolute"` + `Anchor`), `min-w-0` / `w-full` (now the
runtime's defaults); the « Plus de modules » look moved into `MenuLink` (the launcher's footer link, inset by the grid's
`--kb-waffle-inset`). Left, by design or waiting for a decision: off-scale text sizes (`text-base`, `text-2xl`, `text-[10px]`,
`text-[11px]` — VIEWS-SPEC §7.1: a role, never a size), the hero avatar's ring (outline + shadow), the overlapping preview
avatars (`-space-x-1`), the chips' / badge's `flex items-center justify-center shrink-0`.

Parity (§7 / §15.4 method, bench `C:\kubuno-build\agent-webelements`: `run-cap.ps1`, `cap.mjs` — cards now found by their
computed 20 px radius —, `compare.mjs`; before = core `cbe1e58`, after = this lot; 60 states): launcher **0.000–0.001 %** on all
60; account panel **0.000 %** in `fr` and `en` (all themes, widths, states); accessibility trees **60/60 equal**; keyboard logs
equal. Intended differences, Arabic only (panel 0.78–1.93 %): the close and camera buttons sit on the end side (logical
anchoring; the former classes were physical `right-*`), the other accounts' names are start-aligned (the former `text-left`).
Whole page ≤ 0.594 % LTR (the dashboard's live clock).

### 17.3 WV-5b: navigation and misc (`@ui`, `navigation.meta.ts`)

`Toolbar`/`ToolbarItem` (`role="toolbar"`, one tab stop, arrow keys mirrored in RTL, Home/End; web `ToolTip`, `Enabled` on
items), `Sidebar`/`SidebarSection`/`SidebarItem` (the shell's `SidebarNavItem` look; `<nav>` of links with `aria-current`, folding
sections with `aria-expanded`, compact rail, `ItemsSource`, two-way `SelectedItem`, `OnItemInvoked`/`OnSelectionChanged`),
`StatusBar`/`StatusLabel` (`role="status"`, separators, spring and clickable cells, `OnItemClicked`), `Splitter` (focusable
`role="separator"` with `aria-valuenow`, arrows ±10 / Shift ±50 px, Home/End, pointer drag, two-way `Distance`), `SearchField` (the
shell's search pill; Escape clears; web `OnSearch` on Enter), `MaskedField` (WinForms mask characters, `Invalid` →
`aria-invalid`, values shown LTR in RTL), `PaintBox` (a `<canvas>`; `OnPaint(sender, e: PaintEventArgs { ctx, width, height,
dpr, data })` in CSS px, repainted on resize, pixel-ratio change and when the web `PaintData` changes), `Popover` alignment
(`Placement`, `Alignment Center`, `PopupWidth/Height`, `LightDismiss`, `OnOpened`; Escape closes and returns the focus — in
`AnchoredPopover`, so for every user of it), `Avatar Presence`. Runtime: converter `items-source-icons`, args adapter `paint`.
`scripts/verify-nav.mjs`: 50/50 (LTR and RTL). Known limit: `SearchField`'s clear-button label is English only (`@ui` has no
i18next). **Ribbon: not promoted** (next lot) — the copies are office, app, flow `ribbon/Ribbon.tsx` (≈ 980 lines, 13–18
lines apart) and build (573 lines, an older fork, ≈ 494 lines apart), each tied to `react-i18next`, `WorkspaceTheme`, a local
`officeThemes` and `types.ts`: move `Ribbon`/`RibbonTabStrip`/types to `@ui` (i18next → `uiT`, theme → tokens), map onto
RIBBON.md's classes (`Command`, adaptive sizing), ≈ 30 ribbon metas against the desktop ribbon family, publish `@kubuno/ui`,
migrate the four modules (rebase build first) — several days over five repositories.

### 17.4 WV-5b: lists and trees (`@ui`, `lists.meta.ts`)

| Element | Desktop members | Web only (allowlisted) |
|---|---|---|
| `ListBox` | `SelectionMode`, `SelectedIndex` (two-way), `ItemsSource`, `ItemHeight`, `OnSelectionChanged` | `DisplayMember`, `ValueMember`, `SelectedValue` (two-way), `OnSelectedValueChanged`, `OnItemActivate` |
| `CheckedListBox` | `SelectedIndex`, `CheckOnClick`, `ItemsSource`, `OnSelectionChanged`, `OnCheckedChanged` (`ItemCheckEventArgs`) | `DisplayMember`, `ValueMember`, `ItemHeight` |
| `ListView` | `SelectedIndex`, `MultiSelect`, `ItemsSource`, `OnSelectionChanged`, `OnItemActivate` | `View` (`Details`/`List`/`LargeIcon`), `ItemHeight` |
| `TreeView` | `SelectedPath` (two-way, dot-separated indexes `0.2.1`), `MultiSelect`, `ItemsSource` (children in `Items`), `ItemHeight`, `OnSelectionChanged`, `OnItemActivate` | — |
| `Item` | `Text` | `Value`, `Icon`, `Checked` (two-way), `Expanded`, `OnCheckedChanged` |
| `DataTable` (alignment) | `SelectedIndex` (two-way, in `ItemsSource` order whatever the sort), `OnSelectionChanged` (default event), `OnRowActivated` (double-click / Enter), `Density`, `EmptyTitle`, `EmptyText` | — |

`@ui` `ListBox.tsx`, `ListView.tsx`, `TreeView.tsx` over `listCore.ts` (keyboard model) and `useVirtualList.ts` (fixed rows,
32 px by default; without a `Height` a list grows to 320 px, then scrolls): WAI-ARIA listbox / grid / tree keyboard
(arrows, Home/End, Page Up/Down, type-ahead, Space, Left/Right fold, Enter, Shift/Ctrl), mirrored in RTL,
`aria-activedescendant`, `aria-selected/checked/expanded/level/setsize/posinset`. `Item` children nest as sub-trees in a
`TreeView`; in a `ListView` an `Item`'s children fill the next columns, and a child with a header, binding, width or alignment
is a `Column`. Rows from `ItemsSource` are read by `Text`/`text`/`label` or `DisplayMember`. Runtime: `wireCheckedItems`
(two-way `Item Checked`), nested item properties read at any depth. `DataTable` `rowKey` is optional (row position).
Caveat for code-behinds: a getter must return the same value until it changes (a new `[]` on every read loops React, error
185). `scripts/verify-lists.mjs`: 39/39 (10 000-row virtualisation included). Not done: cell editing, `Sorted`, owner-draw, the
selection highlight of the DataTable's card layout (< 700 px), `SmallIcon`/`Tile` views, an NVDA pass.

### 17.5 Gallery and checks

`gallery.html` + `vite.gallery.config.ts` (dev and verification only, never in the product build): one view per element under
`src/gallery/pages/` (`X.kbview` + code-behind `X.ts`, class named after the file), rendered with the host's CSS, i18n and
icon resolver; `?el=X&theme=light|dark&dir=ltr|rtl`. `scripts/capture-gallery.mjs` captures every page in 8 states (own Chrome
profile and port, one tab; the viewport grows to the content's height — Chrome's `captureBeyondViewport` shifts RTL pages);
it also captures the element marked `AccessibleDescription="gallery-hover"` hovered and `gallery-click` opened.
`scripts/verify-wv5a.mjs` (28 checks), `verify-nav.mjs` (50), `verify-lists.mjs` (39): keyboard, ARIA tree, events,
two-way bindings, anchoring, in LTR and RTL.

### 17.6 Left

- Desktop side of WV-5a: `SettingsRow` and `RadioGroup` elements in `kubuno-desktop-views` (allowlisted `element-web-only`
  until then); the web-only members proposed for the desktop (states, `CornerRadius` on containers, `BorderBrush`,
  `Elevation`, `DividerColor`, `FontWeight`/`FontStyle`, `Panel.Layout`).
- The plan carries `design_size` in design builds only: a root `Panel Layout="Absolute"` anchors against its first layout in
  production (the WASM compiler would have to emit it — desktop repository).
- The other `NOT_YET('WV-5a')` members (TextBoxBase `PasswordChar`/`CharacterCasing`…, `Invalid`, `Card Surface`, `CheckBox
  ThreeState/AutoCheck`, `RadioButton AutoCheck`, `Slider LargeChange`, `NumericField DecimalPlaces/ThousandsSeparator`,
  `TextArea MinLines/MaxLines`, `Breadcrumb RootChevron`).
- Ribbon promotion to `@ui` (next lot: see 16.3).
- The runtime alone is now 16.8 KB gzip (was 12.7 KB at the pilot, budget 15 KB, §14): the anchoring, table and menu logic.
- Publishing `@kubuno/ui` 0.1.14 and `@kubuno/views` 0.1.1 (user).

## 18. WV-9b / WV-10 as built (2026-10-05) — the web views in the Visual Studio designer

A `.kbview` / `.kbcontrol` of a web project opens in the shared Kubuno View Designer (Design | XML | Split, Toolbox,
Properties, ⚡ events, Document Outline, component tray — all of `Kubuno.Views`, §4.1) with its Design pane rendered by
WebView2. Checked live on `Kubuno.Core.slnx` with the core pilot's `WaffleMenu.kbcontrol` and `AccountMenu.kbcontrol`.
The WV-9a spike (`.kbwebspike`, `WebDesigner/Spike/`, `SpikeViewDocument`, `WebSurfaceProtocol`) is removed; its
mechanisms live on in the classes below. Shared-layer seams: `DESIGNER.md` §21.

### 18.1 Visual Studio side (`src/Web/Kubuno.Web/WebDesigner`, `src/Web/Kubuno.Web.Logic/WebDesigner`)

| Piece | What |
|---|---|
| `WebDesignSurfaceHostFactory` | An `ITargetDesignSurfaceHostFactory` added by `WebLayer`: claims the views of web projects (`WebViewProject.Find`, the profile rule of `kubuno-views-ls`, §5), every other view keeps the desktop's Rust surface. |
| `WebDesignSurfaceHost` (+ `.Protocol`, `.Input`, `.Language`) | `IProtocolDesignSurfaceHost`, `IDesignSurfaceStatusAware`, `IDesignSurfaceRuntimeAware`, `IDesignSurfaceLanguageServices`. A WPF `WebView2` (Visual Studio's own WebView2 assemblies, one environment per process, profile `%LOCALAPPDATA%\Kubuno\webview2\<hive>`). The page speaks the desktop wire shapes (parsed by the shared `DesignSurfaceProtocol`, `…DragDropProtocol`, `…ContextMenuProtocol`), so the shared `DesignSurfaceEditingCoordinator` drives it unchanged. The state the designer pushes (text, design mode, selection, zoom, language, project controls, theme, document info) is kept and replayed whole on every `ready` of a (re)loaded page. |
| `DesignServer` + `DesignServerDiscovery` | The project's dev server: found through `<root>/.kubuno/design-server.json` (written by the Vite plugin while it listens: `{version, urls, designPath, pid, root}`), probed at `project.json`, local addresses first. « Démarrer le serveur de développement » runs `npm run dev` hidden (through `obj\kubuno-npm.cmd` when `Kubuno.Web.Sdk` prepared the node overlay, Visual Studio's Node.js appended to the `PATH`), with `CHOKIDAR_USEPOLLING` on a network drive (Node's `fs.watch` fails on `Z:` with `UNKNOWN -4094`, measured) and in a kill-on-close job object: the server ends with Visual Studio. Output in Output > Kubuno. |
| `WebDesignRuntimeSource` | The runtime bar (`IDesignSurfaceRuntimeText`): nothing on the project's dev server; « Aperçu : runtime intégré — le serveur de développement du projet ne tourne pas… » + « Démarrer le serveur de développement », « …démarrage… » + « Annuler », « …n'a pas démarré » + « Réessayer ». A 4 s monitor switches to the dev server when one answers and back to the bundled host after two failed probes. |
| Bundled design host | `tools\web-design-host\` in the VSIX = the static build of `@kubuno/host-runtime` (core `npm run build:design-host`), served from the virtual host `https://kubuno-design.invalid/` (`SetVirtualHostNameToFolderMapping`): built-in elements, the production fonts and themes, project controls as labelled placeholders, no code-behind. VSIX property `KubunoWebDesignHostDir` (default: the sibling core checkout's `packages/host-runtime/dist`, a build warning when absent). |
| Keys (`AcceleratorRouting.RouteDesignSurface`, `VsKeyBindings`) | In design mode the page keeps Del, Backspace, arrows, Tab, Enter, Esc, F2, Ctrl+C/X/V/D; Visual Studio gets Ctrl+Z/Y (the buffer's undo), Ctrl+S, F4, F7 and every other binding (`IVsFilterKeys2.TranslateAcceleratorEx` + `PostExecCommand`, §11). Every page gesture activates the document frame. |
| Toolbox drops | The §11 hybrid channel: the page reports `toolboxDragDetected`, a layered child window over the browser takes the OLE drag with `WebToolboxDropTarget` (the Toolbox item through `ToolboxDataObjectReader`) and drives the page with `dragEnter/dragOver/drop/dragLeave`; the page answers `dropTargetChanged` and posts `insertChild`. Web Toolbox items also carry `CF_UNICODETEXT` = `<Name />`. |
| Registry, handlers | `kubuno/registry {uri}` (the language server's web profile, §16) — the host's own `WebRegistry` (host registry + project registries with rewritten modules, `origin: "project"`) only when the server answers with the desktop registry. Handlers: the server's `kubuno/createHandler` (§16; the interim C# TypeScript stub writer is gone), the caret put in the new TypeScript method. F7 / Shift+F7 know `.ts` / `.tsx` / `X.kbcontrol.ts`, and `View.ToggleDesigner` (F7 in Visual Studio 2026's default scheme). |

Protocol additions (host → page): `setDocumentInfo {file, codeBehind, className, userControls, designData}`,
`setVsTheme {mode, colors}`, `setZoom {zoom}`, `setLanguage {lang}`; (page → host) `surfaceInfo {version: 1, target:
"web", views: "1", ui, hostRuntime, mode: "project" | "bundled"}` (refused on another version, target or plan ABI),
`ready`, `focusState`, `unhandledKey`, `toolboxDragDetected`, `log`, `surfaceError`, `viewportChanged`. Everything else
is the desktop protocol.

### 18.2 The design page (core `frontend/src/views/design/`, `src/views/designApi.ts`)

- **Two entries, one page**: `entry.tsx` (project mode, served by the dev server at `/__kubuno_design__/`: registries
  and user controls from `/__kubuno_design__/project.json`, the project's controls and the view's code-behind imported
  through Vite, HMR kept) and `entry.bundled.tsx` (the `@kubuno/host-runtime` build). The buffer is compiled in the
  page with the WASM compiler (`design: true`), the last good plan kept (dimmed) when it does not compile;
  `renderStatus` after every compile.
- **Code-behind**: a *design class*, a subclass of the code-behind with a cell of its own (`designClass`,
  `setDesignPlan`, `setDesignDataContext`): getters and `use()` run, handlers never; the live class and its HMR cell
  are never touched. Runtime changes: switching design mode re-renders in place (state kept); a `Repeater` without
  data in design mode shows `DesignItemCount` sample rows (`name 1`, `name 2`…).
- **Sample data**: `d:` attributes and `<view>.design.json` (`props`, `dataContext`, `frame {background,
  cornerRadius, border}` for the host's popover chrome); written for the two pilot controls.
- **Overlay**: input neutralised in design mode; layout map of every `[data-kb-id]` of the document (portals
  included); hit-test = deepest element, reverse paint order; hover stroke, selection frame and 8 handles (filled when
  resizable), parent outline, container outlines, Ctrl/Shift multi-selection; insertion markers per layout kind
  (`Stack` line ⟂ `Direction`, dock containers in document order, `Panel Layout="Absolute"` free XY with `X`/`Y`
  logical in RTL, `ScrollArea` delegating to its child container, leaves before/after); move / resize →
  `editRequests`, reorder / reparent → `moveElement`, keyboard (Del, arrows ±1/±8, Esc = parent, Ctrl+C/X/V/D),
  double-click, context menu.
- **Viewport toolbar** (platform-neutral HTML in Visual Studio's colours): width design / 390 / 768 / 1280 / 1440 /
  fit, Kubuno light / dark (the core's real `themeStore`), FR / EN / AR (`dir=rtl`), Conception / Exécution (run mode:
  real input, handlers run in project mode), « Données d'exemple ». Choices kept in `localStorage`.
- **Portals and popovers**: a popover's content is a user control designed on its own surface (WaffleMenu,
  AccountMenu); `ContextMenu`, `ToolTip`, `DropDownMenu` are tray components and never open in design mode; something
  opened in run mode stays selectable after switching back (the layout map covers the whole document).
- **Dev-server route** (`@kubuno/views-compiler`, `src/design-server.ts`): `/__kubuno_design__/` (through
  `transformIndexHtml`), `project.json`, `themes/<id>/` (`design.themes`), the discovery file; `kubuno.views.json`
  `"design": {"entry", "themes"}`; `kbview({ designServer: false })` turns it off.

### 18.3 Verified

- **Live, Visual Studio 18 hive `KubunoWebDesigner`, 175 % DPI, Visual Studio dark theme** (captures in
  `C:\kubuno-build\agent-webdesigner\out\`): WaffleMenu and AccountMenu open in the designer; with no dev server, the
  bundled host and the bar (`vs3-bundled.png`); « Démarrer le serveur de développement » starts Vite from Visual Studio
  and the surface switches to the project's page with the real `AppTileGrid` and sample data (`vs4-project.png`);
  a click selects and the XML follows (`vs8-select-label.png`), Esc selects the parent `Stack`, F4 shows it in the
  Properties window, `Gap` set there writes `Gap="24"` in the buffer and Ctrl+Z on the surface undoes it; a `Label`
  dragged from the Toolbox lands in the header `Stack` (`vs13-drop.png`), its `Text` set in the Properties window
  renders at once (`vs14-label-text.png`); a double-click creates `label_click(_sender: Label, _e: MouseEventArgs)`
  in `WaffleMenu.ts` with `OnClick="label_click"` and opens it at the method (`vs15-handler.png`); F7 toggles view ⇄
  `.ts`; light / dark, 390 / 1440, FR / AR (`waffle-*-175dpi.png`, `account-*-175dpi.png`); killing the dev server
  brings the bundled host and the bar back within 8 s (`account-fallback-after-stop.png`); closing Visual Studio ends
  the dev server it started. Nothing was saved (the core files are unchanged).
- **Tests**: C# — `Kubuno.Web.Tests` 63 (web project detection, registry merge, discovery, messages, key routing),
  `Kubuno.Views.Tests` 460 (+ per-document factory selection, TypeScript F7 / Shift+F7), every other suite green;
  frontend — 57 tests in `src/views` + `src/core/shell/menus` (jsdom: hit-test, layout map, drop targets per layout
  kind, move / resize ops, keyboard, protocol), 14 in `@kubuno/views-compiler` (+ the design-server route), the
  conformance suite; `tsc -p tsconfig.app.json` clean.

### 18.4 Not done / limits

- Not captured at 100 % DPI (this machine's only monitor is at 175 %; changing the system scale would disturb the
  user's session). Marquee selection, snaplines, `format` commands (logged), table cells; moving an element into an
  absolute panel writes no `X`/`Y`; live data (`setDataMode`) is not wired.
- Bundled mode runs no code-behind (getters empty: both `Visible` branches show); module projects through
  `@kubuno/host-runtime/entry` in their own dev server are not exercised; `packages/host-runtime` has no `.esproj`.
- The Toolbox flyout is hidden behind the surface when auto-hidden (airspace, §11): pin it. Toolbox items are
  re-installed when the active designer's target changes (desktop ⇄ web).
- Found, not fixed: `generatedPaths` (`project.ts`) writes generated files outside `.kubuno/views` for a view outside
  the project root; the launcher's and account panel's chrome are hard-coded light (`#E9EEF6` in `WaffleButton` /
  `AccountButton`), unreadable in the dark theme — the designer shows it faithfully.

## 19. WV-6 as built (2026-10-05) — `.kbres` resources on the web

### 19.1 What was built (core `frontend/packages/views-compiler`, `frontend/src/views`)

- **`.kbres` → i18next bundles in the Vite plugin.** `import strings from './strings.kbres'` loads the neutral file and
  its satellites (`strings.fr.kbres`, …) through the desktop's own reader/writer (`kubuno-resources-model`, compiled
  into the compiler's WASM: ops `kbres_parse` / `kbres_write`, errors with line and column) and returns
  `{ en: {…}, fr: {…} }`, ready for `registerModuleTranslations`. The neutral file names its language
  (`Culture="en"`, else the plugin's `neutralCulture` option); dotted names nest (`a.b.c`), a dot inside a segment is
  escaped (`errors\.token\.invalid`, needed by the setup dictionary). A satellite added or edited reloads its set
  (HMR). `client.d.ts` declares `*.kbres`.
- **`{Res}` with arguments and plurals, both targets.** The plan's `res` carries `args: [{n, b?, v?}]` (a bound
  argument is an ordinary one-way binding); the runtime's resolver receives them
  (`setResourceResolver((key, set, args) => …)`), `interpolationOptions(args)` turns them into i18next options (each
  name as written and with a lower-case first letter, `Count` → `count` as a number). Plural forms follow i18next
  (`key_zero` for 0, then `key_<CLDR category>`, then `key`) — the desktop's `plural::candidates` does the same, so
  one `.kbres` gives the same string on both targets. `View.t(key, set, args)` takes the same arguments; a view
  re-reads its strings when a bound argument changes and when a bundle is added.
- **Converter `kbres-convert`** (`src/i18n-convert.ts`, `bin/kbres-convert.js`): JSON locale folders and TS modules
  calling `registerModuleTranslations` (read by a static evaluator: literals, constants, imports — anything else is
  an error with its position) → one `.kbres` set; `--verify` reads it back and compares every key and value.
- **The core's navigation labels are converted** (`src/core/i18n/nav.kbres` + 12 satellites; `nav.ts` imports the set).

### 19.2 Verified

- Round trip, 13 languages, every key and value identical (key order included): `core.json` (≈ 50 000 strings),
  the setup dictionary (dotted keys), the navigation set; plural forms, `{{placeholders}}`, `<0>Trans</0>` tags and
  leading/trailing spaces kept; XML-hostile values (`<`, `&`, quotes, newlines) round-trip
  (`test/i18n-convert.test.ts`).
- Runtime: plural choice for `fr` (0 and 1 singular), `ar` (zero, one, two, few, many, other), `en`
  (`runtime.test.tsx`).
- RTL capture: the gallery page `Resources` (`src/gallery/pages/Resources.kbview` + `.kbres` in en / fr / ar) shows
  the six Arabic plural forms right-to-left — `C:\kubuno-build\agent-webmig\captures\wv6\Resources-{light,dark}-rtl-{390,1280}.png`.

### 19.3 The two designer defects of §18.4, fixed

- Panel chrome: `WaffleButton` / `AccountButton` read the theme token `PanelBackground` (`--color-panel-bg`, defaulting
  to `--color-search-bg`; Numix keeps `#e9eef6`) through `shell/menus/panelChrome.ts`; the `*.design.json` frames and
  the design surface use the token too; a test checks no literal colour is left and the text contrast is ≥ 4.5:1 in
  every theme. Desktop: `PanelBackground` theme field and token; designer: `ThemeTokens.cs` + fixture.
- `generatedPaths`: a view outside the root goes under `.kubuno/views/_external/<absolute path segments>` (drive colon
  removed, `..` → `_up`), the same rule as the language server; stale generated files of deleted views are removed.
  A project opened through a link (`C:\kubuno-build` → `E:\…` junction: Vite hands out real paths) keeps its project
  paths — found while checking the migrated screens in the designer (the views' imports of project controls pointed
  to `../../../…`).

## 20. WV-11 as built (measured, 2026-10-05) — `@kubuno/views-migrate`

### 20.1 What it does

`kbview-migrate [--project dir] [--write | --out dir] [--report file] [--component Name] files…` (ts-morph over the
project's `tsconfig.app.json`, the registries of `kubuno.views.json`, the English bundle for `t()` keys). One TSX screen
→ `X.kbview` + `X.ts` (or `X.tsx` when React code stays in the class) + `X.parts.tsx`, and a per-file report
(converted / partial with reasons / skipped):

- §6.2 conversions: `@ui` components by reverse registry lookup; HTML kept as `Panel`/`Stack`/`Label` with `HtmlTag`
  (headings, lists, forms, `<label>`); Tailwind classes turned into properties only where the result is
  pixel-identical (roles, weights, theme colours, flex rows/columns with exact gaps), the rest kept in `Class`;
  `t()` → `{Res}` (arguments, `count`); state → `@bind accessor`; hooks split into `useStores()` (reading nothing of
  the class) and `useHooks()`, their results published as fields with `View.publish` (shallow comparison — a hook
  returning a fresh equal object does not loop); derived constants → getters (`View.memo` when they build objects,
  deps = what the body **and its guard** read); inline handlers → methods; setter calls → assignments; two-way
  `value`/`onChange` → `Mode=TwoWay`; `cond && <X/>` and early returns → `Visible` with guarded getters; `.map` →
  `Repeater` over memoized rows; `<button>` → `Panel AccessibleRole="PushButton" AutoSize`; `title` → `ToolTip`;
  `<Link>` → `LinkLabel`; Lucide icons → `Icon Name`; the file's other statements kept when used, top-level side
  effects (`Registry.register(…)`) always, after the class; importers switched to the default export (batch pre-pass).
- ReactHost extraction: a component that is no element (a local helper, another screen's component) without children
  → `<ReactHost Component Props>` on that very component (local ones exported from `X.parts.tsx`); anything else that
  does not convert (children, spreads, an object value for a text property, a nested list, an unmapped prop) → a part
  function in `X.parts.tsx` with typed props (`X['member']`), a TODO comment in the view. A part shown under a
  condition has its component getter guarded like its props.
- Runtime support added for it: `HtmlTag`, `OnSubmit`, `AccessibleModal`, `AccessibleHidden`, `InputType`,
  `AutoComplete`, `ButtonType`, `AutoSize`, `InheritFontSize`, `ReactHost`, `ToolTip` as the element's `title`,
  `View.memo`, `View.publish`, `component()` typed.

### 20.2 Measured on 10 core screens (before any bulk use)

Settings page + six tabs (profile, notifications, themes, clients, security, sessions), appearance dialog,
administration home and « coming soon »:

| Measure | Result |
|---|---|
| Status | 1 converted, 9 partial, 0 skipped |
| Elements mapped to `.kbview` elements | 112 / 158 (71 %) — 40 parts left in React, 80 `Class` attributes |
| `npm run typecheck` (`tsc -b`, generated view types included) on the output | 0 errors, 10 / 10 |
| Renders (headless Chrome, own profile) | 10 / 10 |
| §7 parity, unedited codemod output (≤ 0.5 % root pixels, same AX tree, same text, same Tab order; light/dark × fr/en/ar × 1440/390 = 12 captures each) | **10 / 10 PASS**, max root diff 0.000 %, AX / text / keyboard identical in all 120 captures |
| Opens in the Visual Studio designer (project dev server) | 10 / 10 |

Per file (elements mapped / total, parts): SettingsPage 5/15, 10 · SecurityTab 9/11, 1 · ThemesTab 9/11, 1 ·
ClientsTab 25/30, 4 · NotificationsTab 26/31, 4 · SessionsTab 6/11, 4 · ProfileTab 4/8, 4 · AppearanceDialog 17/21, 3
· ComingSoon 6/6, 0 · HomeSection 5/14, 9. Partial reasons: another screen's or a local component (TwoFactorSection,
SessionList, DeviceCard, HomeCard, AlertsCard, GettingStartedCard, ThemePreview, ModeMock, NotifCheck, StoreBadge,
`Section`, the settings tabs, `Slot`), `Dropdown` `width` / `height` / `options` objects, spread props, nested lists, an
`EmptyState` action object. No screen was hand-edited after the codemod.

Getting there took six measured rounds (7/10 → 5/10 → 10/10, regressions caught, 10/10). What parity caught that
type-checking did not: a native button's width (→ `AutoSize`), a tooltip wrapper changing row sizing (→ `ToolTip` as
`title`), hooks re-rendering without end (→ `View.publish`), stale first-render values, a class used as a component by
an importer, a dropped top-level registration (the « Account and security » notification group vanished). What only
the designer caught: a memoized part's props computed while its guard failed (the sessions tab's error state).

### 20.3 Limits

- Pixel parity is only as good as the bench's states: the error and empty branches of screens are not captured (the
  designer caught one); captures run against a dev core with fixtures for live counts.
- 40 parts remain React; a local helper component becomes a `.kbcontrol` only by hand (not automated).
- `Class` attributes stay where no property is exact (spacing utilities, grids, responsive variants).

## 21. Core migration status (2026-10-06)

### 21.1 What is converted

| Area | Screens (views: `.kbview` + code-behind + parts) | Parity (§7) | Commit (core) |
|---|---|---|---|
| Pilot | `shell/menus/WaffleMenu`, `AccountMenu` (`.kbcontrol`, §15); settings page and its six tabs, appearance dialog, administration home | 13 / 13 | earlier, `db214f0` (regenerated) |
| Account settings | API tokens, two-step verification, backup codes, « Download my data », profile field helpers | 10 / 10 | `0f5073a`, `db214f0` |
| Administration ▸ users, groups, modules | accounts list and sheet (identity, profile, security, activity), password reset dialog, groups, installed modules, a module's page and settings, marketplace | 16 / 16 | `0cd3271`, `5df397a`, `db214f0` |
| Administration ▸ settings, security, storage | settings pages, rows, scope bar and tree; storage, security dashboard, instance health, SSO, LDAP, admin roles | 20 / 20 (health with fixtures) | `1322556`, `8a51593`, `db214f0` |
| Administration ▸ directory, operations | organisational units, audiences, resources, directory settings, holidays, domains; dashboard, devices, alerts, audit and event logs, reports, rules, detectors | 26 / 26 (audit with fixtures) | `a8ae135`, `7d3bd70`, `db214f0` |
| Administration ▸ the rest | navigation tree and page, mail, network, speech-to-text, backup, database, themes page and component gallery, data export and migration, subscription, inline-edit fields, search panel | 14 / 14 | `db214f0` |
| Shell | header, sidebar, top bar, header actions, side rails, mobile navigation and button, right panel, maintenance banners, the about / labels / applications / home pages | 8 / 8 | `db214f0` |
| Sign-in | login, registration, password reset, forced password change, re-authentication, OAuth callback | 5 / 5 | `db214f0` |

Parity = screens passing (≤ 0.5 % root pixels, same accessibility tree, text and Tab order) over light/dark × fr/en/ar
× 1440/390 (12 captures each, 6 for desktop-only states): **112 screens, all passing** on the last run (`all4`/`all6`,
bench `C:\kubuno-build\agent-webmig2\bench`, sets `screens-*.json`, reports `captures\diff-*-all4\parity.json`). Two
screens of the older sets read live data that drifts between runs (instance health counts, audit entry count); their
fixture twins (`screens-sec-health.json`, `screens-ops2.json`) pass. Every screen converted before 2026-10-06 was
**converted again from its original TSX with the final codemod** (`regen.sh`: originals of `ed9105e`, one pass over
the 279 files of the batches) and benched again; only two code-behinds are hand-finished (below).

Codemod over the whole core batch (279 files after `--split`): **21 converted, 200 partial, 58 left as TSX; 2404 / 3346
elements mapped (72 %)**, 866 parts, 1578 `Class` attributes. Per batch: settings 84 %, users 66 %, modules 81 %,
admin settings 73 %, security 70 %, directory 65 %, operations 73 %, the rest 74 %, shell and sign-in 66 %.

Opens in the VS designer (hive `KubunoWebMig2`, solution opened from its real path — through the `C:\kubuno-build`
junction Vite cannot serve the code-behind, « code-behind not loaded »): `ApiTokensTab`, `MyDataTab`,
`TwoFactorSection`, `AppHeader`, `LoginPage`, `ThemesPanel` (the component gallery renders), `AdminNavTree`
(captures `C:\kubuno-build\agent-webmig2\vs\out\vs-*.png`).

### 21.2 Rules added while migrating (each with a test in `packages/views-migrate/test/rules.test.ts`)

Object props field by field (dialog footers, `action={{…}}`), slots as property elements, `t={t}` → `HostStrings`,
`className` of a field → `FieldClass`, spread props → `ReactHost`, `--split` (one file per exported screen, shared
local components copied, importers switched), filled collections, nested early returns, aliased conditions, memo deps
through methods, typed host props, hook results published in order, `data-*` → `DataAttributes`, hooks inside
expressions (`useLocation().pathname`) kept in `use()`, store hooks called only to render again kept as memo
dependencies, methods passed as values bound once (`ref={setNode}`), conditional handlers, React event types, narrowed
components used as tags, template literals kept verbatim in parts, a screen nothing of which maps left as TSX, a
module's generated catalogue (`src/i18n.data.json`) read as its bundle.

Runtime and registry changes they needed (`@kubuno/views` 0.1.3, `@kubuno/ui` 0.1.16, `@kubuno/views-migrate` 0.1.1 —
versions and changelogs prepared, **not published**): nested object fields, `HostStrings` / `setTranslator`, a view no
longer rendering itself again from its own hooks (`notifyElements`), `Visible` bindings without a value hide, `@ui`
elements whose `className` lands on their root get `Class` without a `display: contents` wrapper (`kbRootClass`),
memos forgotten after a language or theme change, `DataAttributes` on `Panel` / `Stack`.

Hand-finished (kept by `regen.sh`): `shell/AppearanceDialog.ts` (a pilot code-behind that called a store hook inside a
memo) and `shell/BrandLink.ts` (`hit` read again on every render: the registry re-registers its apps after a language
change; memoizing every registry read made the home page render without end).

### 21.3 What stays React (ReactHost islands), and why

- **Canvases and charts**: `AreaChart`, `BarChart`, `TrendChart`, `Sparkline`, `ReportSeriesChart`, the login
  animation (`LoginAnimation*`) — drawn on `<canvas>` / `<svg>` with refs and pointer tracking.
- **Editors and complex widgets**: `PreviewFrame` (a shadow root with its own React root), `GalleryBody` and the
  theme preview stage (context providers), `EditableCard` and the cards built on it (inline editing with focus
  management), the rules tree and condition editors (`leafKinds`, `detectorLeaf`), `DataTable` with column objects
  (`CategoryBreakdown`), `WatermarkPanel` (popover anchored to a ref), `SettingControl` (a registry of control kinds).
- **Shell plumbing**: `Shell`'s module area (`ModuleArea`, theme scope context), `SettingsMenu` and `LanguagePicker`
  (Radix dropdowns), `SearchBar`, `SidebarNavItem`, `InstanceLogo` (`<img>` / `KubunoLogo`), the shell menus' hosts and
  the workspace dock (`shell/workspace/*`), the setup wizard (`SetupWizard`, `LanguageMenu`, `SetupWordmark`).
- **Elements without a view equivalent yet**: `<img>`, `<input>` outside a field, `<table>`, `<pre>`, `<label htmlFor>`,
  `<a target>`, `autoFocus`, `ref` callbacks, computed `style` objects — 866 parts in all, each with a TODO comment in its
  view naming the reason.

Next: more elements for these (Image, Table, a `Ref` binding), the parts as `.kbcontrol`, `@kubuno/host-runtime` for
module projects in the designer.

## 22. Module migration status: drive (2026-10-06)

`drive/frontend` builds its views with the **published** packages (`@kubuno/views` ^0.1.3, `@kubuno/views-compiler`
^0.1.2, registry of `@kubuno/ui` ^0.1.16, `@kubuno/host-runtime` ^0.1.0 for the designer): `kubuno.views.json`
(`target: web`, host registry from `node_modules`, `design.setup: ["src/index.css", "src/i18n.ts"]`), `kbview()` first
in `vite.config.ts` with `@kubuno/views` shared (import map), `rootDirs` + `.kubuno/views` in `tsconfig.json`,
`typecheck` / `build` through `kbview-tsc -b`, and `viewsAbi = 1` exported by `entry.ts`. The views run on the host's
`@kubuno/views` (no module bundles its own runtime); no import of the core or of another module.

### 22.1 Converted and measured

Every drive view was converted again from its original TSX with `kbview-migrate` 0.1.1 (`regen-drive.sh`, hand fixes
for the codemod defects of §22.4 applied by `fixups.mjs`, both in `E:\kubuno-build\agent-webmig3\`).

| Measure | Result |
|---|---|
| Files | 37: 7 converted, 29 partial, 1 split (`TagUI` → `TagDots`, `TagDialog`, `TagInfoSection`) |
| Elements mapped | 678 / 853 (79 %), 147 parts, 463 `Class` (codemod output; the checkbox-label fix moves 12 elements back to React) |
| `kbview-tsc -b` / `vite build` / `.kbpkg` | 0 errors / built / built (Windows, `build_kbpkg.sh` PowerShell fallback fixed) |
| §7 parity (drive installed in the bench's dev core from its Windows `.kbpkg`, seeded: duplicates, image, zip, video, STL, trashed file, label, lock) | **31 / 31**, worst root diff 0.124 % (open dialog, folder picker); accessibility tree, text and Tab order identical everywhere |
| Commits (drive) | `8c55267`, `7fd3b73`, `affaee9` |

Screens: my files, home (`MobileHome` at 390), starred, shared, recent, trash (with its banner), search results,
« Import from a URL »; duplicates, storage overview, advanced sharing, labels dialog, info panel (labels section), image
edit, archive browser, video player (video masked), 3D viewer (canvas masked), image search, empty search, filter
panel, settings (three tabs), storage page, side-panel mini view, open dialog, folder picker, image picker (Drive tab),
fonts home / family / cart (cart at 1440 only: no button at 390). The details panel (`DetailsPanel`) is dead code
(imported nowhere); the info panel shown is `@kubuno/drive`'s. Bench: `C:\kubuno-build\agent-webmig3\bench3`
(`screens-d1.json`, `screens-d2.json`, `run3.ps1`), reports `C:\kubuno-build\agent-webmig2\bench\captures\diff-d1-before-d1-after\parity.json`
and `…\diff-d2-before-d2-after\parity.json`.

Per group (mapped / total): settings + WebDAV 32/44 (11 parts), storage page 50/63 (10), filter panel 32/32, sidebar
tree 6/33 (23), image edit 43/62 (19), fonts pages 36/51 (14), labels 25/32, archive browser 26/28, 3D viewer 21/23,
image picker source 27/37, dialog breadcrumb + tree 11/15, mini panel 4/6, mobile home 6/9, gauge 4/5, trash banner 6/6.

Left as TSX: `FilesDashboardWidget` (renders nothing: it waits for a `files` module that never exists),
`ModuleMountExplorer` (needs a module publishing a storage mount), and the screens nothing of which maps — `DriveHome`,
file / folder cards and rows, the context menu, the viewers layer, the Files open / save / folder-picker / audio /
paint / remote-storage windows, `FilePreviewOverlay` / `Shell`, `ImagePreviewOverlay`, `FontsExplorer`,
`DualPaneExplorer` (unsupported statements, spreads, local components, `FloatingWindow` props). Irreducible islands:
`StorageExplorer` (`@kubuno/drive`), the virtualised grid cells, drag-and-drop and marquee selection, the viewers.

### 22.2 In the Visual Studio designer

- **Module projects**: `@kubuno/host-runtime` 0.1.0 ships, besides the bundled page, a project-mode design page for a
  module's own dev server and the host's shared modules (`dist/project/shared/*`, one build so each exists once,
  `shared.json` = the import map's table). In `vite serve`, for a project with no `design.entry`, `@kubuno/views-compiler`
  0.1.2 resolves the shared specifiers to those modules (kept external in dependency pre-bundling; production builds
  unchanged); `design.setup` loads the module's CSS and translations first. Without `@kubuno/host-runtime` in
  `node_modules`, the designer stays on the bundled host and its bar says `npm install -D @kubuno/host-runtime`
  (« Réessayer »).
- **The registry errors** (`Label` has no `HtmlTag`) came from the VSIX's bundled design host, whose compiled-in
  registry predated `HtmlTag` — not from the language server nor the C# registry merge. The designer now sends the
  project's own host registry (`setHostRegistry`, re-sent when the file changes) to the bundled page.
- **Junctions**: Vite started from a link path (`C:\kubuno-build` → `E:\…`) answers module requests with
  `index.html` (« code-behind not loaded »). `RealPaths` (`GetFinalPathNameByHandle`; mapped network drives keep
  their letter) canonicalises paths; « Démarrer le serveur de développement » runs the server from the real path (one
  server per real folder) and `design-server.json`'s `root` is compared by real path. vskubuno `f719cc4`, core
  `428348b`.
- **Verified live** (VS 2026, hive `KubunoWebMig3`, solutions opened through `C:\kubuno-build\…`, captures
  `C:\kubuno-build\agent-webmig3\vs\out\`): WaffleMenu / SecurityTab load their code-behind on the project dev server
  (`a2`, `a3`); drive's `DriveApp` and `DuplicatesDialog` open with their code-behind on drive's dev server, Error List
  empty (`b2`, `b4`; bundled `b1`; missing package and retry `b5`, `b6`); `Variant` set in the Properties window
  round-trips to the buffer and the surface (`c3`); a double-click creates `button_click` in `SecurityTab.ts` and opens
  it (`c4`). Headless proof of the module page with HMR: `C:\kubuno-build\agent-webmig3\host-runtime\final-*.png`.

### 22.3 Left

- `ImportUrlModal` renders empty in design mode (shown only when the drive store says it is open): needs sample data.
- A dev server started by hand from a link path is only warned about (Output > Kubuno); taking the real path for
  Vite's root in the views-compiler plugin would fix it there.
- `setHostRegistry` is in core after `@kubuno/host-runtime` 0.1.0 (the VSIX's bundled host has it; the npm package
  gets it in its next version).

### 22.4 Defects found (core), to fix

1. **Regression (core `0b661a0`)**: the converted `Shell`'s `<Slot name="app-dialogs">` (a ReactHost with memoised
   props) never renders again — `Slot` reads `SlotRegistry.getSlot` without subscribing, so dialogs a module registers
   after the first render (Files open / save / folder picker, audio player, paint, remote storage) never appear.
   Fix: `Slot` subscribes to the registry. (The bench works around it.)
2. Codemod: `t(key, { defaultValue })` defaults go to `views-defaults.json`, which nothing loads (drive registers them
   in `entry.ts`).
3. Codemod: an inferred getter type `Folder` collides with the Lucide `Folder` import.
4. Codemod: a helper used only inside a template literal is not copied into the parts file.
5. Codemod: `style={…}` is dropped when the class name is computed.
6. Codemod: `{a} / {b}` is merged into one string (changes the accessibility tree).
7. Codemod: `<label><input/>text</label>` becomes span-wrapped text (LabelText + StaticText in Chrome).
8. Runtime (`@kubuno/views`): a wrapper-root element without `Class` (e.g. `TextField`) sits in a `display: contents`
   div, so the parent's `space-y-*` margin is lost.

3–8 are hand-fixed in drive by `fixups.mjs`.
