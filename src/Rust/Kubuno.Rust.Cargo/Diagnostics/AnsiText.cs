using System.Text.RegularExpressions;

namespace Kubuno.Rust.Cargo.Diagnostics
{
    /// <summary>
    /// Strips ANSI/VT100 escape sequences (as produced by
    /// <c>--message-format=json-diagnostic-rendered-ansi</c>'s <c>rendered</c> field) so the
    /// text can be shown in a plain UI surface like the VS Error List or an output pane that
    /// doesn't interpret them.
    /// </summary>
    public static class AnsiText
    {
        // Matches CSI sequences (ESC '[' ... final byte) — SGR color/style codes, cursor moves, etc.
        // This covers everything rustc/cargo emit; OSC sequences are not used by either.
        private static readonly Regex CsiSequence = new Regex("\u001B\\[[0-9;]*[A-Za-z]", RegexOptions.Compiled);

        public static string Strip(string text) =>
            string.IsNullOrEmpty(text) ? text : CsiSequence.Replace(text, string.Empty);
    }
}
