//! `$classname$`: a Kubuno non-visual component (WinForms: a `Component` such as a `Timer`). A view declares it as
//! `<$classname$ x:Name="..."/>`; it paints nothing, and the designer lists it in its component tray. Its
//! `#[property]` fields are its attributes, its `#[event]` fields its events (`self.raise_<field>(args)` raises
//! one to the handler the view names).

use kubuno_desktop_views::prelude::*;

/// A non-visual component. Describe it here: this text is its description in the Toolbox.
#[derive(Component, Default)]
#[kubuno(extends = Component)]
#[toolbox(icon = "component")]
pub struct $classname$ {
    base: ComponentCore,
    /// Whether the component is active.
    #[property]
    #[category("Behavior")]
    #[default_value(false)]
    pub enabled: bool,
    /// Occurs when the component's state changes.
    #[event]
    #[category("Behavior")]
    pub state_changed: Event<EmptyEventArgs>,
}
