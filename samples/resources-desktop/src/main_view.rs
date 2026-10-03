//! The resources sample's window (`main_view.kbview`). Its `{Res …}` properties refresh by
//! themselves when the culture changes; the status line, written from code, reads the typed
//! accessors of `crate::Resources` again.

use kubuno_desktop::prelude::*;

use crate::Resources;

#[kubuno_desktop::view("main_view.kbview")]
#[derive(Default)]
pub struct MainView {}

impl MainView {
    pub fn new() -> Self {
        let mut view = Self::default();
        view.initialize_component();
        view
    }

    fn main_view_load(&mut self, _sender: &Form, _e: &EventArgs) {
        self.show_status();
    }

    /// Switches the UI culture of the whole process: every `{Res …}` of every window follows.
    fn switch_culture_click(&mut self, _sender: &Button, _e: &MouseEventArgs) {
        let french = Resources::culture().starts_with("fr");
        Resources::set_culture(if french { "en-US" } else { "fr-FR" });
        self.show_status();
    }

    fn ok_click(&mut self, _sender: &Button, _e: &MouseEventArgs) {
        self.close();
    }

    fn show_status(&mut self) {
        let logo = Resources::logo();
        let sizes: Vec<String> = Resources::app_icon().sizes().iter().map(|(w, h)| format!("{w}×{h}")).collect();
        self.status.set_text(format!(
            "{} · logo {} {} bytes · icon {}",
            Resources::culture_status(),
            logo.format(),
            logo.bytes().len(),
            sizes.join(", ")
        ));
    }
}
