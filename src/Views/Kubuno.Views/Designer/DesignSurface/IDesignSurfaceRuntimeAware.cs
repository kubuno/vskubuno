using System;
using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Kubuno.Views.Logging;

namespace Kubuno.Views.Designer.DesignSurface
{
    /// <summary>
    /// A host whose surface can run different runtimes (docs/DESIGNER.md section 15) - what
    /// <see cref="UI.DesignerSplitView"/> reads to show the runtime info bar.
    /// </summary>
    public interface IDesignSurfaceRuntimeAware
    {
        IDesignSurfaceRuntimeSource RuntimeSource { get; }

        /// <summary>Raised on the UI thread when a surface failed the <c>surfaceInfo</c> handshake (the message says why).</summary>
        event EventHandler<string>? RuntimeRejected;
    }
}
