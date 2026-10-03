//! printing-desktop - the Kubuno printing sample. `main_view.kbview` is the window (a text, the
//! printing buttons and, in the component tray, the PrintDocument and the three dialogs),
//! `main_view.rs` its code.
#![windows_subsystem = "windows"]

mod main_view;

fn main() -> kubuno_desktop::Result {
    // `--dark`: the dark theme (the print preview dialog follows the application's theme).
    if std::env::args().any(|a| a == "--dark") {
        kubuno_desktop::Application::set_theme(kubuno_desktop::ui::Theme::dark());
    }
    kubuno_desktop::Application::run(main_view::MainView::new())
}
