using System;

namespace Kubuno.Rust.Cargo.Win32
{
    /// <summary>The data of an RT_VERSION resource (Explorer > Properties > Details).</summary>
    public sealed class Win32VersionInfo
    {
        /// <summary>File version text; its numeric part also fills VS_FIXEDFILEINFO.</summary>
        public string? FileVersion { get; set; }

        /// <summary>Product version text; its numeric part also fills VS_FIXEDFILEINFO.</summary>
        public string? ProductVersion { get; set; }

        /// <summary>The CompanyName string.</summary>
        public string? CompanyName { get; set; }

        /// <summary>The FileDescription string.</summary>
        public string? FileDescription { get; set; }

        /// <summary>The InternalName string.</summary>
        public string? InternalName { get; set; }

        /// <summary>The LegalCopyright string.</summary>
        public string? LegalCopyright { get; set; }

        /// <summary>The LegalTrademarks string.</summary>
        public string? LegalTrademarks { get; set; }

        /// <summary>The OriginalFilename string.</summary>
        public string? OriginalFilename { get; set; }

        /// <summary>The ProductName string.</summary>
        public string? ProductName { get; set; }

        /// <summary>The Comments string.</summary>
        public string? Comments { get; set; }

        /// <summary>True when the file is a DLL (VFT_DLL) rather than an application (VFT_APP).</summary>
        public bool IsDll { get; set; }

        /// <summary>
        /// Parses a version text into a four-part <see cref="Version"/>. Accepts "1.2", "1.2.3",
        /// "1.2.3.4" and semver with pre-release/build suffixes ("1.2.3-beta.1+abc" gives 1.2.3.0).
        /// Empty text gives 0.0.0.0; missing or non-numeric parts are 0 and each part is clamped to ushort.
        /// </summary>
        public static Version ParseVersion(string? text)
        {
            int[] parts = new int[4];
            if (text != null)
            {
                string s = text.Trim();
                int cut = s.IndexOfAny(new[] { '-', '+' });
                if (cut >= 0)
                {
                    s = s.Substring(0, cut);
                }

                string[] pieces = s.Split('.');
                for (int i = 0; i < 4 && i < pieces.Length; i++)
                {
                    parts[i] = ParsePart(pieces[i]);
                }
            }

            return new Version(parts[0], parts[1], parts[2], parts[3]);
        }

        private static int ParsePart(string piece)
        {
            piece = piece.Trim();
            long value = 0;
            bool any = false;
            foreach (char c in piece)
            {
                if (c < '0' || c > '9')
                {
                    break;
                }

                any = true;
                value = value * 10 + (c - '0');
                if (value > ushort.MaxValue)
                {
                    return ushort.MaxValue;
                }
            }

            return any ? (int)value : 0;
        }
    }
}
