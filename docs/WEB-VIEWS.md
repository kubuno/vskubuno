# Kubuno web views — `.kbview` for the web UI, designed in Visual Studio — design note

> Status: **design study, nothing built** (2026-10-01). Product-owner decision (2026-10-01): the Kubuno **web** UI
> (core host + module frontends) moves toward a declarative view format similar to the desktop `.kbview`, editable in
> the Visual Studio visual designer (Toolbox, design surface, Properties, ⚡ events, code-behind); the design surface
> renders with **WebView2** (WebKit is dropped); existing React/TSX code is migrated or transformed.
>
> Builds on: `XML_VIEWS.md` (format), `PROGRAMMING-MODEL.md` (`#[kubuno::view]`, the WinForms-like model),
> `DESIGNER.md` (design surface protocol, Toolbox, Properties, element ids), `EVENTS.md` (events, custom controls,
> metadata), `DATA.md` (data components, `{Binding Source=…}`), `RIBBON.md`, `ICONS.md`, `ARCHITECTURE.md` (layers,
> `Kubuno.Web`), `kubuno-resources` (`.kbres`, `{Res}`; `docs/RESOURCES.md` is referenced by the code but not in the
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
   tree of `kubuno-views`), same markup extensions (`{Binding}`, `{Res}`), same `x:Name`, same `On*` event attributes,
   same element ids for the designer, and — wherever the control exists on both sides — **the same element and
   property names and the same enum values**. On the web an element maps to an `@kubuno/ui` (or `@kubuno/sdk`)
   React component; on the desktop to a `kubuno-views` registry component. A view file belongs to **one target**
   (its code-behind is TypeScript or Rust); what is shared is the vocabulary, the tooling and the skills, not files.
2. **WinForms-like authoring in Visual Studio**: Toolbox → design surface → Properties (F4) → ⚡ double-click creates
   a handler in the code-behind, F7/Shift+F7, the `.kbview` buffer as the single source of truth, edits surgical
   (`DESIGNER.md` §2), every gesture one undo unit.
3. **Migrate the existing web UI screen by screen**, never big-bang, with visual parity checked on the live
   instance.

### 1.2 Constraints and how each is met

| Constraint | Answer (details in the referenced section) |
|---|---|
| **Code-behind in TypeScript** | A class per view extending a generated base (`ViewBase`), the TS counterpart of `#[kubuno::view]`: a typed field per `x:Name`, handlers as plain methods, `@bind accessor` fields for bindings, an optional `use()` method where React hooks are allowed (§2.2). |
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

