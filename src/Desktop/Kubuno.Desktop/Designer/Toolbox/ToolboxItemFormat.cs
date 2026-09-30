using System;
using System.Text;

namespace Kubuno.VisualStudio.Designer.Toolbox
{
    /// <summary>
    /// The private clipboard format a Kubuno component carries on Visual Studio's native Toolbox: its
    /// registry element name (<c>"Button"</c>) as UTF-8 bytes. Only the <c>.kbview</c> designer declares
    /// support for it (<c>IVsToolboxUser.IsSupported</c>), which is exactly what makes the Toolbox show the
    /// Kubuno tabs for a <c>.kbview</c> designer and hide them for every other document; the design
    /// surface's OLE drop target and <c>ItemPicked</c> read the name back with <see cref="TryDecode"/>.
    /// Pure (no Visual Studio type), so the drop target in <c>DesignSurface/</c> can use it without
    /// loading VS interop assemblies.
    /// </summary>
    public static class ToolboxItemFormat
    {
        /// <summary>Registered clipboard format name (<c>RegisterClipboardFormat</c>).</summary>
        public const string FormatName = "Kubuno.Views.ToolboxItem";

        public static byte[] Encode(string componentName)
        {
            if (string.IsNullOrEmpty(componentName))
            {
                throw new ArgumentException("A component name is required.", nameof(componentName));
            }

            return Encoding.UTF8.GetBytes(componentName);
        }

        /// <summary>
        /// Reads a component name back from raw clipboard bytes - tolerant of the trailing NULs an HGLOBAL
        /// rounded up to its allocation granularity may carry. False for empty or non-identifier content.
        /// </summary>
        public static bool TryDecode(byte[]? bytes, out string componentName)
        {
            componentName = string.Empty;
            if (bytes is null || bytes.Length == 0)
            {
                return false;
            }

            var text = Encoding.UTF8.GetString(bytes).TrimEnd('\0').Trim();
            if (text.Length == 0 || text.Length > 128)
            {
                return false;
            }

            foreach (var c in text)
            {
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' || c == ':'))
                {
                    return false;
                }
            }

            componentName = text;
            return true;
        }
    }
}
