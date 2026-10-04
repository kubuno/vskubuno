# Shell controls — `WaffleMenu`, `AccountMenu` and the header (shared contract, web and desktop)

User request (2026-10-02): « le wafflemenu du core doit être un usercontrol », then « ce menu aussi doit être un
usercontrol » (the account menu). Both are **user controls** (`.kbcontrol`) on both targets, with the same names,
properties, events and data shapes, so a view written for one target reads the same on the other and the designer
shows the same members. This file is the contract; each target documents its own implementation (desktop: crate
`kubuno-desktop-shell-controls`, `desktop/windows/src/crates/kubuno-desktop-shell-controls/README.md`; web: core/frontend).

**What is inside the user control, and what is not.** The user control is the panel's *content*: everything the
user reads and clicks. The *chrome* that carries it stays with the host, because it differs per platform: on the
desktop a `WindowKind="Flyout"` window (28-DIP corners over a blur of what is behind, its tint, its shadow, closing
on a click outside), on the web a popover (Radix, a backdrop, `rounded-[28px]`, the elevation shadow). The host
also places it (under the header's button), caps its height (the room under the header) and closes it.

Names below are the XML/registry names (PascalCase). Desktop fields are their snake_case (`Apps` → `apps`,
`AppLaunched` → `app_launched`, raised with `raise_app_launched`); on the web, properties and event props follow the
web's own casing of the same names (`apps`, `onAppLaunched`).

## 1. `WaffleMenu` — the app launcher

The favourites card (title « Vos favoris » and its pencil; while editing « Annuler », « OK » and « Glisser-déposer
des applis ») over every other app, three tiles to a row, scrolling as one. 360 DIP/px wide.

### Properties

| Name | Type | Meaning |
|---|---|---|
| `Apps` | list of `LauncherApp` | The apps to show, in the server's order. |
| `Favorites` | list of string | The favourites **exactly as the server holds them** (`preferences.waffle_favorites`): ids of no app of `Apps` are kept (an edit carries them through, appended after the shown ones). |
| `Editing` | bool | The favourites are being edited. The pencil sets it; « Annuler », « OK » and Escape clear it. |

### Events

| Name | Args | Raised when |
|---|---|---|
| `AppLaunched` | `{ id: string }` | An app's tile is clicked outside the edit mode. The host closes its panel. |
| `FavoritesEdited` | `{ favorites: string[] }` | « OK » confirms an edit: the list to save (shown favourites in order, then the unknown ids). |
| `EditModeChanged` | `{ editing: bool }` | The edit mode starts or ends. |
| `ContentHeightChanged` | `{ height: number }` | The content's height changed (edit mode, a favourite added/removed): the host resizes its panel. A web host whose panel sizes itself with CSS may ignore it. |
| `CloseRequested` | none | Escape outside the edit mode (Escape inside it first abandons the edit). |

### Data

```text
LauncherApp { id: string, label: string, icon: string, logo?: string }
```

- `id`: the server's `sidebar_items[].id` — the key favourites are stored under.
- `icon`: a Kubuno icon name (a module logo such as `DriveLogo`, or a Lucide glyph); unknown → `Cloud`.
- `logo`: a logo the server serves (desktop: the cached file's path; web: its URL); wins over `icon`.
- `module` / `moduleLabel` (both targets): a module with several apps — Office, PaintSharp — has its other apps
  grouped under its label (desktop `Tile.module` / `Tile.module_label`).
- Web only: `Icon` (the icon component a module registered in `WaffleAppRegistry`; wins over `icon` and `logo`),
  `href` (the address the tile opens: tiles are links, a middle click opens a new tab).
- Web only, on `WaffleMenu`: `ShowMarketplace` / `MarketplaceHref` (« Plus de modules », for whoever may manage the
  marketplace) and the event `OpenMarketplace`.

### Service

The host may hand the control a **launcher service** instead of setting `Apps`/`Favorites` itself; the control then
launches and saves through it (and still raises its events):

```text
LauncherService {
  apps(): LauncherApp[]
  favorites(): string[]
  launch(id: string): void
  saveFavorites(favorites: string[]): void
}
```

Desktop: the Rust trait `kubuno_desktop_shell_controls::LauncherService`, given with `WaffleMenu::set_service(Rc<dyn …>)`.

### Rules (both targets)

- At most 9 favourites (`FAV_MAX`): a tenth added at the end drops itself, as on the web.
- A click on a favourite removes it while editing; a click on another app adds it; a tile carried with the mouse
  lands in front of the favourite it is dropped on (an insertion bar in that favourite's left gutter), or leaves the
  favourites when dropped on the other apps.
- With no favourite, the card is its header alone outside the edit mode, and a dashed « Faites glisser vos applis
  ici » zone while editing; « Toutes les apps sont dans vos favoris » when every app is a favourite while editing.
- Height: at most 580 outside the edit mode (the host's cap); the edit mode takes the room it needs.
- The other apps: the apps of single-app modules sorted by label (`localeCompare`: case and accents ignored), then
  one group per module with several apps (an 11 uppercase label, `pt-2 pb-1`, `mt-1`), groups sorted by label and
  their apps by label. Geometry: the card 10 from the panel's interior on both sides (the scroll bar's gutter
  included on the trailing side), `mt-[10px]`, `mb-[15px]`; the other apps inset 26 (`--kb-waffle-inset`).
- An administrator's launcher lists the console: tile `core-admin`, « Administration », the web's `admin-logo.png`
  (desktop: it opens the shell's console page).
- A module with ONE app at its root route is keyed by the module's id (Mail's `mail-inbox` entry is the tile and the
  favourite `mail`); a favourite an older desktop saved under the entry's id is mapped to it when read.

## 2. `AccountMenu` — the account panel

The header's account panel: the active account's address and a close button (pinned), then, scrolling under it,
its avatar, « Bonjour <first name> ! », « Gérer votre compte », the other accounts (a click opens one), then
« Ajouter un compte », « Étiquettes », « Administration » (when allowed) and « Se déconnecter ». 320 DIP/px wide.

### Properties

| Name | Type | Meaning |
|---|---|---|
| `User` | `AccountUser` | The active account. |
| `Accounts` | list of `AccountEntry` | The OTHER accounts (the active one excluded). Empty: no accounts card. |
| `ShowAdmin` | bool | Lists « Administration » (the account may enter the console). The host still checks the privilege when it handles `OpenAdmin`. |

### Events

| Name | Args | Raised when |
|---|---|---|
| `ManageAccount` | none | « Gérer votre compte ». |
| `OpenAccount` | `{ id: string }` | An other account's row: switch to it (desktop) / open it (web: switch, or a new tab for another instance). |
| `RemoveAccount` | `{ id: string }` | « Supprimer » on a dead session's or another instance's row. |
| `AddAccount` | none | « Ajouter un compte ». |
| `OpenLabels` | none | « Étiquettes ». |
| `OpenAdmin` | none | « Administration ». |
| `SignOut` | none | « Se déconnecter ». |
| `ChangeAvatar` | none | The camera button on the avatar (desktop: the shell opens the web's `/settings`, where the photo is cropped and uploaded). |
| `ContentHeightChanged` | `{ height: number }` | The content's height changed (the accounts' card folded or unfolded): the host resizes its panel; a web host sizing with CSS may ignore it. |
| `CloseRequested` | none | The close button, or Escape. |

The control raises the event and does nothing else: navigation, switching, signing out (with its confirmation) and
closing the panel are the host's.

### Data

```text
AccountUser  { name: string, email: string, initials: string, avatar?: string }
AccountEntry { id: string, name: string, email: string, server: string, initials?: string, avatar?: string, connected: bool }
```

- `name`: the display name (« Camille Martin »); the greeting uses its first word.
- `avatar`: desktop a cached file path, web a URL; absent → the initials on the accent.
- `server`: the instance's host (`kubuno.asso-exemple.org`); the desktop row shows it under the name.
- `connected`: false for a session that died (web: « Déconnecté » + reconnect). The desktop lists only usable
  accounts and passes `true`. On the web, « Connexion » on such a row raises `OpenAccount` too: the host knows the
  session is dead and opens the add-account dialog pre-filled.
- `remote` (an account of another Kubuno instance: its host on a pill, « Ouvrir » raises `OpenAccount`, « Supprimer »
  `RemoveAccount`) and `unread` (that account's unread notifications, a badge on its row): both targets (desktop
  `AccountEntry.remote` / `.unread`).
- On `AccountMenu`, both targets: `Busy` (a switch is under way: the rows are disabled), `AvatarBusy` (an upload is
  under way: the camera is disabled). Desktop only: `Expanded` (the accounts' card unfolded, `true` by default — the
  web keeps it as internal state). Web only: `ManageHref` / `LabelsHref` / `AdminHref` (those entries are links; a plain click
  raises the event, a middle click opens the address). « Se déconnecter de tous les comptes » replaces « Se
  déconnecter » when the browser holds other accounts of this instance (the host's `SignOut` signs them all out).

### Service

```text
AccountService {
  user(): AccountUser
  accounts(): AccountEntry[]
  canAdminister(): bool
  act(action: AccountAction): void      // optional (no-op by default): what an AccountButton's panel picked
}
AccountAction = ManageAccount | OpenAccount(id) | RemoveAccount(id) | AddAccount | OpenLabels | OpenAdmin | SignOut | ChangeAvatar
```

Desktop: the Rust trait `kubuno_desktop_shell_controls::AccountService` (+ enum `AccountAction`), given with
`AccountMenu::set_service(Rc<dyn …>)`. An `AccountMenu` placed in a view raises its events; the `AccountButton`
(§3), which creates its menu itself, hands the same picks to `act`.

## 3. Header buttons and `HeaderActions`

Three more user controls, so an app drops ONE control in its header and gets the button plus its panel:

| Name | What | Properties | Events |
|---|---|---|---|
| `WaffleButton` | the 36 round waffle (`LayoutGrid`); a click opens a `WaffleMenu` in a popup (§4) | `PopupAnchor` (`Button` \| `Window`), `PopupOffset`, `PopupMargin`, `PopupBottomGap` | `PopupOpened` |
| `AccountButton` | the 36 avatar (photo, else initials on the accent); a click opens an `AccountMenu` in a popup | `UserName`, `Initials`, `AvatarPath` (bindable; else taken from the service), the four `Popup…` | `PopupOpened` |
| `HeaderActions` | the web's right-hand header cluster, in the web's order: bell (+ counter), settings, help, waffle, avatar — 36 circles, no gap, the avatar 2 further (`ml-0.5`); a hidden item leaves no hole | `ShowNotifications`, `ShowSettings`, `ShowHelp`, `ShowWaffle`, `ShowAccount` (all `true` by default), `Minimal` (waffle + avatar only: the web's search mode), `UnreadCount` | `NotificationsClicked`, `SettingsClicked`, `HelpClicked` |

- Data: the buttons take the app's `LauncherService` / `AccountService` — per button (`set_service`) or once per UI
  thread (`set_default_launcher`, `set_default_accounts` on the desktop; the web reads its stores).
- `PopupAnchor="Button"` (default): under the button, right edges aligned, `PopupOffset` (4: the web's
  `sideOffset`) below it. `PopupAnchor="Window"`: at the window's top-right corner, `PopupMargin` (8: `right-2`) from
  its right edge, `PopupOffset` below its client top — the shell's header, whose panels line up with the window.
- A click on the button while its panel is open closes it (the panel loses the focus) and does not reopen it.
- **Other apps** (desktop, 2026-10-02): an app that is not the shell (Documents, Chat) gets both services from the
  shared crate `kubuno-desktop-header-data` (`desktop/windows/src/crates/kubuno-desktop-header-data`, see its README): one
  `kubuno_desktop_header_data::start(...)` in the window's `Load`, with `ShowWaffle="true" ShowAccount="true"` on the view's
  root. A worker borrows the shell's current account from the token broker, fetches `/api/v1/modules` and
  `/api/v1/me`, keeps them under `<data>/accounts/<key>/blobs/header/` (offline-first), follows the broker's events
  and re-installs the services at each snapshot. Picks: the app's own tile brings its window forward, an installed
  desktop app (Chat, Drive) starts, any other app opens its web route (never in a sandboxed run); another account
  is switched to through the broker (`switch_account`); « Gérer », « Étiquettes », « Administration » open
  `/settings`, `/labels`, `/admin` on the web; « Ajouter un compte » and « Se déconnecter » belong to the shell, which
  has no request for them yet (follow-up: a broker `show_page` request). `--sample`, or a Debug run under a
  debugger, shows the design data. The shell's `parse_modules`, favourites' migration and logo cache moved there.

## 4. Popups that leave their window

User requirement (2026-10-02): « le WaffleMenu et l'AccountMenu ne doivent pas rester enfermés dans la fenêtre qui
les contient, ils doivent pouvoir s'afficher à l'extérieur ».

- **Desktop**: the panel is a top-level window of its own — `kubuno_desktop::popup::Popup` (facade): a code-built
  `WindowKind="Flyout"` form holding the user control (docked to fill), `CornerRadius` 28 over the blur
  (`kubuno-desktop-controls::host::backdrop`), its shadow margin, the theme's tint, owned by the anchor's window (above it,
  top-most when it is), closing when it loses the focus (click outside, another window activated) and on Escape
  (`on_escape` lets the content abandon an edit first). `kubuno_desktop::popup::place` puts it next to the anchor on the
  side `Placement` names (`BottomEnd` by default), flips it to the other side when that one has more room, and clamps
  it into the work area of the anchor's monitor (`host::screen_geometry`: per-window DPI, any monitor, negative
  coordinates included). Its height is capped by the room below the anchor (the monitor's, not the window's). It
  takes the keyboard focus when it opens; Windows gives it back to the owner when it closes.
- **Web**: there is no outside of the browser window, so the equivalent is a **portal/popover rendered above the
  page** (`createPortal` to `document.body`, or Radix `Portal`), positioned against the trigger with collision
  handling (`avoidCollisions`, `collisionPadding`), and **never clipped by a container's `overflow`** (no rendering
  inside a scrolled or `overflow-hidden` parent). The web `WaffleButton` / `AccountButton` / `HeaderActions` render
  their panels that way.

## 5. Title-bar header (desktop, as built 2026-10-02)

User requirement: « la barre de titre des fenêtres Kubuno desktop doit prévoir l'ajout ou pas de ces menus (ou d'autres
boutons) comme sur la version web ». A Kubuno window's title bar IS its header: the standard items are Form properties,
and three free regions take any control. Everything sits in the title bar's own row, left of the caption buttons
(`[icon][left][title … drag area … ][centre][right][standard items] [_][□][X]`), mirrored right to left.

### Form properties (the view's root, category « Barre de titre »)

| Property | Item | Event on the view (root) |
|---|---|---|
| `ShowSearch` | the web header's magnifier (an `IconButton`, `Search`) | `SearchClicked` (`OnSearchClicked`) |
| `ShowNotifications` | the bell, with `UnreadCount` (bindable, 0 = no counter, `9+`) | `NotificationsClicked` |
| `ShowSettings` | the settings button | `SettingsClicked` |
| `ShowHelp` | the header's help button (not the caption's `?` of `HelpButton`) | `HelpClicked` |
| `ShowWaffle` | the app launcher (opens the `WaffleMenu` popup itself) | — |
| `ShowAccount` | the avatar (opens the `AccountMenu` popup itself) | — |

- **Defaults: off for every window kind** (Form, Dialog, ToolWindow, Splash, Flyout, MdiChild); what the view writes
  wins, a binding (`ShowWaffle="{Binding SignedIn}"`) counts as shown for the room it takes. Not on by default for
  main windows: every existing app window would grow a header it never asked for, and an app that does not link the
  shell controls would get a diagnostic for nothing. A splash screen, a flyout or a borderless window has no Kubuno
  title band: the items are not shown there (nor under `Chrome="System"`).
- Code first: `form.set_header_items(HeaderItems { waffle: true, account: true, ..Default::default() })`
  (`HeaderItems::ALL`), `form.set_unread_count(n)`, `form.search_clicked()`, `notifications_clicked()`,
  `settings_clicked()`, `help_clicked()` (facade `kubuno_desktop::Form`).
- **Composition** (`kubuno_desktop_views::window::HeaderSpec`, `build_header_items`): the root `<Panel>` builds, after its own
  children, the search `IconButton` and one `<HeaderActions>` element **by class name** (`HEADER_ACTIONS_CLASS`) with the
  view's `Show…`, `UnreadCount` and handler names copied on it (`OnNotificationsClicked="bell_click"`, the view's own
  handler: a click reaches the form's code-behind, or a code-first subscriber, as any event of the view). The framework
  never depends on the shell-controls crate: the class is registered when the application links it. Not registered:
  the language server warns on the first `Show…` attribute (« … need the `HeaderActions` user control: add the shared
  crate that provides it … »), the run time logs it and leaves the cluster out, and the designer shows a placeholder
  (`<HeaderActions>` hatched, with that reason). `ShowSearch` alone needs nothing.
- The items are not elements of the document (ids under `header!`): never selected, moved or serialized; a click on
  them in the designer selects the view, whose Properties window shows the switches.

### Geometry (web header tokens)

- The cluster ends the **right** region, after the view's own `TitleBar.Region="Right"` controls (the web's module
  slots come before the bell), immediately before the caption buttons (the chrome's 10 DIP `gap-2.5` plus the
  cluster's own 8 DIP trailing gap). Search and cluster are joined (no gap, the web's `gap-0`); the regions' own
  controls are `gap-1` (4) apart.
