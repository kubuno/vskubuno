# `.kbview` views — shared specification (desktop and web)

> Status: **normative, version 1** (lot WV-0, frozen 2026-10-01). It applies to both targets: the desktop
> (`kubuno-views`, Rust code-behind) and the web (`@kubuno/views`, TypeScript code-behind), and to the mobile renderer
> when it comes. Decisions behind it: `WEB-VIEWS.md` §10 (recommendations 1–5 accepted, everything migrates, new
> components lot, migration order core → drive). Design notes it builds on: `XML_VIEWS.md`, `PROGRAMMING-MODEL.md`,
> `EVENTS.md`, `DESIGNER.md`, `DATA.md`, `RESOURCES.md`.
>
> "MUST", "SHOULD" and "MAY" are used in their RFC 2119 sense. Where this document and an older design note disagree,
> this document wins; §14 lists the corrections it makes to `WEB-VIEWS.md`.
>
> Reference implementations: the desktop registry export (`desktop/windows/src/crates/kubuno-views/src/registry/
> export.rs`, served as `kubuno/registry`), the web registry (`core/frontend/src/ui/kbview/*.meta.ts`,
> `core/frontend/src/sdk/kbview/*.meta.ts`, built by `npm run build:registry` into
> `core/frontend/packages/ui/kbview-registry.web.json`) and the conformance test between them
> (`core/frontend/src/kbview/kbview-registry.spec.ts`, §11).

## 1. Files and targets

- A view is a `.kbview` file (XML 1.0, UTF-8). It belongs to **one target**: its code-behind is Rust (desktop) or
  TypeScript (web). What both targets share is the vocabulary, the grammar, the tooling and this specification —
  not files.
- The target of a view is decided by the language server, walking up from the file: an explicit
  `kubuno.views.json` (`{"target": "web" | "desktop"}`) wins; else a `package.json` depending on
  `@kubuno/views-compiler` → `web`; else a `Cargo.toml` depending on `kubuno` / `kubuno-views` → `desktop`.
- The code-behind is the same-stem file next to the view: `NotesSettingsPage.kbview` + `NotesSettingsPage.ts` (web),
  `main_view.kbview` + the Rust type carrying `#[kubuno::view("main_view.kbview")]` (desktop).
- The `.kbview` text is the **only source of truth**. Tools edit it surgically (comments, attribute order, formatting
  preserved); nothing generated from it is ever committed (web: `.kubuno/views/` is gitignored; desktop: `OUT_DIR`).

### 1.1 File kinds: `.kbview` and `.kbcontrol`

A view file has one of two extensions, by **role**. The format is the same (this whole specification applies to both);
only what the file *is* differs, which is what tools show (Solution Explorer icon, item templates, the web plugin's
module kind).

| Extension | Holds | Root element | Desktop code-behind | Web code-behind |
|---|---|---|---|---|
| `.kbview` | a form: a window, a dialog, a tool window, an MDI parent or child, a splash screen, a flyout, a page | any container but `UserControl` (`Panel`, …) | `#[kubuno::view("main_view.kbview")]` | a class extending the generated `ViewBase`, imported from `./NotesSettingsPage.kbview` |
| `.kbcontrol` | a user control: a composite control designed as a view, used as an element of other views (`<MessageRow/>`) | `UserControl` | `#[derive(UserControl)]` + `#[user_control(view = "message_row.kbcontrol")]` | a class extending the generated `ViewBase`, imported from `./MessageRow.kbcontrol` |

- A view is a user control when its root is `<UserControl>` **or** its code-behind declares it as one
  (`#[derive(UserControl)]` naming it). Such a file **MUST** be a `.kbcontrol`; every other view **MUST** be a `.kbview`.
- The language server warns (`view-file-kind`) when the extension disagrees with the kind, and offers the quick fix
  « Renommer en .kbcontrol » / « Renommer en .kbview »: it renames the file and updates every path naming it — the
  code-behind's `#[kubuno::view(…)]` / `#[user_control(view = …)]` and the `x:Inherits` of the views deriving from it.
- Inheritance keeps the kind: an inherited user control (`<UserControl x:Inherits="address_editor.kbcontrol">`) is a
  `.kbcontrol`, an inherited form a `.kbview`.
- Unaffected: custom controls (code only, `#[derive(Component)]`, no view file) and inherited controls (code only).
- Everything that reads views takes both extensions: the macros (`include_str!` of the named path, whatever its
  extension), the design build, `kubuno-views-ls` (document selector, project scanning, diagnostics, `{Res}`), the
  Visual Studio designer and Solution Explorer, `Kubuno.Rust.Sdk` (code-behind nesting, `SubType="Designer"`, build
  inputs), and on the web the Vite plugin (`@kubuno/views-compiler` compiles `*.kbview` and `*.kbcontrol` alike; a
  `.kbcontrol` module exports a user control, a `.kbview` module a view).
- A view's resources (`main_view.kbres`) and code-behind (`main_view.rs` / `.ts`) are found by stem, under either
  extension.

## 2. Element names

**Rule: the canonical element names are the desktop names** (decision 1). The web registry maps each canonical name
onto a React component; React component names do not change. A name means the same thing on every target: same
properties, same enum values, same events, same children model — differences are allowlisted (§11).

### 2.1 Mapping table (as built in WV-4)

`@ui` = `@kubuno/ui` (specifier `@ui`), `sdk` = `@kubuno/sdk`. "Item" = consumed by its parent's children → props
adapter (§10.5), not rendered on its own.

