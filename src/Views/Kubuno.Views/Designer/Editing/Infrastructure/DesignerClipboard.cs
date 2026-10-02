using System;
using System.Runtime.InteropServices;
using System.Windows;
using Kubuno.Views.Logging;

namespace Kubuno.Views.Designer.Editing.Infrastructure
{
    /// <summary>
    /// The system clipboard as the designer uses it (docs/DESIGNER.md §12): a copied element goes under the
    /// private <see cref="DesignerFragment.ClipboardFormat"/> AND as plain text; a paste takes the private format
    /// first, else plain text that starts with an element (XML copied from an editor). WPF clipboard calls, on
    /// the UI (STA) thread; a clipboard held by another process is logged and treated as empty, never thrown.
    /// </summary>
    internal static class DesignerClipboard
    {
        public static bool Write(string fragment)
        {
            try
            {
                var data = new DataObject();
                data.SetData(DesignerFragment.ClipboardFormat, fragment);
                data.SetData(DataFormats.UnicodeText, fragment);
                Clipboard.SetDataObject(data, copy: true);
                return true;
            }
            catch (Exception ex) when (ex is COMException or ExternalException or InvalidOperationException)
            {
                KubunoViewsLogHost.Current.WriteException("[designer] writing to the clipboard failed", ex);
                return false;
            }
        }

        /// <summary>The fragment to paste, or null when the clipboard holds none.</summary>
        public static string? Read()
        {
            try
            {
                var data = Clipboard.GetDataObject();
                if (data is null)
                {
                    return null;
                }

                if (data.GetDataPresent(DesignerFragment.ClipboardFormat) && data.GetData(DesignerFragment.ClipboardFormat) is string fragment)
                {
                    return fragment;
                }

                return data.GetDataPresent(DataFormats.UnicodeText) && data.GetData(DataFormats.UnicodeText) is string text &&
                    DesignerFragment.RootTag(text) is not null
                    ? text.Trim()
                    : null;
            }
            catch (Exception ex) when (ex is COMException or ExternalException or InvalidOperationException)
            {
                KubunoViewsLogHost.Current.WriteException("[designer] reading the clipboard failed", ex);
                return null;
            }
        }

        /// <summary>The tag of the element a paste would insert, or null.</summary>
        public static string? PeekTag() => DesignerFragment.RootTag(Read());
    }
}