- **Height (2026-10-04)**: a view showing any of these items, or holding a `HeaderActions` / `WaffleButton` /
  `AccountButton` in a `TitleBar.Region`, gets the tall header by default (`TitleBarStyle="Tall"`, 64 DIP, the web's
  `h-16`); every other window gets the standard 32-DIP band (`EVENTS.md` §20). `TitleBarStyle="Standard"` or an
  explicit `TitleBarHeight` overrides it.
- Sizes follow the band: in the tall header (64, or any `TitleBarHeight` ≥ 56) the buttons are the web's **36** circles
  (`w-9 h-9`, glyph 18), the avatar 2 further (`ml-0.5`); in a lower band (`TitleBarStyle="Standard"`, a tool window)
  they take the compact size, **30** (`HeaderActions Compact="true"`, glyph 16). The cluster is as tall as the band and
  centres its buttons, as the caption buttons are centred (Windows-style ones fill the band's height); widths: 160 /
  190 for all five.
- **Look**: a band carrying the items and coloured by nobody takes the web header's ground (`Background`, the
  `var(--body-bg)` token) and the text colour for the title and caption buttons, light and dark. On a coloured band
  (`TitleBarBackground`, `AccentColor`, or a ribbon whose tab strip it continues — Documents) the items take the band's
  ink (`ForeColor` = `TitleBarForeground`, else `OnPrimary`) and the avatar its pale accent tint (`AvatarTint="Accent"`).
  Hover: the controls' own round wash (`hover:bg-surface-3`).
