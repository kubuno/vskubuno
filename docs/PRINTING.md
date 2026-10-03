# Printing — the Windows Forms printing stack for Kubuno desktop applications (design and as built)

> Scope: product-owner requirement of `docs/EVENTS.md` ("the WinForms printing stack", 2026-09-29): `PrintDocument`,
> `PrinterSettings`/`PageSettings`, `PrintPreviewControl`, `PrintPreviewDialog`, `PrintDialog`, `PageSetupDialog`, with
> their designer tooling. Status: **built** (2026-09-30). Code in `Z:\src\desktop\windows` (uncommitted there): the new
> crate `src/crates/kubuno-print`, and small changes in `kubuno` (facade), `kubuno-views`, `kubuno-views-meta`,
> `kubuno-views-macros`, `kubuno-views-ls`, `kubuno-data` (a test); in this repository the designer's Toolbox tab and
> category, the icons, the sample `samples/printing-desktop` and this note.

## 1. What an application writes

Designed (the three-file model of `docs/PROGRAMMING-MODEL.md`):

```xml
<PrintDocument x:Name="print_document1" DocumentName="Invoice" OnBeginPrint="print_document1_begin_print" OnPrintPage="print_document1_print_page"/>
<PrintPreviewDialog x:Name="print_preview_dialog1" Document="print_document1"/>
<PrintDialog x:Name="print_dialog1" Document="print_document1" AllowSomePages="true"/>
<PageSetupDialog x:Name="page_setup_dialog1" Document="print_document1"/>
```

```rust
fn print_document1_print_page(&mut self, _sender: &Control, e: &mut PrintPageEventArgs) {
    let g = e.graphics();                       // the EVT-8 Graphics
    g.draw_string("Invoice", &font, Color::BLACK, e.margin_bounds, &StringFormat::generic_default());
    e.has_more_pages = self.next_line < self.lines.len();
}

fn print_click(&mut self, _sender: &Button, _e: &MouseEventArgs) {
    if self.print_dialog1.show_dialog(self) == DialogResult::Ok {
        let _ = self.print_document1.print();   // prints when this handler returns (§4)
    }
}
```

