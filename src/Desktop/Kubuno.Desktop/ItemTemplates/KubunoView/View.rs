//! The code of `$fileinputname$.kbview` (a form, like a Windows Forms `Form` class): the view is its
//! design, `#[kubuno_desktop::view]` gives this struct a field per `x:Name`d control. Open it from a handler
//! of another form with `$classname$::new().show()` (a window of its own) or
//! `$classname$::new().show_dialog(self)` (a modal dialog, which returns its `DialogResult`).

use kubuno_desktop::prelude::*;

#[kubuno_desktop::view("$fileinputname$.kbview")]
#[derive(Default)]
pub struct $classname$ {}

impl $classname$ {
    pub fn new() -> Self {
        let mut view = Self::default();
        view.initialize_component();
        view
    }

    fn $modulename$_load(&mut self, _sender: &Form, _e: &EventArgs) {
        // TODO: initialise the form here.
    }
}