| `.kbview` element | Desktop family | Web component | Notes |
|---|---|---|---|
| `Button` | core | `@ui` `Button` | `Text` → `children`, `Icon` → `icon` |
| `IconButton` | choice | `@ui` `Button` (no text) | `Variant`/`Size` web-only; `AccessibleName` required |
| `Switch` | core | `@ui` `Toggle` | `On` ↔ `checked` |
| `CheckBox` | choice | `@ui` `Checkbox` | `CheckState=Indeterminate` → `indeterminate` |
| `RadioButton` | choice | `@ui` `Radio` | `SelectedValue`/`Value` → `checked` (`equals-value`) |
| `Slider` | choice | `@ui` `RangeSlider` | |
| `NumericField` | choice | `@ui` `NumberInput` | |
| `TextField` | core | `@ui` `Input`; `Variant="Outlined"` → `@ui` `OutlinedField` | alternate component (§10.5) |
| `TextArea` | text | `@ui` `Textarea` | |
| `Dropdown` + `Option` | text | `@ui` `Dropdown` (`options[]`) | `Option` = item |
| `ComboBox` + `Option` | text | `@ui` `Combobox` (`options[]`) | `Option` = item |
| `DatePicker` | text | `@ui` `DatePicker` | |
| `ColorField`, `GradientField` | text | `@ui` `ColorField`, `GradientField` | |
| `Badge`, `Spinner`, `ProgressBar`, `Separator`, `Callout`, `EmptyState` | display | same names in `@ui` | |
| `Card` | core | `@ui` `Card` | slots `<Card.Actions>`, `<Card.Footer>` |
| `Tabs` + `TabItem` | containers | `@ui` `Tabs` (`tabs[]`) | the runtime renders the selected item's content after the strip |
| `Accordion` + `AccordionSection` | containers | `@ui` `Accordion` (`items[]`) | |
| `Breadcrumb` + `BreadcrumbItem` | containers | `@ui` `Breadcrumb` (`items[]`) | |
| `Stepper` + `Step` | containers | `@ui` `Stepper` (`steps[]`) | |
| `FloatingWindow` | containers | `@ui` `FloatingWindow` | portal |
| `Popover` | containers | `@ui` `AnchoredPopover` | portal; `Target` → `anchorRef` |
| `DataTable` + `Column` | data | `@ui` `DataTable` (`columns[]`) | |
| `ContextMenu` + `MenuItem` | components | `@ui` `MenuDropdown` (`items[]`) | menus are `MenuDropdown` by construction |
| `ToolTip` | components | `@ui` `Tooltip` options | each control's `ToolTip` property wraps it in `Tooltip` |
| `DockArea` + `DockPanel` | docking | `sdk` `DockArea` (`panels{}`) | record-shaped adapter |
| `WorkspaceShell` | docking | `sdk` `WorkspaceShell` | |

### 2.2 Planned (no web element yet)

Desktop elements without a web element today, to add in WV-5a/5b or the new-components lot, **keeping the desktop
name**: `Label`, `LinkLabel`, `Icon`, `Stack`, `Panel`, `TableLayoutPanel`, `ScrollArea`, `GroupBox`, `Splitter`,
`UserControl`, `PictureBox`, `Avatar`, `PaintBox`, `SearchField`, `MaskedField`, `MonthCalendar`, `ListBox`,
`CheckedListBox`, `ListView`, `TreeView`, `Item`, `Repeater`, `Timer`, `Toolbar`/`ToolbarItem`,
`Sidebar`/`SidebarSection`/`SidebarItem`, `StatusBar`/`StatusLabel`, `SplashArtwork`, the `Ribbon*` family and
`Command`. New shared elements (`RadioGroup`, `SettingsRow`, …) are added to **both** registries in the same lot.
Web-only elements (`ReactHost`, `Query`, `Mutation`) carry `target: web-only` in the allowlist (§11).

### 2.3 Coverage of the first migration targets

Migration order (WEB-VIEWS §10): core (host shell, admin, core screens), then drive (`core/frontend/src/drive`).
Inventory of JSX uses of `@ui`/sdk components in `core/frontend/src/core` and `src/drive` (313 `.tsx` files,
2026-10-01): **1 209 of 1 286 uses (94 %) are covered** by the WV-4 registry. Not covered: `ConfirmDialog` (39 —
the `useConfirm` pattern, which becomes `MessageBox.show` in code-behind, WV-3, not an element), `LabelIcon` (6),
`Collapse`, `MobileSheet`(+`Item`/`Separator`), `RequiredMark` (folded into fields' `Required`), `ConflictDialog`,
`KubunoLogo`, `StartPage`, `HelpBubble`, `DataTableSkeleton`, `ToastProvider`, `FloatCheckbox`, the colour/font
pickers, `RichText`, `ResizeHandle` (→ `Splitter`), `PromptDialog` (→ code-behind), `Slot`. The bulk of the core's
markup is raw HTML + Tailwind (§0 of WEB-VIEWS): it needs the WV-5a layout and text elements first.

## 3. Grammar

- **Element** = component; **attribute** = property (or event, §8). Names are case-sensitive PascalCase.
- **`x:` namespace** (reserved, never a property): `x:Name` (identifier, unique in the view; becomes a typed field of
  the code-behind and the element's handle), `x:Props` (web root only: the TS type of the view's props),
  `x:Inherits` (inherited views, desktop). `xmlns:x` / `xmlns:d` declarations are optional.
- **Design-time attributes**: `d:<Property>="…"` sets that property's value **in the designer only** (`d:Text`,
  `d:Visible`, `d:ItemsSource`); `DesignWidth` / `DesignHeight` (plain attributes, root element only, F32) give the
  canvas size of a view that declares no `Width`/`Height`. Both are ignored by production builds. A repeater's
  design-time item count is the `Repeater` property `DesignItemCount` (category Design), not a `d:` attribute.
  Web addition: `d:DataContext="{SampleData file.json}"` and `<Query d:Sample="…">` (§6.4).
- **Attached properties** `Owner.Property` are set on a child and read by its parent: `Stack.Fill`,
  `TableLayoutPanel.Row`, `.Column`, `.RowSpan`, `.ColumnSpan` (declared on `Control`), `TitleBar.Region`,
  `TitleBar.Drag`, `ActionBar.Region` (desktop window chrome).