In code (Windows Forms' `new PrintDocument()`, `PrintPage += …`):

```rust
let doc = PrintDocument::new().document_name("Report");
doc.on_print_page(move |_, e| { /* draw */ e.has_more_pages = more; });
PrintPreviewDialog::new().document(&doc).show_dialog(self);
```

Printing into a PDF without any dialog (what the tests and the sample's `--print-to` use):

```rust
let mut s = doc.printer_settings();
s.printer_name = "Microsoft Print to PDF".into();
s.print_to_file = true;
s.print_file_name = r"C:\out\report.pdf".into();
doc.set_printer_settings(s);
doc.print()?;                                    // the file is complete when it returns
```

`kubuno::printing` has the handles (`PrintDocument`, `PrintPreviewDialog`, `PrintDialog`, `PageSetupDialog`,
`PrintPreviewControl`) and re-exports the settings and args; `kubuno::prelude` includes the handles and the three args
types; the component classes themselves are `kubuno::printing::components` (`kubuno_print`). The facade depends on
`kubuno-print` unconditionally (printing is part of Windows Forms itself); it adds no `windows` feature (§3).

## 2. Components (`kubuno-print`)

| Class | Kind | Properties (XML) | Events |
|---|---|---|---|
| `PrintDocument` | component (tray) | `DocumentName`, `OriginAtMargins`; Kubuno additions (WinForms has them under `DefaultPageSettings`/`PrinterSettings`): `PrinterName`, `Landscape`, `PaperSize` (Letter, Legal, A4, A3, A5 or a paper name of the printer), `Margins` (`"l, r, t, b"`, 1/100 inch), `Copies`, `PrintToFile`, `PrintFileName` | `BeginPrint`, `QueryPageSettings`, `PrintPage` (default), `EndPrint` |
| `PrintPreviewDialog` | component | `Document` (reference drop-down), `Text` (title), `Width`, `Height` | — |
| `PrintDialog` | component | `Document`, `AllowCurrentPage`, `AllowPrintToFile`, `AllowSelection`, `AllowSomePages`, `PrintToFile`, `ShowNetwork` | — |
| `PageSetupDialog` | component | `Document`, `AllowMargins`, `AllowOrientation`, `AllowPaper`, `AllowPrinter`, `MinMargins`, `ShowNetwork` | — |
| `PrintPreviewControl` | control | `Document`, `AutoZoom`, `Zoom`, `Columns`, `Rows`, `StartPage` | `StartPageChanged` |
| `PreviewPagesButton` | control, hidden from the Toolbox | the preview dialog's layout buttons (`Pages`, `Checked`) | `Click` |

Code API: `PrinterSettings` (`installed_printers()`, `default_printer_name()`, `copies`, `collate`, `duplex`,
`print_range`/`from_page`/`to_page`, `print_to_file`/`print_file_name`, `paper_sizes()`, `paper_sources()`,
`printer_resolutions()`, `can_duplex()`, `supports_color()`, `maximum_copies()`, `is_valid()`, `is_default_printer()`,
`default_page_settings()`), `PageSettings` (`landscape`, `margins`, `paper_size`, `paper_source`, `color`,
`printer_resolution`, and after resolution `hard_margin_x/y`, `printable_area`; `bounds()`, `margin_bounds()`),
`Margins`, `PaperSize`, `PaperSource`, `PrinterResolution`, `PrintRange`, `Duplex`, `PrintAction`, `PrintError`.
`PrintDocument::print`, `print_now`, `show_preview`, `render_preview` (a `PreviewDocument`), `render_recorded` (pages as
display lists, for tests), `printer_settings(_mut)`, `default_page_settings(_mut)`, `on_print_page`… (`Subscription`s
kept by the document), `invalidate`, `revision`, `last_error`.

**Args.** `PrintEventArgs { cancel, print_action }`, `QueryPageSettingsEventArgs { cancel, print_action, page_settings,
page_number }`, `PrintPageEventArgs { cancel, has_more_pages, page_bounds, margin_bounds, page_settings, page_number,
graphics() }` — all `CancelEventArgs` in the args chain (the ⚡ tab offers `&mut`).

**Loop** (WinForms' `PrintController.Print`): `BeginPrint` (cancel: nothing happens, no `EndPrint`); per page
`QueryPageSettings` (a copy of `DefaultPageSettings`, changeable for that page) then `PrintPage` while `has_more_pages`;
`cancel` in either stops the job (the spool job is cancelled); then `EndPrint`. A safety stop at 100 000 pages.

## 3. How a page reaches the printer — and why this route

- Every page is recorded into an **`ID2D1CommandList`** on a Direct2D device of the job's own (a detached `Renderer`,
  no window), through the EVT-8 `Graphics` over the host's `Painter` — so `draw_string`, paths, gradients, images, and
  the canvas primitives the `kubuno_ui` widgets use (a control printed with `draw_to_bitmap`) all work on paper.
- **`ID2D1PrintControl`** turns each command list into a page of an **XPS print job** of the Windows spooler
  (`IPrintDocumentPackageTargetFactory::CreateDocumentPackageTargetForPrintJob`). The job's **print ticket** comes from
  the driver's `DEVMODE` (`DocumentProperties` merges paper, orientation, copies, collation, duplex, colour, tray,
  resolution; `PTConvertDevModeToPrintTicket` converts it); a page whose settings differ (`QueryPageSettings`) gets a
  page-scope ticket.
- **Print to file**: the job's output stream is a file stream — "Microsoft Print to PDF" writes a PDF there, silently.
  `print()` then waits for the spooler to finish the job (polling its queue for the job's id, up to 120 s) so the file
  is complete when it returns; without a file name a Save dialog (`GetSaveFileNameW`) offers the printer's format.
- **Why not GDI** (`CreateDC` + `StartDoc`): Direct2D cannot draw vectors onto a printer DC; the only bridge is a
  `ID2D1DCRenderTarget`, which rasterises every page (huge spool files, image-only PDFs, no selectable text). The
  XPS/Direct2D route is what Windows recommends for Direct2D applications, keeps text as glyph runs with embedded font
  subsets (verified: the PDF has `/FontFile` streams and no images), works with every v3 and v4 driver through the
  spooler's XPS-to-GDI conversion, and lets the same command lists feed the preview. `rasterDPI` (fallback for
  effects that cannot be vector) is the printer's resolution clamped to 150–600.
- **No new `windows` feature**: `ID2D1Device::CreatePrintControl` and `IPrintDocumentPackageTargetFactory` sit behind
  `Win32_Storage_Xps_Printing`, absent from `kubuno-ui`'s graph; adding it would rebuild `windows` and every crate built on it
  for every application. `src/xps.rs` declares the two interfaces with `windows_core::interface` and calls `CreatePrintControl`
  through its vtable slot (documented). The flat APIs (winspool, gdi32 metrics, prntvpt, comdlg32) use `windows-sys`,
  which is not in `kubuno-ui`'s graph (as `kubuno-data` does).
- Printer drivers are not all thread-safe (two threads asking "Microsoft Print to PDF" for its settings crashed the test
  harness): every driver call takes a process-wide lock.

**Units.** Settings keep Windows Forms' hundredths of an inch. The page `Graphics` and the args' bounds are **DIP (1/96
inch) from the physical page's corner** (a deviation: WinForms' printer `Graphics` uses 1/100 inch from the printable
area's corner). With `OriginAtMargins` the `Graphics` is translated to the margins' corner (its own ops; canvas
primitives stay in page coordinates).

**Page range.** `PrintRange::SomePages` (from the Print dialog) is honoured by the spooler sink: pages outside
`from_page..=to_page` are rendered (the handler's state advances) but not sent — WinForms leaves that to the handler.
`Selection` and `CurrentPage` are the handler's to interpret, as in WinForms.

## 4. Events of a view's document: the deferred print

A `.kbview` handler is a method of the view (`&mut self`). While one of the view's handlers runs, the view model is
borrowed, so a document of that view cannot raise its own XML handlers (that would be a second `&mut`). The rule:

- A component raises to its Rust subscribers, then `kubuno_views::scope::raise_now(name, "OnPrintPage", args)`, which
  runs the XML handler synchronously when the runtime lent its sink.
- `print()` (and `show_preview`, i.e. `PrintPreviewDialog.show_dialog()`) called while the view is busy
  (`view_is_busy`: the component is sited in the running view and `scope::can_raise_now()` — new in `kubuno-views` — is
  false) records the request and asks for a frame; the document is a `BindingProvider` whose `binding_sync` (called by
  the runtime once per frame with the sink lent) runs it. **Net effect: the print or preview starts right after the
  handler that asked for it returns, before the next paint.** `print()` returns `Ok(0)` then; `last_error()` tells
  what happened; `EndPrint` is raised as usual. A preview opened this way runs its modal loop inside the owner's sync,
  so its Print button prints through the owner's XML handlers too.
- Documents of code (closures) and documents reached outside a handler print at once, synchronously.
- `PrintDialog`/`PageSetupDialog` raise no view event: they are synchronous and return their `DialogResult`.
- `PrintPreviewControl` (named, so the runtime reaches it) renders its pages in its own `binding_sync` when stale (its
  document's `revision()` changed, a property changed, `invalidate_preview()`); a document of code renders in `on_paint`.

## 5. The preview

- `PreviewDocument`: the pages' command lists on their `Surface`. The control rasterises each **visible** page at the
  window's pixel size (`ID2D1Bitmap1` target, `DrawImage` of the command list, CPU read-back), uploads it to the
  window's own device (`Image::from_bitmap`) and caches it per device, page and size.
- Layout (`PrintPreviewControl::layout`, unit-tested): `Columns × Rows` cells as large as the largest page shown, 10 DIP
  gaps (WinForms' border), `AutoZoom` fits the grid, otherwise `Zoom` (1.0 = actual size) and the content scrolls
  (wheel, arrows); Page Up/Down, Home/End and the wheel (when nothing scrolls) move `StartPage`. Kubuno look: pages with
  the card shadow on `surface_2`; "Génération de l'aperçu…", "Aucune page à afficher.", the error text; in the designer
  an empty Letter sheet.
- `PrintPreviewDialog` window: a host window (`run_scoped`, modal to the application's window, Kubuno chrome, the
  application's theme — `kubuno::Application::set_theme` forwards it through `kubuno_print::set_dialog_theme`) showing
  an inline `.kbview`: **Print** (`IconButton` Printer), **Zoom** (`Dropdown`: Automatique, 500 %…10 %), five
  `PreviewPagesButton`s (1, 2, 3, 4, 6 pages; the current one highlighted), **Page** (`NumericField` bound to
  `preview.StartPage`) "sur N" (`preview.PageText`), **Fermer**. FR/EN texts follow the user's UI language; every tool
  has an accessible name.

## 6. Tooling

- **Registry and view macro**: the classes register through `#[derive(Component)]` (Toolbox category `Printing`,
  icons `printer`, `printer-check`, `file-sliders`, `file-search`, `scan-eye`). `kubuno-views-meta` gained
  `DATA_ELEMENTS`, `PRINT_ELEMENTS` (their union is `LIBRARY_ELEMENTS`, tested) and `PRINT_TYPED`: `#[kubuno::view]`
  types a named `<PrintDocument>` field `kubuno::printing::PrintDocument` (likewise the dialogs and the preview control).
- **Language server**: `kubuno-views-ls` now follows `name = { workspace = true }` dependencies through the workspace
  root's `[workspace.dependencies]` — how an application depending on `kubuno` reaches `kubuno-print` (and `kubuno-data`):
  the printing elements are known (no diagnostics), completed, documented, and their handlers get
  `fn print_document1_print_page(&mut self, _sender: &Control, _e: &mut PrintPageEventArgs)`.
- **Designer**: components from the Kubuno library crates (`kubuno_print`, `kubuno_data`) get Toolbox tabs of their own
  by `#[toolbox(category)]` — **"Impression"** (FR) / "Printing" — in every project, without "Choose Items…"
  (`NativeToolboxInstaller.LibraryItems`); they go to the component tray (non-visual); the Properties window shows the
  `Printing` category as "Impression"; `Document` is a drop-down of the view's `PrintDocument`s (the existing
  `reference:` editor); a double-click on a `PrintDocument` creates its `PrintPage` handler (its default event).
  Icons: 16 px hinted Lucide icons from `tools/generate-control-icons.ps1` (`PrintDocument`, `PrintPreviewControl`,
  `PrintPreviewDialog`, `PrintDialog`, `PageSetupDialog`).
- **Design surface**: the VSIX's bundled `tools\surface\view_embed.exe` is now built from `kubuno-print`'s
  `examples/view_embed.rs` (kubuno-views' surface with the data and printing components linked):
  `cargo build --release -p kubuno-print --example view_embed`.
- **Accessibility** (`kubuno-views`, `node/custom.rs`): UI Automation's Invoke now clicks custom controls (it only
  clicked built-in buttons) — found testing the preview dialog's page buttons.

## 7. As built: tests and live verification (2026-09-30)

- `kubuno-print`: unit tests (dialog view compiles and owns its controls; tool-bar state), `tests/printing.rs` (margins
  and papers; orientation; the event order over 3 pages with `QueryPageSettings` making page 2 landscape; cancel in
  `BeginPrint` and in `PrintPage`; `OriginAtMargins`; XML properties vs code settings; preview layout, start page,
  zoom, a rasterised blank page is white; layout glyphs; registry classes = `PRINT_ELEMENTS`, non-visual, category,
  events, reference editor; a real `Runtime` owning the view's components whose XML `PrintPage` handler draws the pages
  through `with_dispatch`, and the view's preview control rendering in the same sync), 1 doctest. `kubuno`:
  `tests/printing.rs` (typed fields; the view's own handler methods draw the pages through the composed view and the
  runtime; a document of code) and `tables.rs` adapted (built-ins exclude linked library classes). `kubuno-views-ls`:
  workspace dependencies. `kubuno-views-meta`: the tables. C#: `PrintingToolboxTests` (3, on the real registry answer of
  the language server). All suites green (`kubuno-views` 610 + integration, `kubuno-views-ls` 128 + 18, facade, macros;
  C#: Designer 346, VisualStudio 556, Views 18, TestAdapter 49, Launch 73, Cargo 411, Mcp 29, MSBuild tasks 28). `cargo
  clippy --all-targets -D warnings` clean on the touched crates; 0 C# warning.
- **PDF**: `examples/print_to_pdf.rs` → 3 pages (page 2 landscape by `QueryPageSettings`: MediaBox 842×595), fonts
  embedded, no image. The sample `printing-desktop.exe --print-to out.pdf` (a **view** document, printed from the load
  handler → deferred → XML handlers) → a 3-page PDF, then the window closes from `EndPrint`.
- **Preview dialog** (sample `--preview`, driven through UI Automation): 1 / 3 / 4 / 6 pages layouts, landscape after
  Page Setup, the code-first preview, dark theme (`--dark`). **Page Setup** (native) → Landscape → OK wrote back ("A4
  landscape"). **Print dialog** → Pages 2-3 + Print to file → Save dialog → a 2-page landscape PDF of pages 2-3.
- **Visual Studio** (hive `KubunoPrint`): the sample's view in the designer with the four components in the tray and
  their icons; Toolbox tab "Impression" with the printing components.
- Not automated: the zoom drop-down and the page number box (their UI Automation patterns are not published by the
  runtime); a physical printer (only the Microsoft PDF/XPS drivers exist here).

## 8. Limits

- Deferred printing: a view handler cannot read the page count of a print it starts (use `EndPrint`/`last_error`).
- A preview dialog created in code over a document of the view (`PrintPreviewDialog::new().document(&self.print_document1)`)
  defers through the document too; `DialogResult::None` is returned then.
- No `PrintController` property (standard/preview/with-status-dialog): printing shows no "Printing page n" dialog.
- The page `Graphics` has no `PageUnit`; `HardMarginX/Y` and `PrintableArea` are informational.
- `PrintPreviewControl` without an `x:Name` cannot render a document of the view (the runtime reaches named controls).