**One grammar, compiled to WASM.** The parser is the Rust rowan parser of `kubuno-views` (lossless CST, `ast`,
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
host's import-map plugin (chunk `kubuno-views`). Inside the core build the same specifiers resolve by alias, as
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
| `#[kubuno::view("main_view.kbview")]` on the struct | `class X extends ViewBase` where `ViewBase` is imported from `./X.kbview` |
| `Form1.Designer.cs` / the macro's in-memory expansion | the Vite plugin's generated module + `.kubuno/views/X.kbview.d.ts` |
| `initialize_component()` | done by `X.component()` on mount: the instance is created once per mounted view (`useRef`), its control handles linked by `x:Name`, initial property values read from the plan (`this.save.text === "Enregistrer…"` before the first paint) |
| a field per `x:Name` (`status: kubuno::forms::TextField`) | a typed handle per `x:Name` (`save: Button` from `@kubuno/views`): `text`, `enabled`, `visible`, `checked`, `value`, `focus()`, `click`… — setting one re-renders only that element (each handle is a slot in the view's store, the same idea as the desktop's composed `__kb.N.Text` bindings) |
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

| `.kbview` element | Web (`@ui` / `@kubuno/sdk`) | Desktop (`kubuno-views`) | Notes / work to converge |
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

All of it lives in `Kubuno.Desktop` today, and the web layer may not reference the desktop layer
(`tests/Kubuno.Architecture.Tests`). Per `ARCHITECTURE.md` ("code moves DOWN"), **WV-8 moves the view designer into a
shared lower layer** — `Kubuno.Views` / `Kubuno.Views.Logic` between Rust and the targets — leaving
`RustDesignSurfaceHost`, the design build of `kubuno_ui.dll` and the desktop templates in `Kubuno.Desktop`; the web
layer adds `WebDesignSurfaceHost`. The same move is already planned for the mobile renderer.

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

Exactly the desktop wire shapes (`kubuno_views::protocol` / `DesignSurfaceProtocol`), carried over
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
`rangeOfElement`, completion, hover, outline, `{Res}` support (`kubuno-resources-model`), diagnostics framework — a
second server would duplicate 7 200 lines and drift.

- **Profile selection** per document: walk up from the `.kbview` — `Cargo.toml` depending on `kubuno`/`kubuno-views`
  → `desktop`; `package.json` depending on `@kubuno/views-compiler` → `web`; an explicit `kubuno.views.json`
  (`{"target": "web"}`) wins.
- **Registry provider**: `desktop` = the compiled-in registry (today); `web` = `node_modules/@kubuno/ui/
  kbview-registry.json` + `@kubuno/sdk`'s + `@kubuno/views`' + scanned project controls. Requires the registry *model*
  (types + JSON loader) to be split from the Windows-only `kubuno-views` (WV-1).
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
| **WV-1** | Split platform-neutral crates out of `kubuno-views`: `kubuno-views-syntax` (lexer, rowan CST, `ast`, `edit`, element ids, validator core) and `kubuno-views-model` (registry types + JSON loader); `kubuno-views` re-exports, no behaviour change | M | WV-0 | Churn in a crate other agents edit daily (`git status` shows concurrent work) — do it in one short window, re-export everything | `cargo test` of `kubuno-views` and `kubuno-views-ls` unchanged; desktop designer smoke test |
| **WV-2** | `@kubuno/views-compiler`: WASM build (Node + browser), Vite plugin (plan + accessors + source maps + `.d.ts` + check files + HMR), `kbview-tsc` | L | WV-1, WV-4 (fixture registry is enough to start) | WASM toolchain in module CI (offline `.kbpkg` builds: the package ships the `.wasm`, no Rust needed downstream) | Golden tests (view → plan), `tsc` errors remapped to `.kbview` lines, HMR keeps state (vitest + browser) |
| **WV-3** | `@kubuno/views` runtime in core: renderer, binding engine (one/two-way, `StringFormat`, `{Res}` via i18next), events → args, `ViewBase`/`component()`/`@bind`/handles, `Repeater`, `Query`/`Mutation`, `Timer`, `KbView`, `defineControl`, `ReactHost`, `MessageBox`/`Dialog`; import-map entry, `viewsAbi` handshake | L | WV-2 | Re-render granularity and React 19 semantics (StrictMode double mount, concurrent rendering) | Conformance suite (compiled vs interpreted DOM equal), React Profiler budget, bundle ≤ 15 KB gzip |
| **WV-4** | Web registry: `*.meta.ts` per family for the existing `@ui`/sdk components, `build:registry`, conformance test vs desktop export + allowlist | M | WV-0 | Metadata drifting from props — mitigated by `satisfies Props` | Test in core CI; JSON diff report |
| **WV-5a** | Core element set on web: `Label`/text styles, `Stack`, `TableLayoutPanel`, `Panel` (dock grid, absolute), `ScrollArea`, `GroupBox`, `SettingsRow`, `RadioGroup`, `Icon`, `PictureBox`, `LinkLabel`, children→props adapters (Tabs, Accordion, Breadcrumb, Stepper, Dropdown, ContextMenu); same names added on the desktop where missing | M | WV-3 | Visual drift from the existing hand-written screens | **Visual**: gallery page per element, light/dark, ltr/rtl, 390/1280 px |
| **WV-5b** | List & navigation elements: `ListBox`, `CheckedListBox`, `ListView`, `TreeView` (virtualised, ARIA), `DataTable` alignment, `Toolbar`, `Sidebar*`, `StatusBar`, `Splitter`, `Popover`, `SearchField`, `MaskedField`, `Avatar`, `PaintBox`; Ribbon promoted to `@ui` on `RIBBON.md`'s classes | L | WV-5a | Ribbon unification touches 4 modules | **Visual** + keyboard/screen-reader pass (NVDA) on lists and trees |
| **WV-6** | i18n: `.kbres` → i18next bundles in the plugin, `{Res}` arguments and plurals (both targets), converter `i18n.ts` → `.kbres` | M | WV-2 | 13 languages × keys: a lossy conversion would be silent | Round-trip test: every key/language identical before/after; RTL screenshot |
| **WV-7** | Language server web profile: profile detection, registry from JSON, `oxc` code-behind reader, `createHandler`/rename/remove/compatible handlers for TS, go-to-definition into `.ts`, web diagnostics, `.d.ts` generation | L | WV-1, WV-4 | `oxc` version churn; TS edits must respect user formatting (insert, never reformat) | Unit + round-trip tests (as the 127 + 18 existing); live in VS |
| **WV-8** | VS: move the designer down to `Kubuno.Views*` (shared layer), desktop keeps its surface host; architecture tests updated | M | — (can start now) | Concurrent edits in `Kubuno.Desktop/Designer` (uncommitted work today) — coordinate, move files without changing behaviour | Designer test suite (343) green; desktop designer smoke test in the experimental instance |
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
   in `use()`), or hooks-only functions? **Recommended: the class**, for parity with `#[kubuno::view]`, reliable
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
desktop counterpart (`kubuno_ui` + `kubuno-views` element), same name and semantics on both sides, pixel-identical
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
5. **Desktop parity**: the same rule holds for desktop modules/apps — reuse goes through `kubuno_ui`/`kubuno-views`
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

Two platform-neutral crates were split out of `kubuno-views` (`desktop/windows/src/crates/`), both in the workspace
and in `Kubuno.Core.Desktop.slnx` under `/Libraries/` (`.rsproj`, `Kubuno.Rust.Sdk/1.1.1`). Neither depends on
`kubuno_ui`, `kubuno_controls`, `windows` or any C library.

