//! Code-behind for `main_view.kbview`: the view model it binds to and its event handlers.
//!
//! Kept as a separate, same-stem file next to the view on purpose (`main_view.kbview` /
//! `main_view.rs`) - the WPF `MainWindow.xaml` / `MainWindow.xaml.cs` convention, so Solution
//! Explorer can nest one under the other once that grouping ships.
//!
//! Event handlers are methods of the view model, in the `#[kubuno_views::event_handlers]` impl
//! below, like the handlers of a Windows Forms form: `OnClick="on_hello_click"` in the view runs
//! `on_hello_click` with the button as `sender` and the click's `MouseEventArgs` as `e`. Double-click
//! a control in the designer (or an event in the Properties window's Events tab) to add one.

use kubuno_views::prelude::*;

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

/// The handlers the `On*` attributes of `main_view.kbview` name.
#[kubuno_views::event_handlers]
impl MainViewModel {
    fn on_hello_click(&mut self, sender: &Sender<Button>, e: &MouseEventArgs) {
        self.status = format!("Hello from $safeprojectname$! ({} was clicked.)", sender.text());
    }
}
