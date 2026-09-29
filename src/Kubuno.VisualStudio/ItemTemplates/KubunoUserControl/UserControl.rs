//! Code-behind of the user control `$classname$` (`$fileinputname$.kbview`): its properties (the attributes of
//! `<$classname$ .../>` in the other views, and the bindings of its own view), its events, and the handlers of
//! its view. Like a Windows Forms UserControl, it is its own view model.

use kubuno_views::prelude::*;

/// A user control. Describe it here: this text is its description in the Toolbox and the Properties window.
#[derive(UserControl, Default)]
#[user_control(view = "$fileinputname$.kbview", default_event = "ActionClicked")]
pub struct $classname$ {
    base: UserControlCore,
    /// The text shown on the left.
    #[property(bindable)]
    #[category("Appearance")]
    pub caption: String,
    /// Occurs when the button of the user control is clicked.
    #[event]
    #[category("Action")]
    pub action_clicked: Event<EmptyEventArgs>,
}

/// The handlers the `On*` attributes of `$fileinputname$.kbview` name.
#[kubuno_views::event_handlers]
impl $classname$ {
    fn on_action_click(&mut self, sender: &Sender<Button>, e: &MouseEventArgs) {
        // Re-raises the inner click as the user control's own event (WinForms: OnActionClicked(e)).
        self.raise_action_clicked(EmptyEventArgs);
    }
}
