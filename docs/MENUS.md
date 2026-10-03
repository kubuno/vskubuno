# Menus: the menu bar, context menus, drop-down and split buttons

> Design note and as-built record (2026-10-02). The menu family of `.kbview` views on the desktop target, modelled
> on Windows Forms (`MenuStrip`, `ContextMenuStrip`, `ToolStripMenuItem`, `ToolStripDropDownButton`,
> `ToolStripSplitButton`) and WPF (`Menu`, `ItemTemplate`, commands), drawn like the web's `@ui` `MenuDropdown` (the
> visual reference: same pill hover, accent icons, shortcut column, section labels, separators, danger rows), and
> edited in the Visual Studio designer the way the Windows Forms designer edits a `MenuStrip` (« Tapez ici »).
> Related: [VIEWS-SPEC.md](VIEWS-SPEC.md), [DESIGNER.md](DESIGNER.md), [RIBBON.md](RIBBON.md) (the `Command`),
> [EVENTS.md](EVENTS.md) (typed events), [ICONS.md](ICONS.md).

## 1. What existed before

- `<ContextMenu>` + `<MenuItem>` (foundation F1, `kubuno-desktop-views/src/window.rs`): sub-menus, icons, separators
  (`Text="-"`, `Kind="Separator"`), headers (`Kind="Header"`), `Checked`/`CheckOnClick`/`RadioGroup`, bindings on
  `Text`/`Enabled`/`Checked`/`Visible`, `ItemsSource` (rows with fixed fields), `OnDropDownOpening`, owner draw, a
  `ContextMenu` property on every control, `DropDownMenu` on `Button`/`IconButton`, `show_context_menu` from code. The
  `ShortcutKeys` were displayed only: nothing ran them.
- `kubuno_desktop_ui::workspace::MenuBar` (the web docking port), a Rust widget with no `.kbview` element.
- The ribbon family and its `<Command>` (RIBBON.md §4), whose shortcuts only the ribbon ran.

## 2. Elements

| Element | Kind | WinForms / WPF | Children |
|---|---|---|---|
| `MenuBar` | control (`Control` level) | `MenuStrip`, WPF `Menu` | `MenuItem` (the top-level menus) |
| `ContextMenu` | component (tray) | `ContextMenuStrip` | `MenuItem`, `MenuSeparator`, `MenuHeader`, `<ContextMenu.ItemTemplate>` |
| `MenuItem` | structural | `ToolStripMenuItem`, WPF `MenuItem` | `MenuItem`, `MenuSeparator`, `MenuHeader`, `<MenuItem.ItemTemplate>` |
| `MenuSeparator` | structural | `ToolStripSeparator` | — |
| `MenuHeader` | structural | the web's `{ type: 'label' }` row | — |
| `DropDownButton` | control (`ButtonBase`) | `ToolStripDropDownButton` | the menu's items |
| `SplitButton` | control (`ButtonBase`) | `ToolStripSplitButton` | the menu's items |

The Toolbox shows them in the « Menus et barres d'outils » tab (registry family `menus`).

### 2.1 `MenuItem`

