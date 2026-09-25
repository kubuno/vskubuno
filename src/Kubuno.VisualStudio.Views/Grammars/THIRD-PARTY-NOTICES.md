# Third-party notices — Kubuno view (`.kbview`) TextMate grammar

This folder ships two files that are not original to this project, following the same
vendoring pattern already used for the Rust grammar in
`../../Kubuno.VisualStudio/Grammars/THIRD-PARTY-NOTICES.md`:

- `kbview.tmLanguage.json` — a generic XML TextMate grammar. Originally authored in
  [atom/language-xml](https://github.com/atom/language-xml) (MIT license, itself derived from
  the original [textmate/xml.tmbundle](https://github.com/textmate/xml.tmbundle) permissive
  license — both texts are kept verbatim in `LICENSE-atom-language-xml.txt`), then vendored by
  the Visual Studio Code team into [microsoft/vscode](https://github.com/microsoft/vscode)'s
  built-in `extensions/xml` extension (MIT license, see `LICENSE-vscode.txt`). Fetched from
  `microsoft/vscode`'s `main` branch, `extensions/xml/syntaxes/xml.tmLanguage.json`, on
  2026-09-25.
- `kbview-language-configuration.json` — brackets/comments/auto-closing-pairs/folding rules,
  copied unchanged from the same `microsoft/vscode` `extensions/xml/xml.language-configuration.json`
  (MIT license).

`.kbview` files (Kubuno's declarative view format, see `../../../docs/XML_VIEWS.md`) are XML with
one additional `x:` namespace prefix (`x:Name`, `x:Class`) and no DTD/processing-instruction
usage beyond what plain XML already needs — a generic, unmodified XML grammar colorizes them
correctly with no Kubuno-specific patterns required. Structural validation (unknown component/
property names, binding expression errors) is the language server's job (`kubuno-views-ls`,
diagnostics via LSP), not the grammar's.

Two fields were changed in `kbview.tmLanguage.json` relative to the upstream file — see the
`kubuno_note` field left next to them in the file itself for the same explanation, in place:

- `"fileTypes": ["kbview"]` was **added** (absent upstream, where VS Code wires the extension via
  `package.json`'s `contributes.grammars`; Visual Studio's classic `TextMate\Repositories` pkgdef
  mechanism instead expects each grammar file scanned from the registered folder to declare its
  own file types).
- `"scopeName"` was **changed** from `text.xml` to `text.xml.kbview`, and `"name"` from `XML` to
  `Kubuno View`, purely so this grammar registers under its own scope distinct from any other XML
  grammar Visual Studio (or another extension) may already provide, avoiding a scope collision.
  No internal rule references the top-level `scopeName` (verified: the string `text.xml` does not
  otherwise appear in the file), so this rename has no effect on any matching pattern.

No other content was changed in either file. Both upstream projects are MIT-licensed (with the
TextMate bundle's own even more permissive original license also included for
`language-xml`'s sake); their license texts are kept verbatim in this folder
(`LICENSE-vscode.txt`, `LICENSE-atom-language-xml.txt`) per their terms.
