//! The foundations sample's window (`main_view.kbview`): the pages follow the Sidebar, the lists are
//! 10 000 rows long, and the controls of the views are reached from code — typed handles, a custom
//! control of another crate (`Custom<ChipBar>`), the Repeater's current item, menus shown from code.

use std::sync::Arc;

use foundations_controls::ChipBar;
use kubuno_desktop::prelude::*;

/// The keys of the Sidebar's rows, in the order of the pages (the tabs).
const PAGES: [&str; 4] = ["lists", "cards", "people", "form"];

const AUTHORS: [&str; 6] = ["Alice Martin", "Bob Durand", "Chloé Petit", "David Moreau", "Emma Leroy", "Farid Haddad"];
const PRESENCES: [&str; 4] = ["Online", "Away", "Busy", "Offline"];

#[kubuno_desktop::view("main_view.kbview")]
pub struct MainView {
    /// The custom control of the `foundations-controls` crate, typed with its class.
    #[control]
    chips: Custom<ChipBar>,
    #[bind]
    page: String,
    #[bind]
    page_index: i32,
    #[bind]
    status: String,
    #[bind]
    count_text: String,
    /// 10 000 messages, kept as one shared list: the Repeater follows it by its stamp.
    #[bind]
    messages: Rows,
    #[bind]
    selected_message: i32,
    /// 10 000 files for the ListView.
    #[bind]
    files: Rows,
    #[bind]
    cards: Rows,
    #[bind("Chips")]
    chip_rows: Rows,
    #[bind]
    portrait: Shared<Vec<u8>>,
    #[bind]
    recent: Rows,
    #[bind]
    recent_files: Rows,
    #[bind]
    show_hidden: bool,
    #[bind]
    profile_open: bool,
    #[bind]
    menu_result: String,
    /// The page of the DataTable, two-way.
    #[bind]
    file_page: i32,
    opened_recent: u32,
}

fn message(i: usize) -> Row {
    Row::new()
        .with("Id", Value::F32(i as f32))
        .with("Author", Value::Str(AUTHORS[i % AUTHORS.len()].to_string()))
        .with("Presence", Value::Str(PRESENCES[i % PRESENCES.len()].to_string()))
        .with("Text", Value::Str(format!("Message n° {} — la liste ne construit que les éléments visibles.", i + 1)))
        .with("Time", Value::Str(format!("{:02}:{:02}", (9 + i / 60) % 24, i % 60)))
}

impl MainView {
    pub fn new() -> Self {
        let mut view = Self {
            messages: (0..10_000).map(message).collect(),
            files: (0..10_000)
                .map(|i| Row::new().with("Text", Value::Str(format!("fichier-{i:05}.txt"))).with("Size", Value::Str(format!("{} Ko", (i * 37) % 900 + 1))))
                .collect(),
            cards: (0..24)
                .map(|i| {
                    Row::new()
                        .with("Id", Value::F32(i as f32))
                        .with("Name", Value::Str(AUTHORS[i % AUTHORS.len()].to_string()))
                        .with("Role", Value::Str(if i % 3 == 0 { "Administrateur" } else { "Membre" }.to_string()))
                        .with("Presence", Value::Str(PRESENCES[i % PRESENCES.len()].to_string()))
                })
                .collect(),
            chip_rows: ["Design", "Rust", "Windows"].iter().map(|t| Row::new().with("Text", Value::Str((*t).to_string()))).collect(),
            portrait: Shared::from(Arc::new(include_bytes!("images/portrait.png").to_vec())),
            recent: ["rapport.docx", "budget.xlsx"].iter().map(|t| Row::new().with("Text", Value::Str((*t).to_string())).with("Icon", Value::Str("FileText".into()))).collect(),
            page: "lists".to_string(),
            selected_message: -1,
            status: "Prêt".to_string(),
            ..Self::default()
        };
        view.initialize_component();
        view
    }

    fn main_view_load(&mut self, _sender: &Form, _e: &EventArgs) {
        if let Some(n) = std::env::args().skip_while(|a| a != "--page").nth(1).and_then(|n| n.parse::<usize>().ok()) {
            self.show_page(n.min(PAGES.len() - 1));
        }
        self.count_text = format!("{} messages", self.messages.len());
        self.status = "Prêt".to_string();
    }

    fn show_page(&mut self, index: usize) {
        self.page_index = index as i32;
        self.page = PAGES[index].to_string();
    }

    fn nav_selection_changed(&mut self, e: &TextChangedEventArgs) {
        if let Some(i) = PAGES.iter().position(|p| *p == e.new) {
            self.page_index = i as i32;
        }
    }

    fn messages_item_click(&mut self, e: &ItemEventArgs) {
        // The Repeater's current item: its index, key and row.
        if let Some(item) = Repeater::current_item() {
            self.status = format!("Message {} de {} (clé {})", e.index + 1, item.row.text("Author"), item.key);
        }
    }

    fn branch_click(&mut self) {
        self.status = "Branche : main".to_string();
    }

    fn add_card_click(&mut self) {
        let i = self.cards.len();
        self.cards.push(Row::new().with("Id", Value::F32(i as f32)).with("Name", Value::Str(format!("Nouvelle carte {}", i + 1))).with("Role", Value::Str("Invité".into())).with("Presence", Value::Str("Online".into())));
        self.chip_rows.push(Row::new().with("Text", Value::Str(format!("#{}", i + 1))));
        // The custom control of the other crate, reached through its typed field.
        let shown = self.chips.with_ref(|c| c.texts().len()).unwrap_or(0);
        self.status = format!("{} cartes, {} étiquettes dans la ChipBar", self.cards.len(), shown + 1);
    }

    fn profile_button_click(&mut self) {
        self.profile_open = !self.profile_open;
    }

    fn profile_closed(&mut self) {
        self.menu_result = "Profil fermé".to_string();
    }

    fn sign_out_click(&mut self) {
        self.profile_open = false;
        self.menu_result = "Déconnexion demandée".to_string();
    }

    fn sort_changed(&mut self, sender: &Control, e: &CheckedChangedEventArgs) {
        if e.new {
            self.menu_result = format!("Tri : {}", sender.get_text());
        }
    }

    fn recent_opening(&mut self) {
        // Filled when the sub-menu opens (`OnDropDownOpening`).
        self.opened_recent += 1;
        self.recent_files = (1..=3).map(|i| Row::new().with("Text", Value::Str(format!("document-{i}.kbdoc"))).with("Key", Value::Str(format!("doc{i}")))).collect();
    }

    fn recent_clicked(&mut self, e: &TextChangedEventArgs) {
        self.menu_result = format!("Ouvrir : {}", e.new);
    }

    fn copy_click(&mut self) {
        self.menu_result = "Copié".to_string();
    }

    fn delete_click(&mut self) {
        if MessageBox::show_with("Supprimer l'élément ?", "Supprimer", MessageBoxButtons::YesNo, MessageBoxIcon::Danger) == DialogResult::Yes {
            self.menu_result = "Supprimé".to_string();
        }
    }

    fn file_page_changed(&mut self, e: &NumericValueChangedEventArgs) {
        self.status = format!("Page {} du tableau ({} dans le modèle)", e.new as i32 + 1, self.file_page + 1);
    }

    fn save_click(&mut self) {
        self.status = "Formulaire enregistré".to_string();
        // A menu opened from code, below a control of the view.
        self.save.show_context_menu("more_menu");
    }
}
