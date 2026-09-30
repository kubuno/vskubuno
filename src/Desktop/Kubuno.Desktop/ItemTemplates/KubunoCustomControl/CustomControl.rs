//! `$classname$`: a Kubuno custom control, drawn by its own `on_paint` (WinForms: a class deriving from
//! `Control` that overrides `OnPaintBackground` and `OnPaint`). Use it in a view as `<$classname$ Text="..."/>`;
//! after a build it appears in the Toolbox's project tab and the designer draws it with this code.
//!
//! `e.graphics` is a WinForms-like `Graphics`: lines, rectangles, rounded rectangles, ellipses, arcs and pies,
//! Bezier curves and paths, linear and radial gradients, pens with dash styles, text laid out and measured,
//! images, clipping, transforms and saved states. Its `#[property]` fields are the element's attributes (their
//! `#[category]`, `#[description]` and `#[default_value]` show in the Properties window), its `#[event]` fields
//! its events (raised with `self.raise_<field>(args)`). Override more of the `Control` behaviour with Ctrl+. >
//! "Substituer des membres...".

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
    /// Paints the background before `on_paint` (WinForms `OnPaintBackground`): the base behaviour paints the
    /// control's `BackColor` and `BackgroundImage`. Not called when the class sets the `OPAQUE` style.
    fn on_paint_background(&mut self, e: &mut PaintEventCx<'_>) {
        self.base_mut().on_paint_background(e);
    }

    /// Paints the control (WinForms `OnPaint`): `e.graphics` is the drawing surface, `e.bounds()` the control's
    /// rectangle, `e.state` its hover, pressed and focus state. The paint is kept and replayed until the control
    /// changes (`DoubleBuffered`): call `self.invalidate()` when something it draws changes.
    fn on_paint(&mut self, e: &mut PaintEventCx<'_>) {
        let g = e.graphics;
        let r = e.bounds();
        let theme = g.theme();
        g.set_smoothing_mode(SmoothingMode::AntiAlias);

        // A vertical gradient face with rounded corners, darker when pressed.
        let top = Color::from(if e.state.pressed { theme.accent_hover } else { theme.accent });
        let face = LinearGradientBrush::from_rect(r, top, top.lerp(Color::BLACK, 0.25), LinearGradientMode::Vertical);
        g.fill_rounded_rectangle(face, r, self.corner_radius);

        // A light outline inside the edge, dotted while the control has the focus.
        let mut outline = Pen::new(Color::WHITE.with_alpha(0.6), 1.0).with_alignment(PenAlignment::Inset);
        if e.state.focused {
            outline = outline.with_dash(DashStyle::Dot);
        }
        g.draw_rounded_rectangle(&outline, r, self.corner_radius);

        // The text, centred both ways, with an ellipsis when it does not fit.
        let format = StringFormat::centered().with_trimming(StringTrimming::EllipsisCharacter);
        g.draw_string(&self.text, &Font::role(FontRole::BodyStrong), Color::from(theme.accent_foreground), r, &format);

        // The base behaviour raises `Paint` (WinForms `base.OnPaint(e)`).
        self.base_mut().on_paint(e);
    }

    // Rendering for printing or an off-screen capture (`draw_to_bitmap`), WinForms `OnPrint`: the default paints
    // the background then `on_paint`. Override it to draw differently on paper:
    //
    // fn on_print(&mut self, e: &mut PaintEventCx<'_>) {
    //     self.paint_layers(e);
    // }

    /// The size the control takes when a layout sizes it itself.
    fn get_preferred_size(&self, _canvas: &dyn Canvas, _proposed: Size) -> Size {
        Size { width: 120.0, height: 36.0 }
    }
}