- **Property elements**: `<Owner.Property>…</Owner.Property>`, placed **directly inside** `<Owner>`, set a content
  property (registry `serialization: "Content"`): `<Card.Actions>`, `<Card.Footer>`, `<RibbonTab.ScalingPolicy>`. A
  dotted element anywhere else is an error.
- **Children** follow the registry's model: `None` (no child element), `SingleWidget` (at most one), `List` (any
  number; `allowed_children` non-empty = only those element names). Items (`Option`, `TabItem`, `Column`,
  `MenuItem`…) are children like any other.
- **Size-class suffixes** (§5.3) `Property.Compact|Medium|Expanded` are reserved: `Compact`, `Medium`, `Expanded` can
  never be element names.
- **Element ids** (designer, `data-kb-id` on the web): the dot path of element-child ordinals from the root (`""` =
  root, `"0.2"` = third child of the first child), computed by the shared parser (`Element::stable_id`).
- Comments, whitespace and attribute order are preserved by every tool.

## 4. Values

| Kind (registry `kind`) | Literal form | Notes |
|---|---|---|
| `Bool` | `true`, `false` | |
| `F32` | decimal number, invariant culture (`12`, `0.5`, `-1`) | empty = "not set" where the default is empty |
| `String` | any text | structured strings below |
| `{"Enum": [...]}` | one of the listed PascalCase values | web enum values are mapped by the registry (`Primary` → `'primary'`) |

Structured strings (kind `String`, `type_converter` names the parser): `Padding`/`Margin` = `"left, top, right,
bottom"` px (or one value for all four); `Size` (`MinimumSize`) = `"width, height"`; `Anchor` = comma-separated edges
(`"Top, Left"`); `IconSize` = `Small|Medium|Large|XLarge` (16/20/24/32) or px or `"w, h"`; colours (`Color`
converter) = a **theme token** name (§4.1); icons = a Kubuno icon name (Lucide, `ICONS.md`), a path relative to the
view, or `{Res key}`. Any bindable value MAY be `{Binding …}` or `{Res …}` (§6).

### 4.1 Styling rule (decision 3)

- Colours, surfaces, typography and spacing come from **theme tokens** only: `ForeColor="TextSecondary"`,
  `Surface="Card"`, `Label Role="Heading"`, `Gap="12"` on the spacing scale. The token set is the one `theme.css`
  publishes and the desktop replicates; tokens follow light/dark mode and, on the web, the module accent
  (`[data-module="<id>"]`).
- A **colour literal** (`#202124`, `rgb(…)`) in a view is a language-server warning on both targets.
- **`Class="…"`** (web only, declared on `Control`): Tailwind classes merged into the component's `className`,
  tolerated during the migration. Each use is a warning and is counted; every repository has a budget
  (`kubuno.views.json` → `"classBudget": N`) that the language server enforces and that **MUST reach zero** for every
  class-A screen (WEB-VIEWS §6.1) before its TSX is deleted. The codemod reports the count per repository.
- No style sheets, resource dictionaries or free fonts in views (`Font` is desktop-only, §7).

## 5. Layout

### 5.1 Containers and engines

| Element | `layout_kind` | Semantics (both targets) |
|---|---|---|
| `Stack` | `Flow` | children along `Direction` (`TopDown`, `LeftToRight`, `RightToLeft`, `BottomUp`), `Gap` (F32 px), `Padding` (F32 px, one value), `CrossAlign` (`Stretch`, `Start`, `Center`, `End`), `WrapContents`; a child with `Stack.Fill="true"` takes the remaining room |
| `TableLayoutPanel` | `Flow` | grid of `ColumnCount` × `RowCount`, sized by `ColumnStyles`/`RowStyles`, children placed by `TableLayoutPanel.Row/Column/RowSpan/ColumnSpan` or in reading order (`GrowStyle`) |
| `Panel`, `UserControl` | `DockAnchor` | children docked (`Dock`) as bands in document order, the last `Fill` takes the rest; undocked children placed by `X`/`Y`/`Width`/`Height`/`Anchor` — see §5.2 |
| `Splitter` | `Split` | two panes, `Orientation`, `Distance` |
| `Tabs` | `Tabs` | one `TabItem` visible at a time |
| `Card`, `GroupBox`, `ScrollArea`, `FloatingWindow`, `Popover`, `WorkspaceShell`, `TabItem`, `DockPanel` | — | one child (`SingleWidget`) filling the content area |

### 5.2 Flow by default, absolute on request (decision 4a)

