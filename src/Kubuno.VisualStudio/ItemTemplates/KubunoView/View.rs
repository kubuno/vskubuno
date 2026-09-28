//! Code-behind for `$fileinputname$.kbview`: the view model it binds to and its event handlers.
//! Kept as a same-stem file next to the view on purpose - see that file's own comment.

use kubuno_views::binding::{HandlerTable, Value, ViewModel};
use kubuno_views::handlers;

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

/// Handlers referenced by name from `$fileinputname$.kbview` (e.g. `OnClick="action_clicked"`).
pub fn handler_table() -> HandlerTable {
    handlers! {
        "action_clicked" => |vm, _v| {
            vm.set("Status", Value::Str("Done.".to_string()));
        },
    }
}
