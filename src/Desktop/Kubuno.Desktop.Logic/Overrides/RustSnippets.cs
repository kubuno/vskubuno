using System.Collections.Generic;

namespace Kubuno.Desktop.Logic.Overrides
{
    /// <summary>A code snippet of the Kubuno control authoring (docs/EVENTS.md EVT-7b, "Snippets").</summary>
    public sealed class RustSnippet
    {
        public RustSnippet(string shortcut, string description, string descriptionFr, string[] lines, bool inImpl)
        {
            Shortcut = shortcut;
            Description = description;
            DescriptionFr = descriptionFr;
            Lines = lines;
            InImpl = inImpl;
        }

        /// <summary>What to type (<c>onpaint</c>).</summary>
        public string Shortcut { get; }

        public string Description { get; }

        public string DescriptionFr { get; }

        public IReadOnlyList<string> Lines { get; }

        /// <summary>Offered inside an <c>impl</c> block (a method), else inside a struct (a field).</summary>
        public bool InImpl { get; }

        /// <summary>The text, each line after the first indented by <paramref name="indent"/>.</summary>
        public string Text(string indent, string newline) => string.Join(newline + indent, Lines);
    }

    /// <summary>The snippets <c>onpaint</c>, <c>event</c>, <c>handler</c>, <c>prop</c> (offered by completion in Rust files).</summary>
    public static class RustSnippets
    {
        public static readonly IReadOnlyList<RustSnippet> All = new[]
        {
            new RustSnippet(
                "onpaint",
                "Override on_paint: draw the control with e.graphics, then raise Paint (base).",
                "Substituer on_paint : dessiner le contrôle avec e.graphics, puis déclencher Paint (base).",
                new[]
                {
                    "fn on_paint(&mut self, e: &mut PaintEventCx<'_>) {",
                    "    let g = e.graphics;",
                    "    let r = e.bounds();",
                    "    g.fill_rounded_rectangle(Color::from(g.theme().accent), r, 4.0);",
                    "    self.base_mut().on_paint(e);",
                    "}",
                },
                inImpl: true),
            new RustSnippet(
                "handler",
                "A typed event handler (#[event_handlers] impl).",
                "Un gestionnaire d'événement typé (impl #[event_handlers]).",
                new[]
                {
                    "fn on_element_click(&mut self, sender: &Sender<Button>, e: &MouseEventArgs) {",
                    "    // TODO: implement on_element_click",
                    "}",
                },
                inImpl: true),
            new RustSnippet(
                "event",
                "A control event: an Event<Args> field raised with raise_<field>(args).",
                "Un événement du contrôle : un champ Event<Args> déclenché par raise_<champ>(args).",
                new[]
                {
                    "/// Occurs when the value is committed.",
                    "#[event]",
                    "#[category(\"Action\")]",
                    "pub value_committed: Event<EmptyEventArgs>,",
                },
                inImpl: false),
            new RustSnippet(
                "prop",
                "A bindable control property with its design-time attributes.",
                "Une propriété de contrôle liable, avec ses attributs de conception.",
                new[]
                {
                    "/// The value shown by the control.",
                    "#[property(bindable)]",
                    "#[category(\"Appearance\")]",
                    "#[default_value(0.0)]",
                    "pub value: f32,",
                },
                inImpl: false),
        };
    }
}
