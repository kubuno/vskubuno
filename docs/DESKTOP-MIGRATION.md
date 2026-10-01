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
