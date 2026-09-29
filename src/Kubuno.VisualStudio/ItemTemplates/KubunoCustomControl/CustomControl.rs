//! `$classname$`: a Kubuno custom control, drawn by its own `on_paint` (WinForms: a class deriving from
//! `Control` that overrides `OnPaint`). Use it in a view as `<$classname$ Text="..."/>`; after a build it
//! appears in the Toolbox's project tab and the designer draws it with this code.
//!
//! Its `#[property]` fields are the element's attributes (their `#[category]`, `#[description]` and
//! `#[default_value]` show in the Properties window), its `#[event]` fields its events (raised with
//! `self.raise_<field>(args)`). Override more of the `Control` behaviour with Ctrl+. > "Substituer des
//! membres...".

use kubuno_views::prelude::*;

/// A custom control. Describe it here: this text is its description in the Toolbox and the Properties window.
#[derive(Component, Default)]
#[kubuno(extends = Control, overrides(Control))]
#[toolbox(icon = "shapes")]
pub struct $classname$ {
    base: ControlCore,
    /// The text shown on the control.
    #[property]
    #[category("Appearance")]
    pub text: String,
    /// The radius of the corners, in pixels.
    #[property]
    #[category("Appearance")]
    #[default_value(8.0)]
    pub corner_radius: f32,
}

impl Control for $classname$ {
    /// Paints the control: `e.graphics` is the canvas, `e.clip_rectangle` the control's bounds, `e.state`
    /// its hover, pressed and focus state.
    fn on_paint(&mut self, e: &mut PaintEventCx<'_>) {
        let r = e.clip_rectangle;
        let theme = e.graphics.theme();
        let fill = if e.state.pressed { theme.accent_hover } else { theme.accent };
        e.graphics.fill_rounded(&r, self.corner_radius, &fill);
        e.graphics.text_ellipsis_center(&self.text, &r, &e.graphics.formats().body, &theme.accent_foreground);
        // The base behaviour raises `Paint` (WinForms `base.OnPaint(e)`).
        self.base_mut().on_paint(e);
    }

    /// The size the control takes when a layout sizes it itself.
    fn get_preferred_size(&self, _canvas: &dyn Canvas, _proposed: Size) -> Size {
        Size { width: 120.0, height: 36.0 }
    }
}
