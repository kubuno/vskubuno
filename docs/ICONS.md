# Icons

Every control and component that shows an icon takes it the same way, in the designer, in the
`.kbview` and in code. This page describes the icon value, how icons are drawn, and the tools.

## 1. The icon value

An icon property holds one of these values:

| Value | Example | Meaning |
|---|---|---|
| A name of the Kubuno icon set | `Icon="Save"`, `Icon="FolderOpen"` | A glyph of the embedded set: Lucide's icons, the Kubuno themed icons and the module logos, spelled exactly as the set spells them. A few lower-case aliases are kept (`trash` is `Trash2`, `close` is `X`). |
| An image file, relative to the view | `Icon="resources/save.svg"` | SVG, PNG, ICO, JPEG, BMP, GIF (first frame), TIFF or WebP (WebP needs Windows' codec, present on Windows 10 1809+ and Windows 11). The syntax is the one `BackgroundImage` and `Image` already use. |
| A resource of the project | `Icon="{Res Logo}"` | An image of a `.kbres` resource file (see the resources documentation). |
| A binding | `Icon="{Binding StatusIcon}"` | Any of the above, read from the view model at run time. |

A value with an image extension is a file; anything else is a glyph name. An empty value means no
icon. The language server warns about a name that is not in the set (with a suggestion, `save2` →
`Save`), an unsupported file type, and a file that is not beside the view.

The icon properties are declared with `.editor("icon")` in the registry (`PropKind::String`). That is
the "icon kind": the language server completes them, the validator checks them, the Properties
window gives them the icon editor, and the element also takes the icon options below.

| Element | Icon properties |
|---|---|
| The view (the window) | `Icon`: title bar, task bar, Alt+Tab |
| `Button`, `IconButton`, `MenuItem`, `ToolbarItem`, `SidebarItem`, `StatusLabel`, `EmptyState`, `DockPanel`, `WorkspaceShell`, `FloatingWindow` | `Icon` |
| `Icon` | `Name` |
| Ribbon elements, `Command` | `SmallIcon`, `LargeIcon`; `Icon` on a group, a box, a backstage tab |

## 2. How an icon is drawn

Every element with an icon property also takes these properties (category « Icône » / "Icon"):

| Property | Values | Default |
|---|---|---|
| `IconSize` | `Small` (16), `Medium` (20), `Large` (24), `XLarge` (32), a number of pixels, or `width, height` | the control's own size |
| `IconScaling` | `Fit` (all of it, as large as fits), `Fill` (covers the box, clipped), `Stretch` (the box exactly), `None` (its own size) | `Fit` |
| `IconColor` | a theme colour (`Primary`, `TextSecondary`…), `#RRGGBB[AA]`, a web or system colour | the control's colour |

Without `IconColor`, a glyph and an SVG drawn in `currentColor` take the control's colour, so they
follow its state and the light and dark themes; another image keeps its own colours. With
`IconColor`, every pixel of the icon takes that colour, its transparency kept.

A button also places its icon like WinForms places a button's image:

| Property | Values |
|---|---|
| `TextImageRelation` | `ImageBeforeText`, `TextBeforeImage`, `ImageAboveText`, `TextAboveImage`, `Overlay`. An icon goes before the text when this is not set. |
| `ImageAlign` | The nine-cell `ContentAlignment`, for `Overlay`; otherwise `TextAlign` places the icon and the text together. |
| `IconSpacing` | The space between the icon and the text, in pixels. |

The ribbon governs its own sizes: a large item shows `LargeIcon` at 32 pixels above its label, a
small one `SmallIcon` at 16 pixels before it.

Icons are rendered at the pixel size they cover on the screen: an SVG is drawn as vectors at that
size by Direct2D's SVG renderer, a raster image is resampled with a high-quality filter, an `.ico`
gives the frame closest to that size. The window's `Icon` is rendered the same way at the sizes
Windows asks for (an `.ico` is used as is).

## 3. In code

```rust
use kubuno_desktop::prelude::*;

let save = Button::new().text("Save").icon("Save");
let open = Button::new().text("Open").icon(IconSource::file("resources/open.svg"));
let logo = Button::new().text("Kubuno").icon(IconSource::resource("Logo"));
let print = Button::new().text("Print").icon("Printer").icon_size(32.0).icon_color("Primary")
    .text_image_relation("ImageAboveText");
let form = Form::new().text("Editor").icon("resources/app.svg");
```

`IconSource` converts from `&str`, `String`, `&Path` and `PathBuf`. (`kubuno_desktop::Icon` is the `<Icon>`
control, hence the name.)

## 4. In Visual Studio

- **Properties window**: every icon property shows the icon itself in its row; "…" opens the icon
  picker. `IconSize` lists the named sizes; `ImageAlign` and `TextAlign` open a 3 × 3 alignment grid.
- **Icon picker**: the whole Kubuno icon set as a searchable gallery (by name and keyword), by set or
  recently used; the project's images, Import… (a file outside the view's folder is copied into its
  `resources` folder), and the project's resource files; the chosen icon previewed at 16, 20, 24 and
  32 pixels on the light and the dark theme, rendered by the runtime's own code; No icon. OK writes
  the attribute as one undo unit.
- **Editor**: the icon properties complete the names of the set, each with a picture of it, and the
  aliases.

## 5. For tools

The language server answers `kubuno/icons` (the set, with every glyph's paths, its keywords, the
aliases and the named sizes) and `kubuno/renderIcon` (`{value, uri, size, color}` → straight-alpha
BGRA pixels, base64), which the Visual Studio picker uses.

On the Rust side: `kubuno_drive_desktop_app_controls::icon_source` describes a value and its options,
`kubuno_desktop_controls::icon_image` decodes and draws image icons, `kubuno_desktop_views::icon` resolves an icon
attribute (glyph, file, resource) for the controls.