- **Free regions**: `TitleBar.Region="Left" | "Center" | "Right"` on any child of the root (`IconButton`,
  `DropDownButton`, `SplitButton`, a `TextField`/search field, a user control). The centre region is centred on the
  window but pushed aside (then narrowed, its control cut to it) so it never covers the left or right region
  (`window_chrome::layout`). `RightToLeftLayout="true"` mirrors the regions and their order (from the region's right
  edge), and the cluster (`RightToLeft="true"`: the avatar next to the caption buttons).

### Behaviour

- **Hit-testing** (checked with `WM_NCHITTEST` on the example window): `HTCLIENT` on every item and region control
  (they are title-bar holes), `HTCAPTION` between the title and the controls and in the gap before the caption buttons
  (drag, double-click maximises — checked live), `HTMAXBUTTON` on the maximise button (snap layouts).
- **Keyboard**: **F6** (Shift+F6) moves the focus from the page to the first focusable control of the title band and
  back to the control that had it; **Alt** pressed alone does the same in a view without a menu bar (with one, Alt keeps
  entering the menu bar). Tab reaches the band's controls after the page's.
- **UIA**: the items are ordinary controls of the view's accessibility tree, named by their tooltips (« Rechercher »,
  « Notifications »…), under the window.
- **Popups**: the waffle and the avatar open their panels below the button, outside the window (§4).

