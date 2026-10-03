//! The code of `$fileinputname$.kbview`, an MDI document.

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
        // TODO: initialise the window here.
    }}
