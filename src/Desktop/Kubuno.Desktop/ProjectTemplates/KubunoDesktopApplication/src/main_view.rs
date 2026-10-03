//! The main window's code: `main_view.kbview` is its design (like `Form1.Designer.cs`, generated at
//! compile time by `#[kubuno_desktop::view]` - no file to maintain), this struct is `Form1`.

use kubuno_desktop::prelude::*;

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
        self.status.set_text("Ready.");
    }
}
