//! The storage sample's window (`main_view.kbview`): settings bound through the `settings` component and read
//! through the typed class `crate::Settings`, a secret kept by `secrets`, a Registry value shown by `explorer`.

use kubuno::prelude::*;
use kubuno::storage::{Layer, SecretBackend};
use kubuno::ui::Theme;

use crate::Settings;

/// The palette of a `Theme` setting.
pub fn theme_of(name: &str) -> Theme {
    match name {
        "Light" => Theme::light(),
        "Dark" => Theme::dark(),
        _ => Theme::detect(),
    }
}

/// `--self-test <file>`: exercise the components and the typed class, write a report there, then close.
fn self_test_argument() -> Option<String> {
    let args: Vec<String> = std::env::args().collect();
    args.iter().position(|a| a == "--self-test").and_then(|i| args.get(i + 1)).cloned()
}

#[kubuno::view("main_view.kbview")]
#[derive(Default)]
pub struct MainView {}

impl MainView {
    pub fn new() -> Self {
        let mut view = Self::default();
        view.initialize_component();
        view
    }

    fn main_view_load(&mut self, _sender: &Form, _e: &EventArgs) {
        // A local setting (this machine only), through the typed class: saved at once.
        let launches = Settings::launch_count() + 1;
        Settings::set_launch_count(launches);
        self.launches.set_text(format!("Started {launches} time(s) on this machine; recent files: {}.", Settings::recent_files().join(", ")));
        self.status.set_text(format!("Settings: {}", Settings::store().location(Layer::UserRoaming)));
        if let Some(report) = self_test_argument() {
            let text = self.self_test();
            if let Err(e) = std::fs::write(&report, text) {
                kubuno::tracing::warn!("cannot write the self-test report: {e}");
            }
            self.close();
        }
    }

    /// Another instance (or this window's bindings) changed a setting.
    fn settings_setting_changed(&mut self, _sender: &Control, e: &SettingChangedEventArgs) {
        if e.setting_name == "Theme" {
            kubuno::Application::set_theme(theme_of(&Settings::theme()));
        }
        let origin = if e.external { "in another window" } else { "here" };
        self.status.set_text(format!("{} changed {origin}.", e.setting_name));
    }

    fn save_key_click(&mut self, _sender: &Button, _e: &MouseEventArgs) {
        let key = self.api_key.get_text();
        if key.trim().is_empty() {
            self.status.set_text("Type a key first.");
            return;
        }
        match self.secrets.set_str("ApiKey", key.trim()) {
            Ok(()) => self.status.set_text("The key is saved in the credential store."),
            // The error names the secret, never its value.
            Err(e) => self.status.set_text(format!("The key was not saved: {e}")),
        }
        self.api_key.set_text("");
    }

    fn forget_key_click(&mut self, _sender: &Button, _e: &MouseEventArgs) {
        match self.secrets.delete("ApiKey") {
            Ok(_) => self.status.set_text("The key is forgotten."),
            Err(e) => self.status.set_text(format!("The key was not removed: {e}")),
        }
    }

    /// What `--self-test` checks; one `name: value` line each.
    fn self_test(&mut self) -> String {
        let mut lines = Vec::new();
        let mut line = |k: &str, v: String| lines.push(format!("{k}: {v}"));
        line("app", Settings::app().to_string());
        line("roaming", Settings::store().location(Layer::UserRoaming));
        line("local", Settings::store().location(Layer::UserLocal));
        line("machine", Settings::store().location(Layer::Machine));
        line("theme", Settings::theme());
        line("update_channel", Settings::update_channel());
        line("interval", Settings::sync_interval_minutes().to_string());
        // The view's component and the typed class share the same values.
        let set = self.settings.set("ShowHidden", true).map(|_| ()).map_err(|e| e.to_string());
        line("view_set_show_hidden", format!("{set:?}"));
        line("typed_show_hidden", Settings::show_hidden().to_string());
        Settings::set_display_name("Self-test");
        line("view_display_name", format!("{:?}", self.settings.get_as::<String>("DisplayName")));
        line("refused_theme", format!("{}", self.settings.set("Theme", "Purple").is_err()));
        // Secrets: in memory for the test, never the real credential store.
        let secrets = self.secrets.clone().backend(SecretBackend::Memory);
        line("secret_set", format!("{:?}", secrets.set_str("ApiKey", "s3cr3t").map_err(|e| e.to_string())));
        line("secret_exists", format!("{:?}", secrets.contains("ApiKey").map_err(|e| e.to_string())));
        line("secret_deleted", format!("{:?}", secrets.delete("ApiKey").map_err(|e| e.to_string())));
        // The Registry key, read-only (in a sandbox: the sandbox's copy).
        line("registry_key", self.explorer.full_name());
        line("registry_hidden", format!("{:?}", self.explorer.get_string("Hidden").map_err(|e| e.to_string())));
        line("save", format!("{:?}", self.settings.save().map_err(|e| e.to_string())));
        lines.join("\n") + "\n"
    }
}
