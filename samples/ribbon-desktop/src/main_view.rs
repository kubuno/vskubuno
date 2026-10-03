//! The ribbon sample's window (`main_view.kbview`): the ribbon's commands and elements handled in
//! code — commands bound to the view's state (`Bold`, `Italic`), typed fields of the ribbon's
//! elements (`self.bold`, `self.ctx_table`), their events, and a ribbon element added from code.

use kubuno_desktop::prelude::*;

#[kubuno_desktop::view("main_view.kbview")]
pub struct MainView {
    #[bind]
    bold: bool,
    #[bind]
    italic: bool,
    #[bind]
    underline: bool,
    #[bind]
    show_marks: bool,
    #[bind]
    font_family: String,
    #[bind]
    font_size: String,
    #[bind]
    font_color: String,
    #[bind]
    style: String,
    #[bind]
    table_selected: bool,
    #[bind]
    status: String,
    #[bind]
    state_text: String,
}

impl MainView {
    pub fn new() -> Self {
        let mut view = Self {
            font_family: "Calibri".to_string(),
            font_size: "11".to_string(),
            font_color: "#C00000".to_string(),
            style: "normal".to_string(),
            status: "Prêt".to_string(),
            ..Self::default()
        };
        view.initialize_component();
        view
    }

    fn refresh(&mut self) {
        self.state_text = format!(
            "{} {} pt · gras {} · italique {} · souligné {} · couleur {} · style {}",
            self.font_family,
            self.font_size,
            if self.bold { "oui" } else { "non" },
            if self.italic { "oui" } else { "non" },
            if self.underline { "oui" } else { "non" },
            if self.font_color.is_empty() { "automatique" } else { &self.font_color },
            self.style
        );
    }

    fn main_view_load(&mut self) {
        // A ribbon element made in code, added to a group of the view.
        let clear = RibbonButton::new().label("Effacer la mise en forme").small_icon("Eraser");
        self.grp_clip.controls().add(&clear);
        self.refresh();
    }

    fn paste_execute(&mut self) {
        self.status = "Coller (commande cmd_paste)".to_string();
    }

    fn bold_execute(&mut self) {
        self.status = format!("Gras : {}", if self.bold { "activé" } else { "désactivé" });
        self.refresh();
    }

    fn save_execute(&mut self) {
        self.status = "Enregistrer (Ctrl+S)".to_string();
    }

    fn cut_click(&mut self) {
        self.status = "Couper".to_string();
    }

    fn copy_click(&mut self) {
        self.status = "Copier".to_string();
    }

    fn font_dialog(&mut self) {
        self.status = "Boîte de dialogue Police (lanceur du groupe)".to_string();
    }

    fn case_click(&mut self, sender: &RibbonMenuItem) {
        self.status = format!("Casse : {}", sender.get_label());
    }

    fn font_color_changed(&mut self, e: &TextChangedEventArgs) {
        self.status = format!("Couleur de police : {}", if e.new.is_empty() { "automatique" } else { &e.new });
        self.refresh();
    }

    fn align_changed(&mut self, sender: &RibbonRadioButton, e: &CheckedChangedEventArgs) {
        if e.new {
            self.status = format!("Alignement : {}", sender.get_label());
        }
    }

    fn bullets_click(&mut self) {
        self.status = "Puces".to_string();
    }

    fn style_pick(&mut self, e: &TextChangedEventArgs) {
        self.status = format!("Style : {}", e.new);
        self.refresh();
    }

    fn insert_table_click(&mut self) {
        // The contextual tab group appears with its tabs (its Visible is bound).
        self.table_selected = !self.table_selected;
        self.status = if self.table_selected { "Tableau sélectionné : onglet « Disposition »".to_string() } else { "Tableau désélectionné".to_string() };
    }

    fn ribbon_tab_changed(&mut self, e: &TextChangedEventArgs) {
        self.status = format!("Onglet : {}", e.new);
        self.refresh();
    }

    fn close_click(&mut self) {
        self.status = "Fermer (Backstage)".to_string();
    }
}
