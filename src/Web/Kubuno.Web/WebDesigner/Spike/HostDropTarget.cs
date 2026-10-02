using System;
using System.Runtime.InteropServices;
using System.Text;
using Kubuno.Web.Logic.WebDesigner;
using ComTypes = System.Runtime.InteropServices.ComTypes;
using OleInterop = Microsoft.VisualStudio.OLE.Interop;

namespace Kubuno.Web.WebDesigner.Spike
{
    /// <summary>
    /// SPIKE (docs/WEB-VIEWS.md, WV-9a, question 2, channel "host"): Visual Studio's own OLE drop target, on the overlay
    /// window <see cref="WebSurfaceView"/> raises over the browser once the page has detected a Toolbox drag (registered
    /// on the WebView2 control's own window it is never called, see <see cref="DropChannel.Host"/>). It reads the Toolbox item from the data object
    /// (the private <c>Kubuno.Views.ToolboxItem</c> format of the desktop Toolbox items, or the spike's
    /// <c>kubuno-toolbox:&lt;Name&gt;</c> text), and forwards the drag to the page as the desktop protocol's
    /// <c>dragEnter/dragOver/drop/dragLeave</c> in page CSS pixels; the page answers with <c>dropTargetChanged</c>, whose
    /// validity decides the drop effect (the "not allowed" cursor).
    /// </summary>
    internal sealed class HostDropTarget : OleInterop.IDropTarget
    {
        private const uint DropEffectNone = 0;
        private const uint DropEffectCopy = 1;

        private readonly Func<(double X, double Y)?> _toPage;
        private readonly Action<string> _post;
        private readonly Action<string> _dropped;
        private readonly Action _ended;
        private string? _component;
        private (double X, double Y)? _last;

        /// <param name="toPage">The cursor (read with <c>GetCursorPos</c> by the caller) in page CSS pixels, null when outside.</param>
        /// <param name="post">Sends a protocol message to the page.</param>
        /// <param name="dropped">Called after a drop with the component name (Toolbox bookkeeping).</param>
        /// <param name="ended">Called when the drag leaves or drops (the overlay is hidden again).</param>
        public HostDropTarget(Func<(double X, double Y)?> toPage, Action<string> post, Action<string> dropped, Action ended)
        {
            _ended = ended;
            _toPage = toPage;
            _post = post;
            _dropped = dropped;
        }

        /// <summary>The page's last answer (<c>dropTargetChanged</c>): whether a drop here is allowed.</summary>
        public bool TargetValid { get; set; } = true;

        public void DragEnter(OleInterop.IDataObject pDataObj, uint grfKeyState, OleInterop.POINTL pt, ref uint pdwEffect)
        {
            Kubuno.Shared.Logging.KubunoLog.WriteLine($"[web-spike] host drop target: DragEnter at {pt.x},{pt.y}");
            _component = ReadComponent(pDataObj);
            _last = null;
            Kubuno.Shared.Logging.KubunoLog.WriteLine($"[web-spike] host drop target: DragEnter component={_component ?? "(none)"} formats: {DescribeFormats(pDataObj)}");
            if (_component is null)
            {
                pdwEffect = DropEffectNone;
                return;
            }

            TargetValid = true;
            _post(WebSurfaceProtocol.EncodeDragEnter(_component));
            Over(ref pdwEffect);
        }

        public void DragOver(uint grfKeyState, OleInterop.POINTL pt, ref uint pdwEffect)
        {
            if (_component is null)
            {
                pdwEffect = DropEffectNone;
                return;
            }

            Over(ref pdwEffect);
        }

        public void DragLeave()
        {
            if (_component is not null)
            {
                _post(WebSurfaceProtocol.EncodeDragLeave());
            }

            _component = null;
            _ended();
        }

        public void Drop(OleInterop.IDataObject pDataObj, uint grfKeyState, OleInterop.POINTL pt, ref uint pdwEffect)
        {
            var component = _component ?? ReadComponent(pDataObj);
            _component = null;
            _ended();
            if (component is null || _toPage() is not { } point || !TargetValid)
            {
                pdwEffect = DropEffectNone;
                _post(WebSurfaceProtocol.EncodeDragLeave());
                return;
            }

            Kubuno.Shared.Logging.KubunoLog.WriteLine($"[web-spike] host drop target: Drop {component} at {point.X:0.#},{point.Y:0.#} CSS px");
            _post(WebSurfaceProtocol.EncodeDrop(point.X, point.Y));
            pdwEffect = DropEffectCopy;
            _dropped(component);
        }

