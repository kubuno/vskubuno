using System;

namespace Kubuno.VisualStudio.Views.Logging
{
    /// <summary>
    /// The default <see cref="IKubunoLog"/> installed in <see cref="KubunoViewsLogHost"/> until the
    /// VSIX supplies a real one (see <see cref="IKubunoLog"/>'s remarks). MEF can construct and
    /// activate <see cref="LanguageService.KubunoViewsLanguageClient"/> before the VSIX's package has
    /// finished loading, so a working no-op default - rather than a null check at every call site -
    /// is what keeps that ordering from ever throwing.
    /// </summary>
    internal sealed class NullKubunoLog : IKubunoLog
    {
        public static readonly NullKubunoLog Instance = new();

        private NullKubunoLog()
        {
        }

        public void WriteLine(string message)
        {
            // Intentionally a no-op: nowhere to log to before the VSIX installs a real IKubunoLog.
        }

        public void WriteException(string context, Exception exception)
        {
            // Intentionally a no-op; see WriteLine.
        }
    }
}
