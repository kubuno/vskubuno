//! resources-desktop - the Kubuno resources sample (vskubuno docs/RESOURCES.md). `resources.kbres`
//! becomes the typed `Resources` class below; `main_view.kbview` binds its entries with `{Res key}`.
#![windows_subsystem = "windows"]

mod main_view;

// `Resources::welcome_text()`, `Resources::logo()`, `Resources::app_icon()`… — culture-aware, with
// every file embedded in the executable (nothing is read from disk at run time).
kubuno::resources!("resources.kbres");

fn main() -> kubuno::Result {
    // `--culture fr`: start in that culture (default: Windows' display language, like .NET's
    // CurrentUICulture). `--dark`: the dark theme.
    let args: Vec<String> = std::env::args().collect();
    if let Some(culture) = args.iter().position(|a| a == "--culture").and_then(|i| args.get(i + 1)) {
        Resources::set_culture(culture);
    }
    if args.iter().any(|a| a == "--dark") {
        kubuno::Application::set_theme(kubuno::ui::Theme::dark());
    }
    kubuno::Application::run(main_view::MainView::new())
}