| Property | Meaning |
|---|---|
| `Text` | With an `&` mnemonic (`&Fichier`; `&&` is an ampersand). Bindable, localizable (`{Res key}`). |
| `Icon` | A Lucide name or an image file (ICONS.md), the icon picker in the Properties window. |
| `ShortcutKeys` | `Ctrl+S`, `Ctrl+Shift+N`, `Alt+F4`, `F5`, `Ctrl++`, `Shift+Delete`… (§4). Runs the item while its menu is closed. |
| `ShortcutKeyDisplayString` | Text shown instead of the shortcut. |
| `ShowShortcutKeys` | `false` hides the shortcut text (the shortcut still works). |
| `Checked`, `CheckOnClick`, `RadioGroup` | Check mark, toggled on click, exclusive group. |
| `Enabled`, `Visible`, `Danger` | Bindable state; a danger row is red (the web's `danger`). |
| `ToolTip` | Shown when the pointer (or the keyboard) rests on the row. |
| `Command` | A `<Command x:Name>` of the view (also `{x:Ref name}`): text, icon, shortcut, enabled and checked state come from it when the item does not write them; choosing the item raises its `OnExecute` (a checkable command switches). |
| `ItemsSource` | A bound list: rows with the fixed fields (`Text`, `Key`, `Icon`, `ShortcutKeys`, `Checked`, `Enabled`, `Danger`, `ToolTip`, `Kind`), or made from the `<MenuItem.ItemTemplate>` (WPF): its literals copied, each `{Binding Field}` read from the row. |
| `Kind` | `Command`, `Separator`, `Header` (kept; `MenuSeparator`/`MenuHeader` say it shorter). |

Events: `OnClick` (default: a double-click in the designer creates it), `OnCheckedChanged`, `OnDropDownOpening`
(fill a sub-menu just before it opens).

### 2.2 `MenuBar`

`Style="Workspace"` (28 DIP, `#ffffff` / `#1c1c1e`, the web's `WorkspaceMenuBar`) or `Compact` (24 DIP, PaintSharp's
bar in the theme's header colour). Events `OnMenuActivate` / `OnMenuDeactivate` (WinForms `MenuActivate`/`MenuDeactivate`).

### 2.3 `ContextMenu`

As before, plus `MenuSeparator`/`MenuHeader` children, `ItemTemplate`, `OnClosed`, and Shift+F10 / the context-menu key
(§3). The `ContextMenu` property of a control accepts `name` or `{x:Ref name}`.

### 2.4 `DropDownButton`, `SplitButton`

Kubuno buttons (`Variant`, `Size`, `Icon`, mnemonic `Text`) whose menu is their own `MenuItem` children, or the
`<ContextMenu>` their `DropDownMenu` names. A drop-down button opens on the press (chevron after its label); a split
button is a button and an arrow part: `OnClick` on the main part (else its `DefaultItem`, the `x:Name` of one of its
items, runs), the menu on the arrow. Keyboard: Down, Alt+Down, F4 open the menu; Space/Enter open it (drop-down) or
click (split). Events `OnDropDownOpening`, `OnDropDownClosed`.

## 3. Run time (`kubuno-desktop-views`)

- **One menu engine.** Every menu of a view — a context menu, each top-level menu of a bar (`#bar:<item id>`), the
  items of a drop-down button (`#dd:<button id>`) — is a `MenuSpec` (`window::read_menus`) opened by the runtime as an
  `OpenMenu` (`window.rs`): each level its own floating surface (popups may leave the window), sub-menus to any depth,
  kept inside the monitor, `OnOpening`/`OnDropDownOpening` before it reads its bindings and lists, owner draw.
- **Keyboard.** While a menu is open it has the keyboard (nothing reaches the focused control): arrows, Home/End,
  Enter/Space, Escape, Left/Right across sub-menus, and **letters**: the item whose mnemonic it is (else whose text
  starts with it) is chosen, or the next of several becomes hot. A menu opened from the keyboard underlines its
  mnemonics (WinForms' keyboard cues), as does holding Alt.
- **Menu bar.** A click on a label opens its menu (a click on the open one closes it); while one is open, the pointer
  over another label opens that one (WinForms hover tracking); Left on a first level / Right on a row without a
  sub-menu go to the previous / next menu of the bar. **Alt** pressed and released alone, or **F10**, puts the bar in
  keyboard mode (labels underlined, a label hot): Left/Right, Down/Up/Enter/Space or a mnemonic letter open, Escape or
  a click leaves; **Alt + a mnemonic** opens its menu directly. Escape on a keyboard-opened menu returns to the bar.
- **Shortcuts (accelerators).** Each item's `ShortcutKeys` (else its command's `Shortcut`) runs it while its menu is
  closed, **before** the focused control reads the key (WinForms `ProcessCmdKey`): its check mark, its `OnClick`, its
  command — only when it is enabled and visible as its menu would show it. A `<Command>` no item runs gets its shortcut
  too when the view has no ribbon (a ribbon runs its commands' shortcuts itself). The first of two equal shortcuts wins
  (the language server warns, §6).
- **Context menus** open on a right-button release over a control that names one, and on **Shift+F10 / the
  context-menu key** for the focused control (else its nearest container with one, else the view's), from the keyboard.
- **Tooltips** of rows show after the view's `ToolTip` `InitialDelay`.
- **Accessibility (UI Automation).** The bar is a `MenuBar` whose labels are `MenuItem`s with `ExpandCollapse` (state =
  their menu is open) and their access key; an open menu publishes a `Menu` per level and a `MenuItem` per row:
  `Invoke`, `Toggle` for a check or radio row, `ExpandCollapse` for a sub-menu, `Separator`; the hot row holds the
  focus. Invoke / Expand / Collapse act on the menu. (`AccessNode::expanded` was added to `kubuno-desktop-controls` for this.)
- **Themes and DPI.** The rows are `kubuno_desktop_ui::lists::Menu` (the `MenuDropdown` port), light and dark; everything is
  DIP-based (verified at 100 % and 175 %).

## 4. The shortcut grammar (`kubuno-desktop-views-syntax::shortcut`, platform-neutral)

Modifiers (`Ctrl`/`Control`, `Shift`/`Maj`, `Alt`, any case) then one key: a letter, a digit (`0`…`9`, `D0`…`D9`),
`F1`…`F24`, `Delete`/`Del`, `Insert`/`Ins`, `Home`, `End`, `PageUp`/`PgUp`, `PageDown`/`PgDn`, arrows, `Enter`,
`Escape`/`Esc`, `Space`, `Tab`, `Backspace`, `Plus` (`Ctrl++`), `Minus` (`Ctrl+-`), `Comma`, `Period`, `Slash`,
`Semicolon`, `Apps`. A key that types text (a letter, a digit, Space…) needs Ctrl or Alt; the F-keys, Escape, Delete and
Insert do not. `mnemonic_key` reads the `&` letter. The desktop maps a key to its virtual-key code (`menus::vk_of`).

## 5. Designer (vskubuno)

- **Shown open on the surface.** A selected `MenuBar` shows its « Tapez ici » slot after its labels; a selected
  top-level item (or anything under it) shows its menu below its label; a `ContextMenu` selected in the component tray
  (or one of its items) shows open at the top of the view under a tab with its name, like the Windows Forms designer; a
  selected drop-down / split button shows its menu below it. Each level shows the sub-menu of the selected row, and
  ends with a slot. Every row is an element of the layout map: selectable, with its adorner, Properties (F4), Delete,
  Ctrl+C/X/V/D (with its sub-tree), undo/redo (the text buffer's), double-click → its `OnClick` handler, right-click.
  The mnemonics are underlined on the surface.
- **« Tapez ici » (in place, in the surface process `view_embed`).** A click on a slot opens an editor in it: typing
  then Enter inserts `<MenuItem x:Name="…_item" Text="…"/>` (`-` alone inserts a `<MenuSeparator/>`) and goes on to the
  next slot; Tab inserts and goes into the new item's sub-menu; on the bar, Enter goes into the new menu; Escape stops;
  a click elsewhere keeps what was typed. F2 on a selected row edits its `Text` in place. The `x:Name` is made from
  the text (`&Ouvrir…` → `ouvrir_item`), unique in the view, so handlers get readable names.
- **The « ▾ » of a slot** opens « Ajouter » with what the level takes: Élément de menu, Séparateur, Titre de section.
- **Drag to reorder**, within a level and **across levels**: dragging a row over another row opens that row's
  sub-menu; drop before/after a row of any level, or on a level's slot (appends), with an insertion marker; a row is
  never dropped into its own sub-menu. One `moveElement` edit (one undo unit).
- **Smart tag** on a selected menu element (`MenuDesignerTasks`): « Insérer les éléments standard » (an empty
  `MenuBar`: Fichier, Édition, Outils, Aide with icons, shortcuts, `x:Name`s, texts in the project's `.kbres` as
  `{Res key}` when it has one), « Modifier les éléments… » (the polymorphic collection editor: Items / DropDownItems of
  MenuItem, MenuSeparator, MenuHeader), « Ajouter … », and on a `MenuItem` « Modifier le texte… », « Choisir
  l'icône… », « Exécuter la commande « cmd_x » » per `<Command>` of the view, « Nouvelle commande à partir de cet
  élément » (Text → Label, Icon → SmallIcon, ShortcutKeys → Shortcut, ToolTip → ScreenTipText) and « Ne plus
  exécuter … ».
- **Properties window**: `ShortcutKeys` has the Windows Forms shortcut editor (Ctrl / Shift / Alt boxes, the key list,
  Réinitialiser) writing the canonical text; `Command` has the reference drop-down; `Icon` the icon picker.
- **Document Outline** shows the menu tree (the elements are ordinary elements); XML ⇄ surface selection sync applies.

## 6. Language server

Warnings (non-blocking, translated through `messages::localize`, French when Visual Studio is in French):

- a `ShortcutKeys` / `Shortcut` that does not parse (unknown key, two keys, a letter without Ctrl or Alt…);
- **a shortcut used twice** in the view (`le raccourci `Ctrl+S` est déjà utilisé ligne 4`);
- **two items of one menu level with the same access key** (`&Fichier` / `&Format`);
- a `Command` naming no `<Command x:Name>` (outside the ribbon, which checks its own);
- a `ContextMenu` / `DropDownMenu` naming no `<ContextMenu x:Name>`.

Completion of the new elements and properties comes from the registry (reference drop-downs for `Command` and
`ContextMenu`).

## 7. As built

| Piece | Where |
|---|---|
| Model, reading, open menus, keyboard, mnemonics, UIA, tooltips | `kubuno-desktop-views/src/window.rs` |
| Commands, accelerators, bar state, `MenuBarNode`, `DropDownButtonNode`, `ContextMenuNode`, design overlay, « Tapez ici » slots, menu rows and drop targets, lints | `kubuno-desktop-views/src/menus.rs` |
| Registry (`MenuBar`, `MenuSeparator`, `MenuHeader`, `DropDownButton`, `SplitButton`, French docs) | `kubuno-desktop-views/src/registry/families/menus.rs`; `ContextMenu`/`MenuItem` in `families/components.rs` |
| Runtime: accelerators, bar keyboard, Shift+F10, `OnClosed`, `OnMenuActivate`, menu access tree | `kubuno-desktop-views/src/runtime.rs` |
| Cross-level drag | `kubuno-desktop-views/src/design.rs` (`DragSession::menu_target`) |
| In-place editor, F2 | `kubuno-desktop-views/examples/view_embed.rs` (`TypeEdit`) |
| Shortcut grammar | `kubuno-desktop-views-syntax/src/shortcut.rs` |
| Mnemonic underline in menus | `kubuno-desktop-ui/src/lists.rs` (`Menu::mnemonics`) |
| `AccessNode::expanded`, Expand/Collapse actions | `kubuno-desktop-controls/src/host/access.rs` |
| Designer verbs, standard items, shortcut editor | vskubuno `src/Views/Kubuno.Views/Designer/Menus/` |
| Demo of every feature | `kubuno-desktop-views/examples/views/menus.kbview` + `examples/menus_demo.rs` |

**Verified**: unit tests (shortcut grammar, reading, commands, item templates, accelerators, bar keyboard and
mnemonics, Escape back to the bar, menu keyboard capture, UIA nodes, lints, the demo view compiles with no warning, LS
handler creation for ribbon buttons, menu items and split buttons); live runtime captures light/dark at 100 % and 175 %
(keyboard F10/Alt navigation, sub-menus, tooltips, Shift+F10 / Apps, real-mouse hover tracking across the bar, item
click, right-click); a UI Automation probe; the web `MenuDropdown` in headless Chrome for comparison; the design surface
driven over its protocol (menus open on selection, « Tapez ici » typing on the bar and in a level, Enter/Tab chaining,
real-mouse cross-level drag → `moveElement`, double-click → `doubleClick`). The C# designer side (smart tag verbs,
standard items, shortcut editor, Toolbox tab) is covered by unit tests (`MenuDesignerTasksTests`) and the VSIX builds.

**Not verified in Visual Studio**: the end-to-end session (create a menu with « Tapez ici », drag, standard items,
double-click → handler, F5) could not be run: the shared experimental hive held a stale registration of the
extension (KubunoPackage failed to load) and a fresh private hive left the `.rsproj` « incompatible » (its project
system not initialised). To do next, in a prepared hive.

**Not done yet**: MDI menu merging (`AllowMerge`/`MergeAction`), ComboBox / TextBox items inside menus, scrolling of a
menu taller than the screen, RTL mirroring of menus, a `CommandParameter`, typed `x:Name` handles for `MenuBar` /
`DropDownButton` / `SplitButton` in the `kubuno-desktop` crate (they are `kubuno_desktop::Control`), the web target (these elements
are desktop-only in the web registry), shortcuts of menus inside user controls (their context menus still open).
