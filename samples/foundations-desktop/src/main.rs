//! foundations-desktop - the Kubuno desktop foundations sample (docs/DESKTOP-MIGRATION.md, lot F1).
//! `main_view.kbview` is the window, `main_view.rs` its code, `message_row.kbview` the user control
//! each message of the Repeater is shown with.
#![windows_subsystem = "windows"]

mod main_view;
mod message_row;

fn main() -> kubuno::Result {
    if std::env::args().any(|a| a == "--dark") {
        kubuno::Application::set_theme(kubuno::ui::Theme::dark());
    }
    kubuno::Application::run(main_view::MainView::new())
}
