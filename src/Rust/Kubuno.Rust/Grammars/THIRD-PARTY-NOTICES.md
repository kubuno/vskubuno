# Third-party notices — Rust TextMate grammar

This folder ships two files that are not original to this project:

- `rust.tmLanguage.json` — the Rust TextMate grammar. Originally authored in
  [dustypomerleau/rust-syntax](https://github.com/dustypomerleau/rust-syntax) (MIT license,
  see `LICENSE-rust-syntax.txt`), then converted to JSON and vendored by the Visual Studio Code
  team in [microsoft/vscode](https://github.com/microsoft/vscode)'s built-in `extensions/rust`
  extension (MIT license, see `LICENSE-vscode.txt`). This is the same grammar rust-analyzer's
  own VS Code extension relies on (it ships no grammar of its own and defers to VS Code's
  built-in Rust extension).
- `rust-language-configuration.json` — brackets/comments/indentation rules, copied from the same
  `microsoft/vscode` `extensions/rust/language-configuration.json` (MIT license). Two cosmetic
  changes were made to fit Visual Studio's stricter JSON parser: the trailing comma in
  `onEnterRules` and the JS-style comment above it were removed; no rule semantics changed.

One field was added to `rust.tmLanguage.json` that is not present upstream: `"fileTypes": ["rs"]`.
VS Code does not need this field because it wires the `.rs` extension to the grammar itself
(`extensions/rust/package.json`, `contributes.grammars`); Visual Studio's classic
`TextMate\Repositories` pkgdef mechanism instead expects each grammar file scanned from the
registered folder to declare its own file types (see `microsoft/VSSDK-Extensibility-Samples`'s
`TextmateGrammar` sample, `Dart.tmLanguage`). This is the only substantive change; see the
`kubuno_note` field left next to it in the file for the same explanation, in place.

Both upstream projects are MIT-licensed; their license texts are kept verbatim in this folder
(`LICENSE-rust-syntax.txt`, `LICENSE-vscode.txt`) per their terms.
