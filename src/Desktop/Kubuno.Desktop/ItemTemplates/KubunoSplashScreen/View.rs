//! The code of `$fileinputname$.kbview`, the application's splash screen.
//!
//! Show it as the first line of `main`, before the heavy start-up, then report the steps and let
//! it fade out into the main window:
//!
//! ```ignore
//! let splash = $classname$::show_at_startup();
//! splash.step("Chargement des réglages…", 0.3);
//! let main_form = MainView::new();
//! splash.close_when(main_form.form());
//! kubuno::Application::run(main_form)
//! ```

use kubuno::prelude::*;

#[kubuno::view("$fileinputname$.kbview")]
#[derive(Default)]
pub struct $classname$ {}

impl $classname$ {
    pub fn new() -> Self {
        let mut view = Self::default();
        view.initialize_component();
        view
    }

    /// Shows this splash screen at once - on its own thread, so it paints while `main` goes on -
    /// set up from its design (the `<SplashArtwork>` of the `.kbview`), and returns the handle that
    /// reports the start-up steps (`splash.step("...", 0.5)`) and closes it (`splash.close_when(...)`;
    /// by default it fades out once the application shows its first window). `--no-splash` on the
    /// command line turns it off.
    pub fn show_at_startup() -> kubuno::Splash {
        kubuno::splash::from_kbview(include_str!("$fileinputname$.kbview"))
            .version(env!("CARGO_PKG_VERSION"))
            .license(env!("CARGO_PKG_LICENSE"))
            .show()
    }

    fn $modulename$_load(&mut self, _sender: &Form, _e: &EventArgs) {
        // Shown as a form (`$classname$::new().show()`), it closes itself after SplashDuration.
        self.artwork.set_property("Status", "Prêt.");
    }
}
