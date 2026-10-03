//! `$classname$`: a `$baseclass$` whose behaviour it overrides (WinForms: a class deriving from `$baseclass$`).
//! It keeps every property and event of `<$baseclass$>`: use it in a view as `<$classname$ .../>` with the same
//! attributes. Override more members with Ctrl+. > "Substituer des membres...".

use kubuno_desktop_views::prelude::*;

/// A `$baseclass$` with its own behaviour. Describe it here: this text is its description in the Toolbox.
#[derive(Component, Default)]
#[kubuno(extends = $baseclass$, overrides(Control))]
pub struct $classname$ {
    base: $baseclass$,
    /// How many times it was clicked.
    #[property]
    #[category("Behavior")]
    #[browsable(false)]
    pub clicks: u32,
}

impl Control for $classname$ {
    /// Raises `Click` (the base behaviour), then counts the click.
    fn on_click(&mut self, e: &mut EventCx<'_, MouseEventArgs>) {
        self.base_mut().on_click(e);
        self.clicks += 1;
    }
}
