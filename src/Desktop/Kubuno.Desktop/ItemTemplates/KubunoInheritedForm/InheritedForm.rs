//! The form `$classname$`, inheriting `$basetypename$` (`$baseview$`): Windows Forms' inherited form. Its view adds to
//! the base form's; `self.base` is the base form - its controls (those it makes Protected or Public) and its event
//! handlers, which keep running for the controls it declares.

/// A form inheriting `$basetypename$`.
#[kubuno::view("$fileinputname$.kbview")]
#[derive(Default)]
pub struct $classname$ {
    /// The base form (Windows Forms' base class).
    #[base]
    pub base: $basetype$,
}

impl $classname$ {
    #[allow(dead_code)]
    pub fn new() -> Self {
        let mut form = Self::default();
        form.initialize_component();
        form
    }
}
