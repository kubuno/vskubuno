//! $safeprojectname$ - a Kubuno desktop application: a native window (no web view) that renders
//! `main_view.kbview` through `kubuno_views`' runtime, the same way the Kubuno apps in
//! `desktop/windows` do. Edit `main_view.kbview` (the view) and `main_view.rs` (its code-behind)
//! to build your UI; both hot-reload on save while this is running (`FileWatcher`).
//!
//! Like a Windows Forms application (`OutputType=WinExe`), it opens no console window, not even in
//! Debug: log with `tracing` (`tracing::info!`, `tracing::debug!`...) - under the debugger (F5) the lines
//! appear in Visual Studio's Output window, otherwise in `%LOCALAPPDATA%\Kubuno\logs\<exe>.log`; a panic
//! shows an error dialog. To get a console back, change the line below to
//! `#![windows_subsystem = "console"]`.
#![windows_subsystem = "windows"]

mod main_view;

use kubuno_controls::host::{self, Chrome, Frame, HostOptions};
use kubuno_ui::{Rect, Theme};
use kubuno_views::runtime::{FileWatcher, Runtime};

use main_view::MainViewModel;

/// `main_view.kbview` sits next to this file - resolved from `CARGO_MANIFEST_DIR` (a compile-time
/// constant) so the path is correct regardless of the process's current working directory,
/// whether launched via `cargo run`, a `.rsproj` F5, or the built .exe directly.
const VIEW_PATH: &str = concat!(env!("CARGO_MANIFEST_DIR"), "/src/main_view.kbview");

fn main() -> std::process::ExitCode {
    let mut watcher = FileWatcher::new(VIEW_PATH);
    let mut runtime = Runtime::new();
    watcher.poll(&mut runtime); // Load once before the window even opens.

    let mut view_model = MainViewModel::default();

    // Like a Windows Forms form, the window opens with the view's designed size as its client area
    // (the root's DesignWidth x DesignHeight in main_view.kbview), and the view fills the whole client
    // area: when the window is resized, each control follows its Anchor.
    let (width, height) = runtime.design_size().unwrap_or((800.0, 450.0));
    let mut options = HostOptions::new("$safeprojectname$", width.round() as u32, height.round() as u32, Theme::light());
    options.chrome = Chrome::Kubuno;
    options.client_size = true;
    options.fit_work_area = true;

    let result = host::run_with_options(
        options,
        move |canvas, frame: &Frame| {
            host::request_repaint_after(250);
            watcher.poll(&mut runtime);

            let body = Rect::new(0.0, frame.chrome_top, frame.size.0, frame.size.1.max(frame.chrome_top));

            if runtime.has_view() {
                // Runs the `#[kubuno_views::event_handlers]` methods of `MainViewModel` the view names.
                let events = runtime.frame_typed(canvas, frame, &mut view_model, body);
                for event in &events {
                    tracing::debug!("event: {event:?}");
                }
            } else {
                let theme = canvas.theme();
                let format = &canvas.formats().body;
                let text = Rect::new(body.left + 24.0, body.top + 24.0, body.right, body.bottom);
                canvas.text("Waiting for main_view.kbview to compile...", &text, format, &theme.text_secondary, false);
            }
        },
    );

    match result {
        Ok(()) => std::process::ExitCode::SUCCESS,
        Err(error) => {
            tracing::error!("$safeprojectname$: {error}");
            std::process::ExitCode::FAILURE
        }
    }
}