| Crate | Depends on | Holds |
|---|---|---|
| `kubuno-views-model` | `serde`, `serde_json` | `meta`: `PropKind`, `PropertyMeta`, `EventCategory`, `Routing`, `EventMeta`, `ArgsChain`, `LevelMeta`, `ChildrenModel`, `LayoutKind`, `DesignTimeAttribute`/`DESIGN_TIME_ATTRIBUTES`; `schema`: the export's wire types (`ComponentJson`, `PropertyJson`, `EventJson`, `PropKindJson`, `ChildrenModelJson`, `LayoutKindJson`, `RegistryExport`, `fnv1a_hex`); `json`: **new** loader `load_registry` → owned `RegistryDocument`/`ComponentEntry`/`PropertyEntry`/`EventEntry` (desktop export and web file, unknown fields ignored, `schema` > 1 rejected, `web`/`typography` kept raw); `editor`: **new** `EditorKind`; `binding_sources`: the binding source schema (`Shape`, `MemberKind`, `Member<L>`, `Context<L, U>`, `ConverterInfo<L>`, `Schema<L, U>`, `Resolution<'a, L>`), generic over the location types |
| `kubuno-views-syntax` | `rowan`, `kubuno-views-model` | `syntax` (lexer, parser, rowan CST, `Diagnostic`, `LineIndex`), `ast` (typed layer, `stable_id`/`resolve_id`), `edit` (all surgical edits, `match_line_endings`); `binding`: the `{Binding}` grammar (`BindingMode`, `UpdateSourceTrigger`, `BindingFormat`, `BindingPart`, `BINDING_KEYS`, `BindingIssue`, `binding_parts`, `canonical_key`, `is_binding_expr`, **new** `BindingSyntax`/`parse_binding_syntax`/`binding_issues`); `res`: the `{Res}` grammar (`RES_PREFIX`, `parse_res_path`, `res_reference`, `is_res_expr`); `ids`: `parent_id_of`, `is_ancestor_id`, `top_level_ids`; `markup`: `x:`/`d:` attributes; `view_kind`: `.kbview`/`.kbcontrol` rules (from `kubuno-views-ls`); `validate`: the registry-independent validator core (`COMMON_ATTRIBUTES`, `check_value`, `is_binding_expression`, `is_root_element`, `distance`/`closest`/`with_suggestion`) |

**What stayed in `kubuno-views`, and why.** `ComponentMeta` (its `build` field is a runtime `fn` returning a
`ViewNode`, and its methods read the runtime class chain `controls::class_of`), the registry *tables* (`COMMON_EVENTS`
and `VIEW_EVENTS` name runtime args types, `LEVELS`, the families), `BindingSpec` (it carries a run-time
`BindingState`; it is now built from `BindingSyntax`), the validator's registry walk (`validate`, `warnings`, `hints`,
`contrast_warnings`: they read `ComponentMeta`, the colour/font grammars of `style` and the icon set), and everything
runtime, rendering, Windows and design mode. A web consumer reads the registry through `json` (the export repeats
every inherited and root-only member on each element, so `ComponentEntry` needs no class chain).

**Paths kept.** `kubuno-views` re-exports `syntax`, `ast`, `edit` as modules and every moved item at its old path
(`registry::PropKind`, `events::ArgsChain`, `registry::export::ComponentJson`, `binding::BindingMode`,
`resources::RES_PREFIX`, `design::parent_id_of`…); `kubuno-views-ls` re-exports `view_kind`'s items and aliases the
binding source types to their LSP instantiation (`type Member = model::Member<lsp_types::Location>`…). No
downstream source changed (the facade `kubuno`, the macros, chat, shell, documents, drive, the gallery,
`view_embed`).

**Verified.** Tests, before → after: `kubuno-views` lib 745 → 673 (the 73 tests of `syntax`, `ast` and `edit` moved with their modules, +1 new: the model loader reads the real export back), its integration tests 7/16/7/1 and 24 doc-tests unchanged; `kubuno-views-syntax` 83 (73 moved + 10 new), `kubuno-views-model` 8 + 1 doc-test (new); `kubuno-views-ls` 156 + 1 + 18 unchanged; `kubuno-views-macros` 25 + 28 doc, `kubuno-views-meta` 14, `kubuno` 25 + 15 doc, `kubuno-chat` 19 unchanged. `kubuno-desktop` (shell) was being edited by another agent during this lot (78 → 82 tests; its new `signout_dialog::the_dialog_says_how_many_changes_wait` fails identically with the original `kubuno-views`, checked on a copy of the tree): 81 pass. Cross-checks of the two new crates (`cargo check --all-targets`): `wasm32-unknown-unknown`,
`x86_64-unknown-linux-gnu`, `x86_64-apple-darwin`, `aarch64-apple-darwin` all clean; `clippy -D warnings` clean on
Windows and on `wasm32-unknown-unknown`. Not done: the desktop designer smoke test in Visual Studio (no VS session in
this lot; the export and `view_embed` compile unchanged, and the export is now also read back by the model loader in a
test).
