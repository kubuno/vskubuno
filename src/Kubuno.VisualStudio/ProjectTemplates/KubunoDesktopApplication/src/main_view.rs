//! Code-behind for `main_view.kbview`: the view model it binds to and its event handlers.
//!
//! Kept as a separate, same-stem file next to the view on purpose (`main_view.kbview` /
//! `main_view.rs`) - the WPF `MainWindow.xaml` / `MainWindow.xaml.cs` convention, so Solution
//! Explorer can nest one under the other once that grouping ships.

use kubuno_views::binding::{HandlerTable, Value, ViewModel};
use kubuno_views::handlers;

/// The starter view's state. Add your own fields as you add bindings to `main_view.kbview`.
pub struct MainViewModel {
    pub status: String,
}

impl Default for MainViewModel {
    fn default() -> Self {
        Self { status: "Ready.".to_string() }
    }
}

impl ViewModel for MainViewModel {
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

/// Handlers referenced by name from `main_view.kbview` (e.g. `OnClick="say_hello_clicked"`).
pub fn handler_table() -> HandlerTable {
    handlers! {
        "say_hello_clicked" => |vm, _v| {
            vm.set("Status", Value::Str("Hello from $safeprojectname$!".to_string()));
        },
    }
}