- On the **web**, layout is **flow** by default: an element sizes to its content, `Width`/`Height` fix a size,
  `Margin` spaces it, the parent container places it. `X`, `Y` and `Anchor` are honoured **only** inside a
  `Panel Layout="Absolute"`; elsewhere they are a language-server warning ("ignored on the web: use a layout
  container"). `Dock` on the web is laid out with CSS grid areas using **logical** edges: `Left` = inline *start*,
  `Right` = inline *end*, so a view is mirrored correctly in `ar`/`he`.
- On the **desktop**, `Panel` keeps its WinForms dock-and-anchor engine for both values of `Layout`; the desktop
  designer's free-positioning root corresponds to `Layout="Absolute"`.
- `Panel.Layout` (`Dock` | `Absolute`, default `Dock`) is a new property to add to the desktop registry with the
  WV-5a `Panel` element (allowlisted until then).
- `AutoSize`/`AutoSizeMode` are implicit on the web (flow sizes to content) and desktop-only in the registry.

### 5.3 Responsive values: size classes (shared with the mobile renderer)

- Size classes by the width available to the view's root: **Compact** < 640 px ≤ **Medium** < 1024 px ≤
  **Expanded** (`@ui` `MOBILE_MAX_WIDTH` / `useIsMobile` align on Compact).
- Syntax: `Property.<Class>="value"` next to the base attribute: `Direction="TopDown" Direction.Expanded="LeftToRight"`,
  `Visible.Compact="false"`, `ColumnCount.Compact="1"`.
- Resolution: the value for the current class is `Property.<Class>` when written, **else the base value** (no cascade
  between classes — what you read on the attribute is what applies). Bindings are allowed in suffixed values.
- Allowed on any property that is not `design_time`, not `root_only`, and not an event. The desktop accepts the
  syntax and applies the class of its window width when its renderer supports it; until then the language server
  reports "size classes not applied on this target" (information, not an error).

## 6. Bindings and resources

### 6.1 `{Binding}` — the desktop grammar (`kubuno-views` `binding.rs`), on both targets

```
{Binding Path[, Mode=OneWay|TwoWay|OneTime|OneWayToSource][, UpdateSourceTrigger=PropertyChanged|LostFocus|Explicit]
         [, Source=name][, Converter=Name][, ConverterParameter=text][, FallbackValue=text]
         [, StringFormat=fmt][, TargetNullValue=text][, ConverterCulture=ci]}
```

- `Path`: dotted member path (`prefs.font`, `locations.data.locations`); first unnamed part, or `Path=`.
- `Mode`: `OneWay` (default), `TwoWay` (also writes the user's changes back), `OneTime` (reads the source once — per
  item of a `Repeater` — and keeps that value), `OneWayToSource` (only writes; the property keeps showing what was
  entered, its `FallbackValue` before that). A write-back happens on the property's **change event**, named by the
  registry (web: `prop_map[P].change`, e.g. `Checked` ↔ `OnCheckedChanged`).
- `UpdateSourceTrigger`: when a write-back reaches the source — `PropertyChanged` (default; `Default` is accepted),
  `LostFocus` (when the focus leaves the element, or the window), `Explicit` (when the code asks:
  `kubuno_views::binding::update_sources(vm, path)` on the desktop). The property shows the pending value meanwhile.
  Inside an item template a write is always immediate (the row only exists while the item is built).
- `Source=name`: a named data component (`<Query x:Name>` on the web, `BindingSource` on the desktop); `Source=a,
  Path=b` ≡ path `a.b`.
- `Converter=Name` (+ `ConverterParameter`): applied to the source value before the format, and backwards on a
  write-back (a converter that does not convert back drops the write). Built-in on both targets: `Not`, `IsEmpty`,
  `IsNotEmpty`, `ToUpper`, `ToLower`, `Trim`, `Equals` / `NotEquals` (compare with the parameter; `Equals` writes the
  parameter back when it becomes true: a radio button per choice), `BoolToText` (parameter `'yes|no'`), `Count`
  (rows of a list, characters of a text). Application converters: desktop `#[kubuno_views::value_converter]` on an
  `impl ValueConverter for T` (`T: Default`), or `binding::register_converter(name, value)`; a project converter may
  replace a built-in one. An unknown converter shows the value unconverted.
- `FallbackValue`: what the property shows while the path does not resolve (an unset key, a value of the wrong
  shape, a converter that declines); without it, the property's default.
- Formatting: `StringFormat` (alias `FormatString`: .NET-style `N2`, `C`, `d`, `dd/MM/yyyy`, `#,##0.00`),
  `TargetNullValue` (alias `NullValue`), `ConverterCulture` (aliases `Culture`, `FormatInfo`). Values with commas,
  quotes, braces or `=` are quoted `'…'` (`''` = one quote).
- Unknown keys and unknown `Mode` / `UpdateSourceTrigger` values are ignored by the runtime and reported by the
  language server as warnings (with « did you mean » for a key in the wrong case); so are an unknown converter, an
  unknown path (when the data context lists every path it answers), a member whose shape does not fit the property,
  and a two-way binding of a read-only member.
- **Not in this version**: `ElementName`, `RelativeSource`, expressions. Logic lives in code-behind getters
  (`Enabled="{Binding canSave}"`) and converters.
- Design time: `d:Property="value"` (any property) replaces the property's value in the designer only — the sample a
  bound property shows while the view model is not running.
- Resolution order: the view instance (fields, `@bind` accessors / `#[bind]` fields, getters), then its
  `dataContext` / `#[data_context]`; inside a `Repeater` item: the row, then the user control, then the page.
- A value that starts with `{` and ends with `}` is a markup extension; v1 has no escape for a literal in braces.

### 6.2 `{Res}` — resources

`{Res key[, Source=set]}`: a string (or image, colour…) of the view's `.kbres` resources in the current culture,
one-way. Web: `.kbres` compiles to i18next bundles; `{Res}` resolves through `t(ns:key)` (WV-6). Reserved for WV-6
on **both** targets: arguments and plurals `{Res key, Count={Binding n}, Name={Binding user.name}}`, `.kbres`
plural entries `key_one` / `key_other`.

### 6.3 Data components

Non-visual elements (registry `kind: "component"`, `non_visual: true`) live in the designer's component tray:
`Timer`, `ToolTip`, `ContextMenu` (both targets); `BindingSource`, `TableAdapter` (desktop, `DATA.md`); `Query`,
`Mutation` (web). `Query`/`Mutation` name code-behind methods (`Fetch="load_locations"`, `Run="add_location"`) like
handlers.

### 6.4 Sample data

Design mode never runs handlers or mutations. Sample values come from `d:` attributes, `d:DataContext="{SampleData
X.sample.json}"`, `Repeater DesignItemCount`, `<Query d:Sample="…">`; the web designer MAY switch to live data
(opt-in, WEB-VIEWS §4.6).

## 7. Typography

### 7.1 Shared roles and tokens

Both targets publish the **same typographic roles** under the same names **and the same sizes**; only the face
differs (§7.2). A web CSS px is one desktop DIP, so a role renders at the same physical size on both targets at any
scale (at 175 %, 13.5 px = 13.5 DIP = 23.6 device pixels). The desktop sizes live in
`drive_app_controls::themes::shape::text` (re-exported as `kubuno_ui::metrics::text`), each role with its own
DirectWrite format in `TextFormats`; the web sizes in `theme.css`.

| Role (`Label Role`, `kubuno_ui::display::Role`) | Web token | Web (`theme.css`) | Desktop (`metrics::text`, DIP) | Desktop format | Desktop line box |
|---|---|---|---|---|---|
| `Micro` — badges, counters | `--kb-text-micro` | 10.5 px | 10.5 (`MICRO`) | `micro` | 16 |
| `Meta` — metadata, captions | `--kb-text-meta` (= host `text-xs`) | 11.5 px | 11.5 (`META`) | `caption` | 16 |
| `Body` — labels, fields, menus, tabs (default) | `--kb-text-body` (= host `text-sm`) | 13.5 px | 13.5 (`BODY`) | `body` | 20 |
| `Heading` — section headers, card titles | `--kb-text-heading` | 15.5 px | 15.5 (`HEADING`) | `heading` | 24 |
| `Title` — object name heading a panel | `--kb-text-title` | 21.5 px | 21.5 (`TITLE`) | `title` | 30 |
| page title (not a Label role) | `--kb-text-page` | 22.5 px (27.5 in `.kb-admin`) | 22.5 (`PAGE`; `PAGE_ADMIN` 27.5) | `page` | — |

Until 2026-10 the desktop followed the older web scale (11 / 12 / 16 / 22, body at the OS form size 12); it is now
aligned role by role. A few web components set a literal size outside the scale (`@ui/Tooltip` 12 px,
`OutlinedField` 14 / 20 px, the workspace menu bar 12 px and status bar 10 px, the dock tabs 13 px, the office ribbon
14 / 11 / 10 px): the desktop ports keep those literal values where they paint with their own format (workspace,
dock, ribbon) and use the nearest role where they paint with a shared one (tooltip → `Meta`, outlined field →
`Body` / `Title`).

Weights (web, enforced by the host CSS): running text **500**, `font-medium` → **600**, buttons **500** and never
bold. Elements choose a role, never a size or a font: `Font` is a desktop-only `Control` property (allowlisted).

### 7.2 Faces per target

- **Web**: the production faces, served by the core itself from `/fonts/` (`core/frontend/public/fonts/`, declared
  in `src/index.css`): **Plus Jakarta Sans** (variable 200–800, roman and drawn italic), then **Outfit** (variable
  100–900) and **Roboto** (variable 100–900) as fallbacks, `Arial, sans-serif` last; monospace **DM Mono** 400 then
  `Fira Code, monospace`. Licences: SIL OFL 1.1 for Plus Jakarta Sans, Outfit and DM Mono, Apache 2.0 for Roboto.
- **Desktop**: **Segoe UI Variable** (Text / Display optical sizes; earlier user decision — Plus Jakarta Sans stays
  on the web), DirectWrite fallback chain of the OS.

### 7.3 Rendering web views with the production fonts (requirement)

The user requirement « pour la version web, tu vas devoir rapatrier la police utilisée pour obtenir le même rendu »:

- Every web rendering of a view — production, the project's Vite dev server, the **Visual Studio design surface**
  (WebView2), `@kubuno/host-runtime`, the VSIX's bundled fallback design host — **MUST** use exactly the production
  font files (same `.woff2` bytes, same `@font-face` declarations: families, weight ranges, styles,
  `font-display`) and the host's own CSS that sets roles and weights (`index.css` + `theme.css`, never re-declared by
  a module or by the design host).
