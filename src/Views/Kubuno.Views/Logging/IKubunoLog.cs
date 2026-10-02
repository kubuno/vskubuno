using System;

namespace Kubuno.Desktop.Views.Logging
{
    /// <summary>
    /// Everything this library needs to log a line or an exception, kept as an interface so it does
    /// not have to own (or duplicate) the VSIX's existing "Kubuno" Output pane
    /// (<c>src/Core/Kubuno.Core/Logging/KubunoLog.cs</c>) - this library must not reference that
    /// project (it will itself be referenced BY the VSIX once integrated, see INTEGRATION.md; a
    /// reference the other way would be circular). The VSIX supplies a small adapter implementing
    /// this interface, wrapping its own <c>KubunoLog.WriteLine</c>/<c>WriteException</c>, and installs
    /// it into <see cref="KubunoViewsLogHost.Current"/> during package initialization.
    ///
    /// Implementations must never throw: logging must not become a new source of failure for the
    /// very code (locator, language client) that reports failures through it.
    /// </summary>
    public interface IKubunoLog
    {
        void WriteLine(string message);

        void WriteException(string context, Exception exception);
    }
}
