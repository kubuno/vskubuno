//! The code of `$fileinputname$.kbview`, an MDI parent form: `new_document_click` opens a document
//! inside it (replace the plain `Form` with your own MDI child view, "Add > Kubuno MDI Child Form").

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
    }

    fn new_document_click(&mut self, _sender: &Button, _e: &MouseEventArgs) {
        let count = self.form().mdi_children().len() + 1;
        let document = Form::new().text(format!("Document {count}")).client_size(420.0, 260.0);
        document.set_mdi_parent(&*self);
        document.show();
    }

    fn cascade_click(&mut self, _sender: &Button, _e: &MouseEventArgs) {
        self.form().layout_mdi(MdiLayout::Cascade);
    }

    fn tile_click(&mut self, _sender: &Button, _e: &MouseEventArgs) {
        self.form().layout_mdi(MdiLayout::TileVertical);
    }

    fn $modulename$_mdi_child_activate(&mut self, _sender: &Form, _e: &EventArgs) {
        kubuno_desktop::tracing::info!("active document: {:?}", self.form().active_mdi_child().map(|f| f.get_text()));
    }
}