- The fonts are **provided locally**: `@kubuno/host-runtime` ships `fonts/*.woff2`, the `@font-face` CSS and the
  licence files; the dev server serves the host's `/fonts/`; the fallback design host bundles the same files. No CDN,
  no network fetch, no system-font substitute.
- **Licences ship with the files** (OFL 1.1 requires its text to accompany redistributed fonts). Today
  `PlusJakartaSans-OFL.txt` and `Outfit-OFL.txt` are in `public/fonts`; **the DM Mono (OFL) and Roboto (Apache 2.0)
  licence texts are missing** and MUST be added before `@kubuno/host-runtime` is published.
- The design surface renders only after `document.fonts.ready`, then checks each face with
  `document.fonts.check('13.5px "Plus Jakarta Sans"')` (and the italic, mono); a missing face is reported in
  `surfaceInfo` (`"fonts": {"Plus Jakarta Sans": "loaded", …}`) and shown as an info bar ("Polices de production
  absentes : le rendu diffère de la production"), never silently replaced.
- The registry carries the contract: `kbview-registry.web.json` → top-level `typography` (faces with file and
  licence, family stacks, role sizes, weights, §10.6). Its test fails when it no longer matches `index.css`,
  `theme.css` or the files in `public/fonts`.

### 7.4 Text metrics in parity checks

- **Web ↔ web** (TSX before, view after): same engine (Chromium, headless Chrome or WebView2), same fonts, same CSS
  → **pixel parity with the usual small tolerance** (anti-aliasing), after `document.fonts.ready`; a text-metric
  difference here is a bug, not noise.
- **Web ↔ desktop**: never compared pixel to pixel — the sizes are the same (§7.1), but the faces
  (Plus Jakarta Sans vs Segoe UI Variable, whose advances differ at the same size) and the rasterisers differ by
  design. They are compared by **structure**: the same element tree, the
  same text content, the same roles, accessibility tree and layout relations (order, alignment, which element fills),
  with a size tolerance proportional to the role's line box.
- Captures freeze animations, fonts load before capture, and text is compared in `fr`, `en` and `ar` (RTL), as the
  WEB-VIEWS §7 parity method says.

## 8. Events

- An event is an attribute named `On<Event>` whose value is a **handler name** (a code-behind method):
  `OnClick="save_click"`. Older names are accepted as **aliases** (`OnToggled` → `OnCheckedChanged`, `OnChanged` →
  `OnTextChanged`).
- **Default handler name** (designer ⚡ double-click, both targets — the desktop language server's
  `view_handler_name`, Windows Forms style): `<x:Name in snake_case>_<event display name in snake_case>`
  (`save_click`, `prefs_checked_changed`); without `x:Name`, the element name (`button_click`); the root's view
  events use the view's file stem (`notes_settings_page_load`, like `Form1_Load`).
- Every control (`Control` in its `base_chain`) has the **common events** (`common: true`, `inherited_from:
  "Control"`): `OnClick`, `OnDoubleClick`, `OnMouse*`, `OnKey*`, `OnEnter`/`OnLeave`, `OnGotFocus`/`OnLostFocus`,
  `OnResize`, `OnSizeChanged`, `OnDrag*`; desktop-only: `OnValidating`/`OnValidated`, `OnMove`,
  `OnLocationChanged`, `OnPaint`. The view's root also takes the **view events** (`root_only: true`, `inherited_from:
  "View"`): `OnLoad`, `OnShown` (both), the window events (desktop), `OnUnload` (web only).
- `default_event`: the event the designer's double-click handles (`OnClick` for a button); the view's root uses
  `OnLoad`. A target that lacks the other's default sets `null` (allowlisted).
- **EventArgs naming**: `<Name>EventArgs`, chain ending in `EventArgs` (`args_chain`). Catalogue (both targets):
  `EventArgs`, `MouseEventArgs`, `KeyEventArgs` (`handled`), `KeyPressEventArgs` (`handled`), `CancelEventArgs`
  (`cancel`), `DragEventArgs`, `ValueChangedEventArgs` (`value`), `ItemEventArgs`, `ItemActivateEventArgs`; desktop
  also `PaintEventArgs`, `DrawItemEventArgs`, `MeasureItemEventArgs`, `Cell*EventArgs`, `FormClosing*EventArgs`,
  `DpiChangedEventArgs`, printing and data args. `args_mut: true` marks args a handler writes back into; on the web
  `e.handled = true` → `stopPropagation()`, `e.cancel = true` → `preventDefault()` (and refusal of the change).
- Routing is `Direct` (raised on the element) unless the registry says `Bubble`.

## 9. Code-behind

### 9.1 Web (decision 2): a class extending the generated `ViewBase`

```ts
import { bind, type Button, type MouseEventArgs } from '@kubuno/views'
import { ViewBase } from './NotesSettingsPage.kbview'      // generated by the Vite plugin

export class NotesSettingsPage extends ViewBase {
  @bind accessor busy = false                               // bindable state (TC39 accessor decorator)
  use() { /* the only place React hooks run */ }
  async save_click(sender: Button, e: MouseEventArgs) { … } // handler = plain method
}
export default NotesSettingsPage.component()                 // a React component
```

- `ViewBase` (generated, never committed) declares one **typed handle field per `x:Name`** (`save: Button`, handle
  types named like the elements, from `@kubuno/views`) and every handler the view names as an **abstract method**
  (`abstract save_click(sender: Button, e: MouseEventArgs): void | Promise<void>`): a missing handler is a `tsc`
  error at the attribute; a method with fewer parameters is accepted.
- Handler signature: `(sender: <ElementHandle>, e: <ArgsType>) => void | Promise<void>`, `sender` typed by the
  element, `e` by the event's `args_type`.
- `@bind accessor` fields notify bound elements; getters are bindable read-only; `dataContext` holds paths not found
  on the instance; `use()` runs on every render and is the only place hooks are allowed; `this.props` holds the
  root's `x:Props`.
- Lifecycle: `OnLoad` after the first commit, `OnShown` (dialogs), `OnUnload` on unmount; writes after unmount are
  dropped with a development warning.

### 9.2 Desktop (unchanged, `PROGRAMMING-MODEL.md`)

`#[kubuno::view("x.kbview")]` on the struct, `#[bind]` fields, `#[data_context]`, handlers as methods of an `impl`
block with the several shapes `EVENTS.md` §5.4 lists. The two models correspond member for member
(WEB-VIEWS §2.2 table).

## 10. The element registry — `kbview-registry.json` schema (version 1)

### 10.1 Document

The desktop serves `kubuno/registry` (language server) and prints `view_embed --export-registry`:

```json
{ "version": "<16 hex>", "components": [ <ComponentJson>, … ] }
```

The web file `kbview-registry.web.json` (shipped in `@kubuno/ui`) has the same shape plus three top-level fields:

```json
{ "schema": 1, "target": "web", "version": "<16 hex>", "typography": { … }, "components": [ … ] }
```

- Field names are **snake_case** (vskubuno's `RegistryJsonOptions` snake-case policy).
- `version` = 64-bit FNV-1a, hex, of the components' JSON (web: of `{typography, components}`): a cache key, not a
  checksum.
- Consumers MUST ignore unknown fields (`System.Text.Json` and serde both do by default), so web additions never
  break the desktop consumers. The desktop export keeps its **exact** key sets (its own test asserts them).
- The web file is written with one component per line (each element repeats its inherited members, as the desktop
  export does; ~1.1 MB for 40 elements).

### 10.2 `ComponentJson`

| Field | Type | Meaning |
|---|---|---|
| `name` | string | canonical element name |
| `doc`, `doc_fr` | string, string\|null | English / French user documentation |
| `family` | string | Toolbox tab: `core`, `display`, `choice`, `text`, `containers`, `data`, `docking`, `navigation`, `components`, `ribbon`, `project` |
| `icon` | string | Toolbox glyph: kebab-case of the name, or the project control's own |
| `children` | `"None"` \| `"SingleWidget"` \| `"List"` | children model |
| `allowed_children` | string[] | gated child names (empty = any, for `List`) |
| `layout_kind` | `"Flow"` \| `"DockAnchor"` \| `"Split"` \| `"Tabs"` \| null | layout engine |
| `properties` | PropertyJson[] | own, then inherited levels nearest first, then the view's (`root_only`) |
| `events` | EventJson[] | own, then the common ones not overridden, then the view's (`root_only`) |
| `default_event` | string\|null | the designer's double-click event |
| `base_chain` | string[] | the class then its ancestors, `Component` last |
| `origin` | `"builtin"` \| `"project"` | |
| `kind` | `"control"` \| `"user_control"` \| `"component"` | `component` = non-visual (component tray) |
| `non_visual` | bool | |
| `linked`, `extends`, `crate_name`, `toolbox_category`, `toolbox_icon`, `browsable`, `default_property`, `view_path`, `source_file`, `source_line`, `design_size` | | project controls (EVT-7b); `null`/defaults for built-ins |
| `design_defaults` (web; desktop: optional, future) | `{attributes: {…}, size: [w,h]\|null}` | what the designer writes when the element is added from the Toolbox |
| `web` (web only) | WebBlockJson | §10.5 |

### 10.3 `PropertyJson`

`name`, `kind` (`"Bool"` \| `"F32"` \| `"String"` \| `{"Enum": [...]}` — serde's externally tagged encoding),
`default` (string, `""` = none), `doc`, `doc_fr`, `category` (`Accessibility`, `Appearance`, `Behavior`, `Data`,
`Design`, `Focus`, `Icon`, `Layout`, `Misc`, `Paging`, `Printing`, `Title Bar`, `Window Style`), `browsable`,
`bindable`, `localizable`, `serialization` (`"Visible"`/`"Hidden"`/`"Content"`/null), `editor` (`icon`, `image`,
`color`, `font`, `cursor`, `list`, `object`, `reference:<Class>`, `class:<Class>`, null), `type_converter`,
`inherited_from` (declaring level, null = own), `root_only`, `design_time`, `aliases`.

### 10.4 `EventJson`

`name`, `display_name` (without `On`), `doc`, `doc_fr`, `category` (`Action`, `Appearance`, `Behavior`, `Data`,
`Drag Drop`, `Focus`, `Key`, `Layout`, `Mouse`, `Property Changed`), `args_type`, `args_chain`, `args_rust_type`
(desktop: the Rust args type; web: the `@kubuno/views` TS type, same name as `args_type`), `args_mut`, `cancelable`,
`routing` (`Direct`/`Bubble`), `aliases`, `browsable`, `root_only`, `common`, `inherited_from`.

### 10.5 The `web` block

Read only by the web compiler, runtime and designer.

| Field | Meaning |
|---|---|
| `module`, `export` | import specifier (`"@ui"`, `"@kubuno/sdk"`) and named export; both `null` for an item |
| `dom_root` | how the designer reaches the root DOM node: `ref` (forwards a ref), `wrapper` (layout-neutral wrapper in design mode), `portal` (map the portal's root), `none` (not rendered) |
| `prop_map` | per property: `{prop, convert?, values?, change?}` (a React prop), `{prop, field, convert?, values?}` (a field of an object prop: `ActionLabel` → `action.label`) or `{runtime}` (applied by the runtime around the component). `values` maps `.kbview` enum values (keys verbatim) to prop values. `change` names the event of a two-way binding |
| `event_map` | per event: `{prop, args}` (a callback prop), `{prop, field, args}` (a callback field), `{dom, args}` (a DOM listener on the root) or `{runtime, args}` (`view-load`, `view-shown`, `view-unload`, `parent-adapter`, `menu-open`) |
| `children_to_prop` | children → prop adapter: `{prop, shape?: "array"\|"record", item, content?, key?, nested?}`; `content` = where an item's own child goes (`{field}`, `"selected-after"`, `"none"`), `key` = item field given a stable key (`x:Name` or index), `nested` = item field receiving an item's own items (sub-menus) |
| `content` | the prop that receives the single child (`children`) |
| `slots` | property-element name → ReactNode prop (`Actions` → `actions`) |
| `alternates` | `[{when: {Prop: value}, module, export, dom_root, prop_map, event_map, fixed}]`: another component rendering the element for those values (`TextField Variant="Outlined"` → `OutlinedField`); a member absent from its maps is unavailable in that variant |
| `item_of` | for an item: the parent elements whose adapter consumes it |
| `fixed` | props always passed |
| `web_only` | members that exist on the web only (each allowlisted) |

**Converters** (`convert`): `identity`, `icon-node`, `icon-component`, `invert`, `null-when-false`, `equals-value`,
`index-to-key`, `items-source`, `element-ref`, `binding-cell`, `gradient-css`, `status-text`, `workspace-theme`.
**Runtime targets** (`runtime`): `visible`, `enabled`, `aria-label`, `aria-description`, `aria-role`, `tab-index`,
`tab-stop`, `tooltip`, `tooltip-options`, `context-menu`, `drop-down-menu`, `layout`, `cursor`, `direction`,
`theme-color`, `allow-drop`, `design`, `tag`, `icon-size`, `icon-color`, `icon-scaling`, `class`, `view-title`,
`component-variant`, `item-state`, `element-mapped` (an inherited member that an element supports only through its
own mapping). **Args adapters** (`args`): `none`, `value`, `target-value`, `target-checked`, `mouse`, `key-to-index`,
`id-index`, `open-keys`, `item`, `row`, `sort`, `dom`. The exact semantics are documented in
`core/frontend/src/ui/kbview/types.ts`; the runtime (WV-3) implements them.

### 10.6 `typography` (web)

`faces: [{family, style, weight, file, license, license_file}]`, `families: {sans: [...], mono: [...]}`, `roles:
{Micro|Meta|Body|Heading|Title: {token, size}}`, `page_title: {token, size, admin_size}`, `weights: {body, medium,
button}` (§7).

### 10.7 Authoring (web)

One `*.meta.ts` table per component family next to the components (`src/ui/kbview/`, `src/sdk/kbview/`), each table
written `… as const satisfies ElementMeta<ComponentProps<typeof X>>` (items: `satisfies ElementMeta<ItemType>`), so a
renamed or retyped React prop, a wrong enum mapping or a callback that does not exist is a `tsc` error. Inherited
levels (`Control`, `TextBoxBase`, `View`) are shared tables (`levels.ts`), remapped per element with
`inheritedMap` (`Enabled` → `disabled`). The tables are pure data (`import type` only), so `node` runs them directly
(type stripping, Node ≥ 22.18) in `npm run build:registry`.

## 11. Conformance between the registries

- The web registry is compared with the **real desktop export** (`view_embed --export-registry`), reduced to a
  committed snapshot (`core/frontend/src/kbview/__fixtures__/desktop-registry.snapshot.json`: built-in elements,
  own members, inherited members once per level) or, with `KUBUNO_DESKTOP_REGISTRY=<export.json>`, a fresh export.
- For every web element with a desktop counterpart: `family`, `children`, `allowed_children`, `layout_kind`,
  `default_event`, `base_chain`, `kind`, `non_visual`; for every property (own and inherited): presence, `kind`
  (enum values included), `default`, `category`, `bindable`, `localizable`, `editor`, `type_converter`,
  `design_time`, `root_only`, `aliases`; for every event: presence, `category`, `args_type`, `args_chain`,
  `cancelable`, `aliases`, `root_only`.
- Every difference is a **failure** unless an entry of `core/frontend/src/kbview/allowlist.ts` accepts it
  (`{kind, element, member?, level?, field?, reason}`, `*` wildcards) — and an entry that accepts nothing is a
  failure too. Informational only: desktop elements absent from the web (coverage), `doc_fr` differences.
- Report: JSON (`failures`, `allowlisted` with reasons, `info`, `unused_allowlist`, `summary`) written by the test to
  `KBVIEW_CONFORMANCE_REPORT` (default: the OS temp directory) and by `npm run check:registry`.
- Refreshing the snapshot after a desktop registry change: `node scripts/kbview-conformance.mjs --snapshot
  <export.json>` (core frontend), then rerun the test. The desktop CI SHOULD run the same comparison from its side
  (WV-1 makes the registry model a platform-neutral crate).

### 11.1 Validation done for WV-0/WV-4 (2026-10-01)

Desktop export from `view_embed.exe --export-registry` (release build of the current desktop tree, 09:08):
`version bcfcf7447218cb9c`, 112 components (100 built-in, 12 project controls of the example), 5.6 MB. Web registry:
40 elements. Comparison: 40/40 web elements have a desktop counterpart with identical family, children model,
allowed children, layout kind, base chain and kind (two `default_event`s allowlisted); 0 unlisted differences;
2 105 allowlisted differences (mostly the 38 window properties and 11 window events of `View`, repeated on the 30
controls, and the WinForms `ButtonBase`/`TextBoxBase`/`LabelBase` members), 88 informational (60 desktop elements
not on the web yet, 28 French-doc differences, all intentional web notes). The web JSON was checked to have exactly the desktop key sets (component, property, event)
plus `design_defaults`/`web`.

## 12. Versioning

- `schema` changes only on an incompatible change of the JSON (a field removed or retyped). Additive fields keep
  `schema: 1`; consumers ignore what they do not know.
- Element vocabulary changes are additive by default (new element, new property, new enum value at the end).
  Renaming or removing a name is a breaking change for every view: it needs an alias (`aliases`) first, a language
  server quick fix, and a minor (or major) bump of `@kubuno/ui` / the desktop crates.
- `kbview-registry.web.json` is regenerated by `npm run build:registry` and committed with the metadata change (its
  test fails when stale); it reaches modules with the next `@kubuno/ui` publish (a user action).

## 13. Where things live

| What | Desktop | Web |
|---|---|---|
| Registry tables | `kubuno-views/src/registry/` (`components.rs`, `families/*`, `common.rs`, `docs_fr.rs`) | `core/frontend/src/ui/kbview/*.meta.ts`, `src/sdk/kbview/*.meta.ts`, `levels.ts`, `typography.ts` |
| Export | `registry/export.rs` → `kubuno/registry`, `--export-registry` | `src/kbview/export.ts` → `packages/ui/kbview-registry.web.json` |
| Conformance | (WV-1: desktop CI) | `src/kbview/conformance.ts`, `allowlist.ts`, `kbview-registry.spec.ts`, `scripts/kbview-conformance.mjs` |
| Consumer | `kubuno-views-ls`, vskubuno `ComponentRegistry.FromJson` | WV-2 compiler, WV-3 runtime, WV-7 language-server web profile |

## 14. Corrections to `WEB-VIEWS.md`

Found while freezing the spec against the real desktop registry:

- `Label`'s text style property is **`Role`** (`Micro`, `Meta`, `Body`, `Heading`, `Title`), not `TextStyle`
  (`Body|Caption|Title|Heading|Overline`).
- `TableLayoutPanel` uses **`ColumnCount`/`RowCount`/`ColumnStyles`/`RowStyles`**, not `Columns`; responsive form:
  `ColumnCount.Compact="1"`.
- `Stack` uses **`CrossAlign`** and **`WrapContents`** (not `Align`, `Wrap`), and its **`Padding` is one F32**
  (`Padding="0, 20, 0, 0"` in WEB-VIEWS §2.2's example is invalid on a `Stack`; use a `Margin` on the child or a
  `Panel`).
- The binding grammar has no `FallbackValue` in version 1 (§6.1).
- A repeater's design-time count is **`DesignItemCount`**, not `d:ItemsCount`; `DesignWidth`/`DesignHeight` are plain
  root attributes, not `d:` ones.
- The registry JSON keys are snake_case: `prop_map`, `event_map`, `children_to_prop`, `dom_root` (not `propMap`…).
- The web registry is one file in `@kubuno/ui` covering the `@ui` **and** sdk elements (each with its `module`),
  not one file per package.

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
