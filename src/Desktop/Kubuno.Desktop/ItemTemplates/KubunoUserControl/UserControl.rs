//! Code-behind of the user control `$classname$` (`$fileinputname$.kbcontrol`): its properties (the attributes of
//! `<$classname$ .../>` in the other views, and the bindings of its own view), its events, and the handlers of
//! its view. Like a Windows Forms UserControl, it is its own view model.

use kubuno_views::prelude::*;

/// A user control. Describe it here: this text is its description in the Toolbox and the Properties window.
#[derive(UserControl, Default)]
#[user_control(view = "$fileinputname$.kbcontrol", default_event = "ActionClicked")]
// Its Toolbox icon (WinForms' [ToolboxBitmap]): a glyph name, or an image next to this file:
// #[toolbox(bitmap = "$fileinputname$.png")]
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

/// The handlers the `On*` attributes of `$fileinputname$.kbcontrol` name.
#[kubuno_views::event_handlers]
impl $classname$ {
    /// `Load`: runs before the user control is first shown - in the designer of the views using it too, where
    /// `design_mode()` is true (show sample data there, never touch files, the network or a database).
    fn $modulename$_load(&mut self) {
        if self.design_mode() && self.caption.is_empty() {
            self.caption = "$classname$".to_string();
        }
    }

    fn on_action_click(&mut self, sender: &Sender<Button>, e: &MouseEventArgs) {
        // Re-raises the inner click as the user control's own event (WinForms: OnActionClicked(e)).
        self.raise_action_clicked(EmptyEventArgs);
    }
}
