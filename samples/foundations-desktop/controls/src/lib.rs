//! The custom controls of the foundations sample, in a crate of their own: the application that
//! depends on this crate names them in its views (`<ChipBar Items="{Binding Tags}"/>`), types them
//! in its code (`#[control] chips: Custom<ChipBar>`) and finds them in the designer's Toolbox.

use kubuno_desktop::prelude::*;
use kubuno_desktop::views::component::{Control, ControlCore, PaintEventCx};
use kubuno_desktop::ui::{Canvas, Rect, Size};

/// A row of chips, one per row of `Items` (its `Text` field). Written from the `Control` level: it
/// paints itself.
#[derive(kubuno_desktop::views::component::Component, Default)]
#[kubuno(extends = Control, overrides(Control))]
#[category("Foundations")]
#[toolbox(icon = "tags")]
pub struct ChipBar {
    base: ControlCore,
    /// The chips: a list whose rows have a `Text` field.
    #[property]
    pub items: Rows,
    /// Shows the chips in the accent colour.
    #[property]
    #[category("Appearance")]
    pub accent: bool,
    /// How many chips code added with `add` (read back through `Custom::with`).
    pub added: u32,
}

impl ChipBar {
    /// Adds a chip (from code: `self.chips.with(|c| c.add("new"))`).
    pub fn add(&mut self, text: &str) {
        self.items.push(Row::new().with("Text", Value::Str(text.to_string())));
        self.added += 1;
        self.invalidate();
    }

    /// The chips' texts.
    pub fn texts(&self) -> Vec<String> {
        self.items.iter().map(|r| r.text("Text")).collect()
    }
}

impl Control for ChipBar {
    fn get_preferred_size(&self, _canvas: &dyn Canvas, _proposed: Size) -> Size {
        Size { width: 360.0, height: 32.0 }
    }

    fn on_paint(&mut self, e: &mut PaintEventCx<'_>) {
        let bounds = e.clip_rectangle;
        let g = e.graphics;
        let theme = g.theme();
        let (fill, ink) = if self.accent { (theme.accent_light, theme.accent) } else { (theme.surface_2, theme.text_primary) };
        let format = &g.formats().caption_strong;
        let mut x = bounds.left;
        for text in self.texts() {
            let w = g.measure(&text, format) + 24.0;
            if x + w > bounds.right {
                break;
            }
            let chip = Rect::new(x, bounds.top + 4.0, x + w, bounds.bottom - 4.0);
            g.fill_rounded(&chip, (chip.bottom - chip.top) / 2.0, &fill);
            g.text_ellipsis_center(&text, &chip, format, &ink);
            x += w + 8.0;
        }
        e.raise(self, "OnPaint");
    }
}
