//! Code-behind of the user control `MessageRow` (`message_row.kbcontrol`), the item template of the
//! messages Repeater: each item of the list gets its own instance (its `likes` is per message).

use kubuno_desktop::views::prelude::*;

/// A message: its author's avatar, the text and the time, and a like button.
#[derive(UserControl, Default)]
#[user_control(view = "message_row.kbcontrol", default_event = "Liked")]
pub struct MessageRow {
    base: UserControlCore,
    /// The author (also set from the row of the item: a property named like a row field).
    #[property(bindable)]
    #[category("Data")]
    pub author: String,
    /// How many times this message was liked.
    #[property(bindable)]
    pub likes: u32,
    /// Occurs when the like button is clicked.
    #[event]
    #[category("Action")]
    pub liked: Event<EmptyEventArgs>,
}

#[kubuno_desktop::views::event_handlers]
impl MessageRow {
    fn like_click(&mut self) {
        self.likes += 1;
        self.raise_liked(EmptyEventArgs);
    }
}
