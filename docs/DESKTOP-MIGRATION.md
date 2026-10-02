# Migrating the Kubuno desktop apps to `.kbview` views

Goal (user request, 2026-09-30): every existing Kubuno desktop app (shell, chat, documents, drive) must be
editable and buildable in Visual Studio, **views included**, rewriting code where needed, and fixing whatever is
missing in `kubuno_ui`, the `kubuno-views` runtime, the facade and the VSIX along the way.

Sources: the four audits of 2026-09-30 (chat+shell, documents, drive, VS build) and `docs/RIBBON.md`.

## Done

| Item | Where |
|---|---|
| The desktop workspace opens/builds/runs/debugs in VS: `Kubuno.Core.Desktop.slnx`, one `.rsproj` per crate, workspace-scoped cargo build, Test Explorer (2923 tests) | vskubuno 56308dd, `docs/RSPROJ.md`, `docs/GETTING-STARTED.md` |
| Kubuno windows faithful to the web FloatingWindow; every WinForms window kind (dialog, tool, borderless, splash, owned, MDI, flyout, in-window dialog); title-bar slots, custom caption buttons, backdrop, message hooks, live theme | vskubuno 4596fc0 |
| Web docking ported (`DockArea`, `DockPanel`, `WorkspaceShell`, `MenuBar`), pixel-checked against the web | vskubuno 3cd9a6e |
| Ribbon design (every element a control, `Command` component, scaling policies, designer UX) | `docs/RIBBON.md` |
| In progress: build-hash-named `kubuno_ui-<hash>.dll` | — |
| App lot 1, **chat** (2026-10-01): `Application::run(ChatWindow)`; `chat_window.kbview` (title bar, rail, panes) + user controls `ConversationListPane` (Repeater of `ConversationRow`), `ConversationPane` (header, `MessageThread`, composer, empty state, its two menus); `MessageThread` custom control in the crate's lib target; `UiDispatcher` instead of `WM_CHAT`; `WM_COPYDATA` through `Form::on_message`; resources fr/en; 19 tests (state, view model, protocol, API parsing, MessageThread layout). Design build links a package's lib target. | desktop `src/chat`, vskubuno `DesignProjectCrate` |
| App lot 2, **shell** increment 1 (2026-10-01): `Application::run(ShellWindow)`; `shell_window.kbview` (Kubuno chrome, Mica Alt, 64-DIP header through title-bar slots, `Sidebar` rail compact under 520 DIP, hide-to-tray through a cancellable `FormClosing`, message hooks for tray/TaskbarCreated/sync/WM_SETTINGCHANGE) + user controls `LauncherPage`, `SettingsPage`, `AccountsPage` (Repeater of `AccountRow`), `ActivityPage` (Repeater of `ActivityRow`), `LoginPage`, `LabelsPage` (Repeater of `LabelRow`), `StatusPresenter`, `ConfirmDialog` (in-window); `StatusDot`/`StorageGauge` custom controls; resources fr/en; `--sample` offline mode; old frame/painter/hit-test code removed. Designer: `d:Visible="false"` hides on the surface, nested user controls show their instance data. Parity captures ≤ 1 % pixels per page. | desktop `src/shell` |
| App lot 2, **shell** increment 2 (2026-10-01): the launcher and the account panel as `WindowKind="Flyout"` views (`waffle_flyout.kbview`, `user_flyout.kbview`) owned by the window; `AppTileGrid` custom control (tiles, favourites edit, drag-reorder), `PanelMenu` and `AccentPill`. Generic: floating-panel flyouts (`CornerRadius`: Windows.UI.Composition blur clipped to the radius, shadow margin, ported from the shell's `flyout_window.rs` into `kubuno-controls`), `Form::set_client_size` on an open window, `Control::accessible_parts`, `{Res}` on custom properties, owned windows of a top-most window open top-most. Next: admin console. | desktop `src/shell`, `kubuno-controls::host::backdrop` |
| App lot 2, **shell** increment 3 (2026-10-01): the administration console as `AdminPage` hosting one user control per section — `DashboardSection` (`StatCard`, `BarChart`), `UsersSection` (`DataTable` paged by the server, owner-drawn cells), `GroupsSection` (`GroupRow`), `AudiencesSection`, `OrgUnitsSection` (`OrgUnitTree`), `ModulesSection`, `InstanceSettingsSection` (`SettingsCategory`), `StorageSection` (`StorageBlock`, `StackedBar`) — each opened by `AdminSectionHeader` at the `PageAdmin` size; the rail's console tree from `admin.rs`; `--page admin:<section>` restored; the old hand-painted `admin_view` removed. Generic: `Column AutoSizeMode="Fill"`, `DataTable` `OnCellClick` and bound-row `e.index` in `OnDrawItem`, no page-size chooser under manual paging, `Repeater ItemHeightField`, bindable `BreadcrumbItem`, `{Res}` column headers, `Label Role="Page"/"PageAdmin"`, a control measured in its own `Font` (an `AutoSize` label in a `Stack` no longer ellipsizes). | desktop `src/shell` |
| App lot 2, **shell** increment 3 follow-up (2026-10-01): each admin section previews its own design data (`design/*.json` through `d:ItemsSource`, `d:` values on the dashboard's cards); generic: `d:ItemsSource` on a `DataTable`, nested JSON arrays as inner Repeater lists, owner-drawn tables drawn by default in the designer, a `DataTable` too narrow for its columns scrolls sideways (the `Fill` column keeps its minimum) instead of squeezing them. A Debug build under a debugger runs `--sample` by default; `--live` opts out. F5 of the console checked. | desktop `src/shell`, `kubuno-views` |
| App lot 3, **documents** (2026-10-02): `Application::run(DocumentWindow)`; `document_window.kbview` with the declarative `<Ribbon>` converted 1:1 from `ribbon.rs` (208 `<Command>`s shared by the ribbon, the QAT and the shortcuts; Backstage « Informations » as the `BackstageInfo` user control), ribbon pixel-identical to the Rust declaration; custom controls `PageCanvas` (the `doc` engine unchanged, wheel/keys/scroll bars incl. a horizontal one), web-faithful `HorizontalRuler` / `VerticalRuler` / `RulerCorner` (graduations, margins, indent markers, tab stops, drags with guide) and `ZoomSlider`; resources fr/en. Generic: ribbon elements in the accessibility tree (UIA Invoke), `{Res}` ScreenTips, `RibbonComboBox ItemsSource`, `RibbonGallery Display="Inline"`, `Option Label`, command-owned toggle state, `Control::cursor_at`/`tool_tip_at`, `StatusLabel ForeColor`. | desktop `src/documents`, `kubuno-views`, `kubuno-ui` |
| App lot 3 follow-up, **documents editing** (2026-10-02): the engine moved into the platform-neutral `common/kubuno-docs-core` (model, `.kbdoc`, layout/pagination ported from `canvas-engine.ts` with ProseMirror positions, hit-testing, editing commands, Yjs-like undo grouping; builds for wasm32/linux/macOS); `PageCanvas` edits (caret, selection across pages, typing, IME placement, clipboard, context menu), ribbon commands wired with checked states from the selection, ruler edits undoable, pages side by side; `--doc <id>` opens a server document through the shell's broker with the digest-guarded save session on a worker thread, crash journal, Kubuno `ConfirmDialog` for recovery and conflicts. | desktop `common/kubuno-docs-core`, `src/documents`, `docs/DOCUMENTS-EDITING.md` |
| App lot 2, **shell** increment 4 (2026-10-02): the header's menus as shared user controls in the new crate `desktop/windows/src/crates/shell-controls` — `WaffleMenu` (favourites card, edit mode, `AppTileGrid`), `AccountMenu` (`PanelMenu`, `AccentPill`), the header buttons `WaffleButton` / `AccountButton` that open them in an out-of-window popup, and the web's `HeaderActions` cluster; data through `LauncherService` / `AccountService`; names shared with the web (`docs/SHELL-CONTROLS.md`). Generic: `kubuno::popup` (a control in a top-level floating panel anchored to a control, flip and work-area clamping), `host::screen_geometry`, a hidden docked panel child no longer counted in the panel's measure. The shell's `waffle_flyout` / `user_flyout` views are gone (one code path: the buttons). Parity: as user controls inside the former flyouts 0.000 % pixels (light/dark, 100/175 %, open, editing, dragging, dropped, account panel and its hover); through the popup buttons the panel chrome and the account panel stay at 0.000 % (the tiles changed with the web-logo work of the same day). `examples/header_demo.rs` (320 × 200) shows the popups leaving the window, flipped above it at the screen's bottom and clamped at its edges. | desktop `src/crates/shell-controls`, `src/shell`, `kubuno::popup` |

Decisions: the desktop keeps the system font (Plus Jakarta Sans stays web-only); property-element syntax
(`<RibbonTab.ScalingPolicy>`) is adopted in `.kbview`; drive code ported from Files stays MIT-noticed when it moves
into AGPL crates.

## Foundations (ordered)

| Lot | Content | Needed by |
|---|---|---|
| F1 | `kubuno-views` gaps: **repeater** (`ItemsSource` + `ItemTemplate` UserControl, virtualised), **typed access to custom controls** (typed fields / `with::<T>()`, list/object properties) incl. **controls from other crates**, **sub-menus** (MenuItem children, icons, separators, programmatic `show`), `Sidebar`/NavigationView, `StatusBar`/`StatusLabel`, `Avatar`/`PictureBox`, `Popover`/Flyout element, Stack wrap/fill + `TableLayoutPanel`, bindable `ForeColor`/`BackColor`, `ItemHeight`, auto-growing `TextArea`, Splitter drag, DataTable paging/density/loading | chat, shell, drive |
| F2 | Localisation in views (`{Res Key}` + design-time language) — **done 2026-10-01**: `.kbres` resource files, `kubuno::resources!`, live culture switch, resource editor, Select Resource dialog, drive `.resw` converter (`docs/RESOURCES.md`; drive itself not migrated yet) | drive (49 languages), all |
| F3 | Virtual regions (a node exposes selectable/routable sub-element rects) + binding of structural children | ribbon, toolbar, tabs |
| F4 | Ribbon family RIB-1..8 (`docs/RIBBON.md`) + `Command` component + smart-tag infrastructure in vskubuno | documents |
| F5 | `kubuno-canvas` crate extracted from `drive-app-controls` (canvas, renderer, geometry, themes, themed icons) | drive, whole stack |

## Apps (ordered)

1. **chat** — `Application::run`, 3 views, `MessageThread` custom control in a lib target, dispatcher instead of `WM_CHAT`.
2. **shell** — window + header + rail + pages, then flyouts (waffle, user panel), then admin console.
3. **documents** — `PageCanvas`/`Ruler` custom controls, `main_view.kbview` with the ribbon, backstage.
4. **drive** — one `.kbview` UserControl per Files XAML UserControl (navigation/address toolbar, sidebar, status bar, info-pane sections, Home widgets QuickAccess/Drives/RecentFiles, settings cards/expanders, property pages), reused across views like in Files; dialogs and Properties first, main window on the host wrapping the current engine, then region by region; `FileItemsView`/`ColumnsView` custom controls; one `.kbview` per Files `.xaml`.

Each app lot: before/after screenshots (dark/light, 100 % and 175 %), UIA check, opens in the VS designer, F5.

## Source layout

Every desktop app crate (`shell`, `chat`, `documents`, and `drive` when it migrates) groups its `src` by **role**,
so that a human finds a file by what it is, not by scrolling a flat folder (user request, 2026-10-02). The layout is
the same in every app; a folder exists only when it holds something.

### Folders

| Folder | Holds | Examples |
|---|---|---|
| *(root)* | `main.rs` (the `Program.cs`) and `lib.rs` (the module tree, the `pub use` of the classes, `resources!`) — nothing else | |
| `views/` | the top-level views (`.kbview`): the main window, its dialogs, flyouts, tool windows | `views/shell_window.kbview` + `.rs`, `views/confirm_dialog.kbview` |
| `pages/` | the user controls (`.kbcontrol`) that make up the main window's content — pages, panes, sections — **and the item templates their lists repeat**, next to them | `pages/accounts_page.kbcontrol`, `pages/account_row.kbcontrol` |
| `<feature>/` | a feature area big enough for its own folder (roughly six views or more, or non-UI logic of its own): its views, its item templates, its `design/` data; the feature's own logic is the folder's `mod.rs` | shell `admin/` (`mod.rs` = the console's structure) |
| `controls/` | the building blocks views place: custom-drawn controls (`#[derive(Component)]`, code only) and user controls placed as plain elements by several views. A sub-folder for a family (`controls/ruler/`) | `controls/status_dot.rs`, `controls/status_presenter.kbcontrol` |
| `model/` | what the views show and raise, pure and unit-tested: state, `view_model`, event arguments; or the domain model | shell `model/{view_model, events}`, chat `model/` (state) + `model/view_model.rs` |
| `services/` | the work behind the views, no UI: server clients, the sync engine's door, sessions, settings, background loops | shell `services/{backend, session, sync, …}`, chat `services/api.rs` |
| `platform/` | OS integration the views do not touch: Explorer, Cloud Files, tray, pickers, protocol handlers, drawing surfaces | shell `platform/{cloudfiles, explorer, tray, …}`, documents `platform/painter.rs` |
| `resources/` | the `.kbres` sets (`resources.kbres`, `resources.fr.kbres`, …) | `kubuno::resources!(pub Resources, "resources/resources.kbres")` |
| `design/` | design-time data (`d:ItemsSource="design/x.json"`), **next to the views that name it** | `admin/design/users.json` |

An existing domain folder keeps its name when it says more than the generic role (documents keeps `doc/`, `edit/`,
`model/`, `api/` — `api/` plays the `services/` role).

### Rules

1. **A view and its code-behind share a folder and a stem** (`pages/login_page.kbcontrol` + `pages/login_page.rs`):
   Solution Explorer nests one under the other (`Kubuno.Rust.Sdk` `DependentUpon`, same folder), F7 opens the same-stem
   `.rs`, and the macros' paths (`#[user_control(view = "login_page.kbcontrol")]`, `#[kubuno::view("x.kbview")]`) stay
   bare file names. Never split them.
2. **An item template lives next to the list that repeats it**, not in a separate `rows/` folder; a user control placed
   by several views goes to `controls/`.
3. **Folders are modules declared with `mod.rs`** (`pub mod x;` lines, plus a `//!` saying what the folder holds) —
   it keeps a folder self-contained in Solution Explorer. In a library crate the modules are `pub`.
4. **Paths in code name the module path** (`crate::services::backend`), never a re-export at the crate root to fake
   the old flat paths; `lib.rs` re-exports only the classes (`pub use pages::login_page::LoginPage;`).
5. **Relative paths in files are relative to the file that names them**: `include_str!`/`include_bytes!` to the Rust
   file, `d:ItemsSource`/image paths to the view, `File="../../assets/app.ico"` in a `.kbres` to the `.kbres`.
6. **Module isolation is unchanged**: folders never reach into another app's crate.

### What depends on file locations (checked 2026-10-02)

| Mechanism | Behaviour with folders |
|---|---|
| `#[kubuno::view]`, `#[user_control(view = …)]`, `kubuno::resources!` | resolved like `include_str!`, relative to the declaring file; fallback: the unique match under `src` |
| A view naming a user control or custom control of another folder | the macros, `kubuno-views-ls` and the design build scan every `.rs` of the package recursively (`target`, `obj`, `bin`, hidden folders and nested packages skipped) |
| `d:ItemsSource` and image paths of a **nested** user control | resolved against the user control's own folder (`ClassRegistration::view_dir`, absolute, registered by the derive), not the folder of the view nesting it — added 2026-10-02 |
| `{Res}` in the designer and the language server | every `.kbres` of the package, recursively (set name = file stem) |
| Solution Explorer nesting | same folder + same stem (`Sdk.targets`) |
| Add New Item (all Kubuno view and control templates) on a folder | the files land in the folder; the wizard declares the module in the folder's `mod.rs` (or `<folder>.rs`), creating `mod.rs` — declared by its parent, up to the crate root — when the folder is not a module yet; visibility follows the sibling declarations (`pub` in a library) |
| F7 / code-behind discovery / handler insertion | same folder, same stem first, then the other `.rs` of the folder |
| Test Explorer | test names follow the module path (`kubuno_shell::services::session::tests::…`) |

### Layout of each app

**shell** (`kubuno-desktop`, 2026-10-02): `views/` (shell_window, confirm_dialog,
signout_dialog) · `pages/` (launcher_page, settings_page, accounts_page + account_row, activity_page + activity_row,
labels_page + label_row, login_page) · `admin/` (`mod.rs` = former `admin.rs`; admin_page, admin_section_header, the
eight `admin_<section>` user controls, the item templates group_row, settings_category, storage_block; `design/*.json`)
· `controls/` (bar_chart, org_unit_tree, stacked_bar, stat_card, status_dot,
status_presenter, storage_gauge) · `model/` (view_model, events) · `services/` (backend, session, sync, apps, activity,
settings, favorites, options) · `platform/` (cloudfiles, explorer, tray, folder_picker, actions) · `resources/`.

**chat** (`kubuno-chat`, 2026-10-02): `views/chat_window` · `pages/` (conversation_list_pane + conversation_row,
conversation_pane) · `controls/message_thread` · `model/` (`mod.rs` = former `model.rs`, `view_model`) ·
`services/api` · `platform/protocol` · `resources/`.

**documents** (`kubuno-documents`, 2026-10-02): `views/` (document_window, `view_tests.rs`) · `pages/backstage_info` ·
`controls/` (page_canvas, zoom_slider, `ruler/`) · `model/` (the stored document, `node`, `state` = former `state.rs`)
· `doc/`, `edit/`, `api/` (unchanged) · `platform/painter` · `resources/`.

**drive** (when it migrates): one `.kbcontrol` per Files `.xaml` UserControl, grouped as Files groups them —
`views/` (main window, dialogs, Properties), `pages/` (Home, settings pages, layouts) with a `<feature>/` folder per
big area (`sidebar/`, `infopane/`, `settings/`, `properties/`), `controls/` (FileItemsView, ColumnsView, and the
reused toolbar/status bar controls), and the existing `drive-*` crates for the non-UI layers.

### Shared control libraries

A control used by several apps (the header's menus) lives in a **shared library crate** under
`desktop/windows/src/crates`, never in an app crate: `shell-controls` (`WaffleMenu`, `AccountMenu`, `WaffleButton`,
`AccountButton`, `HeaderActions`, and their parts). It follows the same layout (`controls/` with each `.kbcontrol`
next to its code-behind and `design/` data, `model/`, `resources/`), depends on the `kubuno` facade only, and takes
its data from the app through traits (`LauncherService`, `AccountService`). Its name must not start with `kubuno`:
the macros, the language server and the designer skip `kubuno*` crates when they look for an app's control
libraries. An app adds it as a path dependency; its controls then show in the Toolbox and the designer.
How to wire it in an app's header: `src/crates/shell-controls/README.md`.