### Designer (Visual Studio)

- Selecting the title bar selects the view: the Properties window lists `ShowSearch` … `ShowAccount`, `UnreadCount`
  (« Barre de titre ») and the four events (⚡); switching one rewrites the root attribute and the surface recompiles
  live (the cluster, or its placeholder when the project does not link the shell controls).
- Dragging a Toolbox control over the window shows the band's three **drop zones** (dashed; the one under the pointer
  washed with the accent; mirrored right to left); a drop inserts the control as the root's last child with
  `TitleBar.Region` (an `IconButton` gets the band's button size: `Diameter`/`Width`/`Height` 24 in the standard
  32-DIP band, 30 in a band of 40 or more, 36 in the tall header).
- With the view selected, a **smart tag** at the window's top-right corner opens the title bar's tasks: « Ajouter un
  bouton à la barre de titre » (à gauche, au centre, à droite: an `IconButton`, then selected) and one switch per
  standard item (checked when shown; switching off removes the attribute). Each task is one undo unit
  (`TitleBarDesignerTasks`).

Example: `kubuno-desktop-shell-controls/examples/form_header.rs` (`--dark`, `--compact`, `--rtl`, `--minimal`, `--accent`): a
code-first Form with every item, a menu button on the left and a search field in the centre.

## 6. Platform notes

