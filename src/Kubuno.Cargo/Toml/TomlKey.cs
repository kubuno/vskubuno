using System;

namespace Kubuno.Cargo.Toml
{
    /// <summary>Formats key segments for writing into a TOML document.</summary>
    public static class TomlKey
    {
        /// <summary>The key as a bare key when possible (<c>[A-Za-z0-9_-]+</c>), otherwise as a quoted basic string.</summary>
        public static string Format(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (IsBare(key)) return key;
            return TomlValueFormatter.FormatString(key);
        }

        internal static bool IsBare(string key)
        {
            if (key.Length == 0) return false;
            foreach (var c in key)
            {
                bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (!ok) return false;
            }

            return true;
        }
    }
}
