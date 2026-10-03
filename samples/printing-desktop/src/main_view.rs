//! The printing sample's window (`main_view.kbview`): a text, printed by the designed
//! `print_document1` (its `BeginPrint` / `PrintPage` / `EndPrint` handlers below), previewed by
//! `print_preview_dialog1`, set up by `page_setup_dialog1` and `print_dialog1` — and, with the
//! "Preview (code)" button, by a document and a preview dialog built entirely in code.

use std::cell::RefCell;
use std::rc::Rc;

use kubuno_desktop::prelude::*;
use kubuno_desktop::printing::PrintAction;
use kubuno_desktop::ui::graphics::{Color, Font, FontStyle, Pen, PointF, RectExt, StringAlignment, StringFormat};
use kubuno_desktop::ui::Rect;

/// Lays a text out on pages: one paragraph per line of the text, as many as fit between the margins.
#[derive(Default)]
struct Pager {
    lines: Vec<String>,
    next: usize,
    page: u32,
}

impl Pager {
    /// Starts again from the first line (`BeginPrint`: a preview then a print run the pages twice).
    fn begin(&mut self, text: &str) {
        self.lines = text.lines().map(str::to_string).collect();
        self.next = 0;
        self.page = 0;
    }

    /// Draws the next page; returns whether another page follows (`e.has_more_pages`).
    fn print_page(&mut self, title: &str, e: &mut PrintPageEventArgs) -> bool {
        self.page += 1;
        let g = e.graphics();
        let m = e.margin_bounds;
        let accent = Color::rgb(0x1A, 0x73, 0xE8);
        let heading = Font::new("Segoe UI", 16.0, FontStyle::BOLD);
        let body = Font::new("Segoe UI", 11.0, FontStyle::REGULAR);
        let small = Font::new("Segoe UI", 8.0, FontStyle::REGULAR);
        let format = StringFormat::generic_default();
        g.draw_string(title, &heading, accent, Rect::from_xywh(m.left, m.top, m.width(), 32.0), &format);
        g.draw_line(&Pen::new(accent, 1.5), PointF::new(m.left, m.top + 38.0), PointF::new(m.right, m.top + 38.0));
        let mut y = m.top + 52.0;
        let bottom = m.bottom - 28.0;
        while let Some(line) = self.lines.get(self.next) {
            let height = g.measure_string(line, &body, Some(m.width()), &format).height.max(12.0);
            if y + height > bottom && y > m.top + 52.0 {
                break;
            }
            g.draw_string(line, &body, Color::BLACK, Rect::from_xywh(m.left, y, m.width(), height), &format);
            y += height + 4.0;
            self.next += 1;
        }
        let footer = StringFormat::generic_default().with_alignment(StringAlignment::Far);
        g.draw_string(&format!("Page {}", self.page), &small, Color::rgb(0x66, 0x66, 0x66), Rect::from_xywh(m.left, m.bottom - 14.0, m.width(), 14.0), &footer);
        self.next < self.lines.len()
    }
}

/// A hundred numbered paragraphs: three pages of A4 or Letter.
fn sample_text() -> String {
    (1..=100).map(|i| format!("{i}. Kubuno prints this paragraph with Direct2D, through the Windows print spooler.")).collect::<Vec<_>>().join("\r\n")
}

/// `--print-to <file>`: print the text into that file with "Microsoft Print to PDF", then close.
fn print_to_argument() -> Option<String> {
    let args: Vec<String> = std::env::args().collect();
    args.iter().position(|a| a == "--print-to").and_then(|i| args.get(i + 1)).cloned()
}

#[kubuno_desktop::view("main_view.kbview")]
#[derive(Default)]
pub struct MainView {
    pager: Pager,
    close_when_printed: bool,
}

impl MainView {
    pub fn new() -> Self {
        let mut view = Self::default();
        view.initialize_component();
        view
    }

    fn main_view_load(&mut self, _sender: &Form, _e: &EventArgs) {
        self.body.set_text(sample_text());
        if let Some(file) = print_to_argument() {
            let mut settings = self.print_document1.printer_settings();
            settings.printer_name = "Microsoft Print to PDF".to_string();
            settings.print_to_file = true;
            settings.print_file_name = file;
            self.print_document1.set_printer_settings(settings);
            self.close_when_printed = true;
            // A document of the view prints when this handler returns (its PrintPage handler is a
            // method of this view).
            let _ = self.print_document1.print();
        }
        // `--preview`: open the print preview at once (a visual check of the dialog).
        if std::env::args().any(|a| a == "--preview") {
            self.print_preview_dialog1.show_dialog(self);
        }
    }

    fn print_document1_begin_print(&mut self, _sender: &Control, _e: &mut PrintEventArgs) {
        let text = self.body.get_text();
        self.pager.begin(&text);
    }

    fn print_document1_print_page(&mut self, _sender: &Control, e: &mut PrintPageEventArgs) {
        e.has_more_pages = self.pager.print_page("Kubuno printing sample", e);
    }

    fn print_document1_end_print(&mut self, _sender: &Control, e: &mut PrintEventArgs) {
        if e.print_action != PrintAction::PrintToPreview {
            self.status.set_text(format!("{} page(s) sent to the printer.", self.pager.page));
            kubuno_desktop::tracing::info!("printed {} page(s) ({:?})", self.pager.page, e.print_action);
            if self.close_when_printed {
                self.close();
            }
        }
    }

    fn preview_click(&mut self, _sender: &Button, _e: &MouseEventArgs) {
        self.print_preview_dialog1.show_dialog(self);
    }

    fn page_setup_click(&mut self, _sender: &Button, _e: &MouseEventArgs) {
        if self.page_setup_dialog1.show_dialog(self) == DialogResult::Ok {
            let page = self.print_document1.default_page_settings();
            self.status.set_text(format!("Page setup: {} {}.", page.paper().paper_name, if page.landscape { "landscape" } else { "portrait" }));
        }
    }

    fn print_click(&mut self, _sender: &Button, _e: &MouseEventArgs) {
        if self.print_dialog1.show_dialog(self) == DialogResult::Ok {
            let _ = self.print_document1.print();
        }
    }

    /// The same text, printed by a document and a preview dialog created in code (the Windows Forms
    /// way: `new PrintDocument()`, `PrintPage += …`, `new PrintPreviewDialog { Document = doc }`).
    fn code_preview_click(&mut self, _sender: &Button, _e: &MouseEventArgs) {
        let doc = PrintDocument::new().document_name("Built in code");
        let pager = Rc::new(RefCell::new(Pager::default()));
        let text = self.body.get_text();
        let p = pager.clone();
        doc.on_begin_print(move |_, _| p.borrow_mut().begin(&text));
        doc.on_print_page(move |_, e| {
            let more = pager.borrow_mut().print_page("Built in code", e);
            e.has_more_pages = more;
        });
        PrintPreviewDialog::new().document(&doc).show_dialog(self);
    }
}
