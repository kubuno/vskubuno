//! $safeprojectname$ — a Kubuno desktop application: a native window (no web view) that renders
//! `main_view.kbview` through `kubuno_views`' runtime, the same way the Kubuno apps in
//! `desktop/windows` do. Edit `main_view.kbview` (the view) and `main_view.rs` (its code-behind)
//! to build your UI; both hot-reload on save while this is running (`FileWatcher`).

mod main_view;

use kubuno_controls::host::{self, Chrome, Frame};
use kubuno_ui::{Rect, Theme};
use kubuno_views::node::ViewEventKind;
use kubuno_views::runtime::{FileWatcher, Runtime};

use main_view::{handler_table, MainViewModel};

/// `main_view.kbview` sits next to this file - resolved from `CARGO_MANIFEST_DIR` (a compile-time
/// constant) so the path is correct regardless of the process's current working directory,
/// whether launched via `cargo run`, a `.rsproj` F5, or the built .exe directly.
const VIEW_PATH: &str = concat!(env!("CARGO_MANIFEST_DIR"), "/src/main_view.kbview");

fn main() -> std::process::ExitCode {
    let mut watcher = FileWatcher::new(VIEW_PATH);
    let mut runtime = Runtime::new();
    watcher.poll(&mut runtime); // Load once before the window even opens.

    let mut view_model = MainViewModel::default();
    let mut handlers = handler_table();

    let result = host::run_with_chrome(
        "$safeprojectname$",
        900,
        700,
        Theme::light(),
        Chrome::Kubuno,
        move |canvas, frame: &Frame| {
            host::request_repaint_after(250);
            watcher.poll(&mut runtime);

            let margin = 24.0_f32;
            let top = frame.chrome_top + margin;
            let body = Rect::new(margin, top, (frame.size.0 - margin).max(margin), (frame.size.1 - margin).max(top));

            if runtime.has_view() {
                let events = runtime.frame(canvas, frame, &mut view_model, &mut handlers, body);
                for event in &events {
                    if matches!(event.kind, ViewEventKind::Clicked | ViewEventKind::Toggled(_) | ViewEventKind::Changed(_)) {
                        eprintln!("[$safeprojectname$] event: {event:?}");
                    }
                }
            } else {
                let theme = canvas.theme();
                let format = &canvas.formats().body;
                canvas.text("Waiting for main_view.kbview to compile…", &body, format, &theme.text_secondary, false);
            }
        },
    );

    match result {
        Ok(()) => std::process::ExitCode::SUCCESS,
        Err(error) => {
            eprintln!("$safeprojectname$: {error}");
            std::process::ExitCode::FAILURE
        }
    }
}
