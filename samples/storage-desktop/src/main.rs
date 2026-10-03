//! storage-desktop - the Kubuno storage sample. `settings.kbsettings` declares the settings (the typed class
//! `Settings` below), `main_view.kbview` is the window (controls bound to the settings, and in the component tray a
//! `Settings`, a `SecretStore` and a `RegistryKey`), `main_view.rs` its code.
#![windows_subsystem = "windows"]

mod main_view;

// The typed class of the project's settings (Windows Forms' `Properties.Settings`): `Settings::theme()`,
// `Settings::set_launch_count(…)`, `Settings::store()`. Generated at compile time from the file.
kubuno_desktop::settings!("settings.kbsettings");

fn main() -> kubuno_desktop::Result {
    kubuno_desktop::Application::set_theme(main_view::theme_of(&Settings::theme()));
    kubuno_desktop::Application::run(main_view::MainView::new())
}