| | Desktop (`kubuno-desktop-shell-controls`) | Web (core/frontend) |
|---|---|---|
| Host chrome | `kubuno_desktop::popup::Popup` (a `WindowKind="Flyout"` window, §4), opened by `WaffleButton` / `AccountButton`; the shell's header uses them (`PopupAnchor="Window"`) | Popover in a portal (§4) |
| Strings | the crate's own `resources/shell_controls.kbres` (+ `.fr`): `launcher_*`, `account_*`, `header_*` keys, read with `{Res key, Source=shell_controls}` | the core's i18n (`shell.*`) |
| Designer data | `controls/design/apps.json` (12 apps), `controls/design/accounts.json` (3 accounts) | none yet (the views' design-time data, WV-10) |
| Differences today | see §8 | see §8 |

## 7. Web as built (core pilot, 2026-10-02)

Files: `core/frontend/src/core/shell/menus/` — `WaffleMenu.kbcontrol` + `WaffleMenu.ts`, `AccountMenu.kbcontrol` +
`AccountMenu.ts` (the user controls), `AppTileGrid.tsx` and `MenuLink.tsx` (custom controls used inside them),
`WaffleButton.tsx` and `AccountButton.tsx` (the header buttons and their popovers), `model.ts` (the data shapes and
services of §1–§2), `controls.meta.ts` → `kbview-controls.json` (the core project's registry of its custom controls).
Details and parity numbers: `WEB-VIEWS.md`, "Core pilot: WaffleMenu & AccountMenu".

- `AppTileGrid` (same name as the desktop's): `Apps`, `Favorites` (the shown list: saved, or the draft while
  editing), `Editing`, `DropHereText`, `AllFavoritesText`, the header band as `<AppTileGrid.Header>`; events
  `TileInvoked` (`e.value` = the id) and `FavoritesEdited` (`e.value` = the new list). It owns the drag and drop.
- `MenuLink` (web only): a link that is an item of the menu hosting the view (the host provides the item through
  `MenuItemHostContext`: in `WaffleButton`, Radix `DropdownMenu.Item`), a plain link elsewhere.
- `WaffleButton` (web): `Apps` (the active modules' apps), `Dark`, `Fab` (the mobile floating button); event
  `OpenChanged`. A Radix menu portalled to `<body>`, `align="end"`, `sideOffset` 4, `collisionPadding` 8/12 (flip and
  clamp), Escape and a click outside close it (inside an edit, Escape first ends the edit), the focus goes back to
  the button. It reads and saves the favourites (`preferences.waffle_favorites` + the localStorage cache) and
  navigates on `AppLaunched`.
- `AccountButton` (web): event `AddAccount` (the add-account dialog stays in `HeaderActions`). A portal on
  `<body>` with the former backdrop, placed by `placePanel`: under the button, end edges aligned (the start edge in
  RTL), 4 px below, clamped 8 px from the viewport's edges, above the button when the room below is too short and
  the room above larger; Escape and a click outside close it and the focus goes back to the button. It reads the
  roster (`GET /auth/accounts` at each opening), the linked accounts of other instances and the unread counts, and
  acts on every event (switch, reconnect, remove, sign out, navigate, the photo's crop and upload).
- Not on the web yet: the `Popup…` properties of §3 (the web popovers anchor to their button), `ContentHeightChanged`
  is raised but the web hosts size their popovers with CSS, `HeaderActions` stays a TSX component (it now places
  `WaffleButton` and `AccountButton`), `AccountService.act` (the web `AccountButton` acts on the events itself).

## 8. Reconciliation of the two targets (2026-10-02)

Read side by side (desktop `kubuno-desktop-shell-controls`, web `core/frontend/src/core/shell/menus`), the user controls carry the
same names, properties, events and data shapes (§1–§3). What remains different is listed here, each on purpose:

| Topic | Desktop | Web | Why |
|---|---|---|---|
| Event args of `AppTileGrid` (an inner part) | `TileInvoked { id }`, `FavoritesEdited { favorites }` | `e.value` (`ValueChangedEventArgs`) | each target's event-args convention; the user-control events (§1–§2) are identical |
| Tiles as links, `href`, `Icon` component, `ShowMarketplace` / `MarketplaceHref` / `OpenMarketplace` | — | yes | the browser's link semantics (middle click) and the web's module bundles; the desktop opens apps through `LauncherService::launch` |
| `ManageHref` / `LabelsHref` / `AdminHref` | — | yes | links (web) |
| « Se déconnecter de tous les comptes » | « Se déconnecter » (the current account) | all the browser's accounts of the instance | a desktop account is one instance's session; signing out of every instance is the accounts page's |
| `Expanded` on `AccountMenu` | a property (`true` by default) | internal state | the desktop host sizes its popup window from it (`ContentHeightChanged`) |
| `Popup…` properties, `ContentHeightChanged` handled | yes (a popup window sized by the host) | CSS sizing, popovers anchored to their button | §4 |
| Panels in the dark theme | dark tint (`#28282CCC`), the theme's cards | the light panel and white cards (`#E9EEF6`, `bg-white`) under light text: the greeting is unreadable | **web defect** to fix on the web side (the desktop follows the theme) |
| Fonts | the system font | Plus Jakarta Sans | desktop decision (DESKTOP-MIGRATION.md) |
| Data source of the labels and ids | `/api/v1/modules` `sidebar_items` (+ the rules of §1) | each module's `WaffleAppRegistry.register(...)` in its bundle | the desktop loads no module bundle. **Follow-up for one source**: the core serves each module's launcher entries (`waffle_apps`: id, label, path, logo, module label) in `/api/v1/modules`, from the module's manifest, and both targets read them |
