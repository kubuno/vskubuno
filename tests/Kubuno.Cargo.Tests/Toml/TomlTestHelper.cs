using Kubuno.Cargo.Toml;

namespace Kubuno.Cargo.Tests.Toml
{
    internal static class TomlTestHelper
    {
        /// <summary>Normalises a raw string literal to LF line endings and appends a final newline.</summary>
        public static string F(string raw) => raw.Replace("\r\n", "\n") + "\n";

        /// <summary>Normalises a raw string literal to LF line endings, without final newline.</summary>
        public static string NoEol(string raw) => raw.Replace("\r\n", "\n");

        public static string Crlf(string text) => text.Replace("\r\n", "\n").Replace("\n", "\r\n");

        public static TomlValue S(string v) => TomlValue.String(v);

        public static TomlValue I(long v) => TomlValue.Integer(v);

        public static TomlValue B(bool v) => TomlValue.Boolean(v);

        public static TomlValue Arr(params TomlValue[] v) => TomlValue.Array(v);

        public static TomlValue Tbl(params (string Key, TomlValue Value)[] entries)
        {
            var list = new List<KeyValuePair<string, TomlValue>>();
            foreach (var e in entries) list.Add(new KeyValuePair<string, TomlValue>(e.Key, e.Value));
            return TomlValue.Table(list);
        }

        /// <summary>Applies SetValue, checks that the edit reproduces the new text minimally, returns the new text.</summary>
        public static string Set(string text, TomlValue value, params string[] path)
        {
            var doc = TomlDocument.Parse(text);
            var edit = doc.SetValue(value, path);
            Assert.NotNull(edit);
            AssertEdit(text, edit!.Value, doc.Text);
            return doc.Text;
        }

        /// <summary>Applies Remove, checks the edit, returns the new text.</summary>
        public static string Remove(string text, params string[] path)
        {
            var doc = TomlDocument.Parse(text);
            var edit = doc.Remove(path);
            Assert.NotNull(edit);
            AssertEdit(text, edit!.Value, doc.Text);
            return doc.Text;
        }

        public static void AssertEdit(string oldText, TomlEdit edit, string newText)
        {
            Assert.True(edit.Start >= 0 && edit.OldLength >= 0 && edit.Start + edit.OldLength <= oldText.Length);
            Assert.Equal(newText, edit.Apply(oldText));
            Assert.Equal(newText, oldText.Substring(0, edit.Start) + edit.NewText + oldText.Substring(edit.Start + edit.OldLength));

            // Minimal: neither the first nor the last char of the replaced range equals the replacement's.
            if (edit.OldLength > 0 && edit.NewText.Length > 0)
            {
                Assert.NotEqual(oldText[edit.Start], edit.NewText[0]);
                Assert.NotEqual(oldText[edit.Start + edit.OldLength - 1], edit.NewText[edit.NewText.Length - 1]);
            }

            // The result must itself be a valid document.
            Assert.Equal(newText, TomlDocument.Parse(newText).Text);
        }
    }
}