        private void Over(ref uint effect)
        {
            if (_toPage() is not { } point)
            {
                effect = DropEffectNone;
                return;
            }

            if (_last is not { } last || Math.Abs(last.X - point.X) >= 0.5 || Math.Abs(last.Y - point.Y) >= 0.5)
            {
                _last = point;
                _post(WebSurfaceProtocol.EncodeDragOver(point.X, point.Y));
            }

            effect = TargetValid ? DropEffectCopy : DropEffectNone;
        }

        /// <summary>The Toolbox component a data object carries: the private format first, else the spike's text. Never throws.</summary>
        internal static string? ReadComponent(object? dataObject)
        {
            if (dataObject is not ComTypes.IDataObject data)
            {
                return null;
            }

            var bytes = ReadHGlobal(data, unchecked((short)RegisterClipboardFormat(WebDesignSpikeConstants.ToolboxItemFormat)));
            if (bytes is not null)
            {
                var name = Encoding.UTF8.GetString(bytes).TrimEnd('\0').Trim();
                if (SpikeElementCatalog.IsElementName(name))
                {
                    return name;
                }
            }

            var text = ReadHGlobal(data, CfUnicodeText);
            return text is null ? null : SpikeElementCatalog.ParseToolboxText(Encoding.Unicode.GetString(text).TrimEnd('\0'));
        }

        /// <summary>Which formats a data object offers (diagnostics: what the Toolbox's drag really carries).</summary>
        internal static string DescribeFormats(object? dataObject)
        {
            if (dataObject is not ComTypes.IDataObject data)
            {
                return "(not an IDataObject)";
            }

            var builder = new StringBuilder();
            try
            {
                var formats = data.EnumFormatEtc(ComTypes.DATADIR.DATADIR_GET);
                var one = new ComTypes.FORMATETC[1];
                var fetched = new int[1];
                while (formats.Next(1, one, fetched) == 0 && fetched[0] == 1)
                {
                    var name = new StringBuilder(128);
                    var id = (ushort)one[0].cfFormat;
                    builder.Append(GetClipboardFormatName(id, name, name.Capacity) > 0 ? name.ToString() : "#" + id).Append(' ');
                }
            }
            catch (Exception ex) when (ex is COMException or NotImplementedException or InvalidCastException)
            {
                builder.Append("(EnumFormatEtc failed: ").Append(ex.Message).Append(')');
            }

            return builder.ToString().Trim();
        }

        private const short CfUnicodeText = 13;

        private static byte[]? ReadHGlobal(ComTypes.IDataObject data, short format)
        {
            var request = new ComTypes.FORMATETC { cfFormat = format, dwAspect = ComTypes.DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = ComTypes.TYMED.TYMED_HGLOBAL };
            ComTypes.STGMEDIUM medium;
            try
            {
                if (data.QueryGetData(ref request) != 0)
                {
                    return null;
                }

                data.GetData(ref request, out medium);
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException)
            {
                return null;
            }

            try
            {
                if (medium.tymed != ComTypes.TYMED.TYMED_HGLOBAL || medium.unionmember == IntPtr.Zero)
                {
                    return null;
                }

                var size = (int)GlobalSize(medium.unionmember).ToUInt64();
                var pointer = GlobalLock(medium.unionmember);
                if (pointer == IntPtr.Zero || size <= 0 || size > 1 << 16)
                {
                    return null;
                }

                try
                {
                    var bytes = new byte[size];
                    Marshal.Copy(pointer, bytes, 0, size);
                    return bytes;
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

        [DllImport("ole32.dll")]
        public static extern int RegisterDragDrop(IntPtr hwnd, OleInterop.IDropTarget target);

        [DllImport("ole32.dll")]
        public static extern int RevokeDragDrop(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint RegisterClipboardFormat(string format);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClipboardFormatName(uint format, StringBuilder name, int size);

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
