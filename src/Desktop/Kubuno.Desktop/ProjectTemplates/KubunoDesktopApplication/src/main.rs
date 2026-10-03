//! $safeprojectname$ - a Kubuno desktop application. `main_view.kbview` is the main window (edit it in
//! the designer), `main_view.rs` its code (`MainView`, like a Windows Forms `Form1`).
#![windows_subsystem = "windows"]

mod main_view;

fn main() -> kubuno_desktop::Result {
    kubuno_desktop::Application::run(main_view::MainView::new())
}
