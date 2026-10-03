//! The user control `$classname$`, inheriting `$basetypename$` (`$baseview$`): a Windows Forms inherited user control.
//! It keeps the base's properties, events and handlers; add its own like in any user control.

use kubuno_desktop_views::prelude::*;

/// A user control inheriting `$basetypename$`. Describe it here: this text is its description in the Toolbox.
#[derive(UserControl, Default)]
#[kubuno(extends = $basetype$)]
#[user_control(view = "$fileinputname$.kbcontrol")]
pub struct $classname$ {
    base: $basetype$,
}
