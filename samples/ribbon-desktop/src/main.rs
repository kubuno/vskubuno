//! ribbon-desktop - the Kubuno ribbon sample (docs/RIBBON.md): `main_view.kbview` is the window,
//! `main_view.rs` its code.
#![windows_subsystem = "windows"]

mod main_view;

fn main() -> kubuno_desktop::Result {
    if std::env::args().any(|a| a == "--dark") {
        kubuno_desktop::Application::set_theme(kubuno_desktop::ui::Theme::dark());
    }
    kubuno_desktop::Application::run(main_view::MainView::new())
}
