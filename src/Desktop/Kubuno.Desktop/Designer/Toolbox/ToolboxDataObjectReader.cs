using System;
using System.Runtime.InteropServices;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace Kubuno.Desktop.Designer.Toolbox
{
    /// <summary>
    /// Reads the <see cref="ToolboxItemFormat"/> payload out of an OLE data object - the Toolbox item's own
    /// data object (<c>IVsToolboxUser.IsSupported</c>/<c>ItemPicked</c>) or the one an OLE drag from the
    /// Toolbox carries onto the design surface. Uses only the BCL's <see cref="ComTypes.IDataObject"/>
    /// (IID 0000010e, the same COM interface as <c>Microsoft.VisualStudio.OLE.Interop.IDataObject</c>, so a
    /// COM object - or WinForms' <c>DataObject</c>, which <c>OleDataObject</c> derives from - can be cast to
    /// it), which keeps the design surface free of any Visual Studio interop assembly.
    /// </summary>
    public static class ToolboxDataObjectReader
    {
        private const int TymedHGlobal = 1;
        private const int DvAspectContent = 1;

        private static short s_format;

        /// <summary>The registered clipboard format id of <see cref="ToolboxItemFormat.FormatName"/>.</summary>
        public static short FormatId
        {
            get
            {
                if (s_format == 0)
                {
                    s_format = unchecked((short)RegisterClipboardFormat(ToolboxItemFormat.FormatName));
                }

                return s_format;
            }
        }

        /// <summary>Whether <paramref name="dataObject"/> offers a Kubuno component (cheap: <c>QueryGetData</c>, no data transfer).</summary>
        public static bool HasComponent(object? dataObject)
        {
            if (dataObject is not ComTypes.IDataObject data)
            {
                return false;
            }

            var format = CreateFormat();
            try
            {
                return data.QueryGetData(ref format) == 0;
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException)
            {
                return false;
            }
        }

        /// <summary>The component name <paramref name="dataObject"/> carries, if any. Never throws.</summary>
        public static bool TryGetComponent(object? dataObject, out string componentName)
        {
            componentName = string.Empty;
            if (dataObject is not ComTypes.IDataObject data)
            {
                return false;
            }

            var format = CreateFormat();
            ComTypes.STGMEDIUM medium;
            try
            {
                data.GetData(ref format, out medium);
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException or System.Runtime.Serialization.SerializationException)
            {
                return false;
            }

            try
            {
                if (medium.tymed != ComTypes.TYMED.TYMED_HGLOBAL || medium.unionmember == IntPtr.Zero)
                {
                    return false;
                }

                var size = (int)GlobalSize(medium.unionmember).ToUInt64();
                var pointer = GlobalLock(medium.unionmember);
                if (pointer == IntPtr.Zero || size <= 0)
                {
                    return false;
                }

                try
                {
                    var bytes = new byte[size];
                    Marshal.Copy(pointer, bytes, 0, size);
                    return ToolboxItemFormat.TryDecode(bytes, out componentName);
                }
                finally
                {
                    GlobalUnlock(medium.unionmember);
                }
            }
            finally
            {
                ReleaseStgMedium(ref medium);
            }
        }

        private static ComTypes.FORMATETC CreateFormat() => new ComTypes.FORMATETC
        {
            cfFormat = FormatId,
            dwAspect = (ComTypes.DVASPECT)DvAspectContent,
            lindex = -1,
            ptd = IntPtr.Zero,
            tymed = (ComTypes.TYMED)TymedHGlobal,
        };

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint RegisterClipboardFormat(string format);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalLock(IntPtr handle);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalUnlock(IntPtr handle);

        [DllImport("kernel32.dll")]
        private static extern UIntPtr GlobalSize(IntPtr handle);

        [DllImport("ole32.dll")]
        private static extern void ReleaseStgMedium(ref ComTypes.STGMEDIUM medium);
    }
}
