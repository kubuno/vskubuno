//! foundations-desktop - the Kubuno desktop foundations sample (docs/DESKTOP-MIGRATION.md, lot F1).
//! `main_view.kbview` is the window, `main_view.rs` its code, `message_row.kbcontrol` the user control
//! each message of the Repeater is shown with.
#![windows_subsystem = "windows"]

mod main_view;
mod message_row;

fn main() -> kubuno_desktop::Result {
    if std::env::args().any(|a| a == "--dark") {
        kubuno_desktop::Application::set_theme(kubuno_desktop::ui::Theme::dark());
    }
    kubuno_desktop::Application::run(main_view::MainView::new())
}
