//! ribbon-desktop - the Kubuno ribbon sample (docs/RIBBON.md): `main_view.kbview` is the window,
//! `main_view.rs` its code.
#![windows_subsystem = "windows"]

mod main_view;

fn main() -> kubuno::Result {
    if std::env::args().any(|a| a == "--dark") {
        kubuno::Application::set_theme(kubuno::ui::Theme::dark());
    }
    kubuno::Application::run(main_view::MainView::new())
}
