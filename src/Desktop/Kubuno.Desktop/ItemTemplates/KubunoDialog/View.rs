//! The code of `$fileinputname$.kbview`, a modal dialog. Open it from a handler of another form:
//! `let result = $classname$::new().show_dialog(self);` - it returns how it was closed.

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

    fn $modulename$_load(&mut self, _sender: &Form, _e: &EventArgs) {
        // TODO: initialise the window here.
    }

    fn ok_click(&mut self, _sender: &Button, _e: &MouseEventArgs) {
        self.form().set_dialog_result(DialogResult::Ok);
    }

    fn cancel_click(&mut self, _sender: &Button, _e: &MouseEventArgs) {
        self.form().set_dialog_result(DialogResult::Cancel);
    }
}
