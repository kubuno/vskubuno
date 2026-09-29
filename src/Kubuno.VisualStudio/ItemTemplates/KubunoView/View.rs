//! Code-behind for `$fileinputname$.kbview`: the view model it binds to and its event handlers.
//! Kept as a same-stem file next to the view on purpose - see that file's own comment.
//!
//! Event handlers are methods of the view model, in the `#[kubuno_views::event_handlers]` impl
//! below (`OnClick="on_action_click"` runs `on_action_click`). Paint the view with
//! `runtime.frame_typed(canvas, frame, &mut state, bounds)` so its events reach them.

use kubuno_views::prelude::*;

/// This view's state. Add fields for each `{Binding ...}` path used in `$fileinputname$.kbview`.
pub struct State {
    pub status: String,
}

impl Default for State {
    fn default() -> Self {
        Self { status: "Ready.".to_string() }
    }
}

impl ViewModel for State {
    fn get(&self, path: &str) -> Option<Value> {
        match path {
            "Status" => Some(Value::Str(self.status.clone())),
            _ => None,
        }
    }

    fn set(&mut self, path: &str, value: Value) {
        if let ("Status", Value::Str(s)) = (path, value) {
            self.status = s;
        }
    }
}

/// The handlers the `On*` attributes of `$fileinputname$.kbview` name.
#[kubuno_views::event_handlers]
impl State {
    fn on_action_click(&mut self, sender: &Sender<Button>, e: &MouseEventArgs) {
        self.status = "Done.".to_string();
    }
}
