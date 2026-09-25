using System.Diagnostics;

namespace Kubuno.VisualStudio.Views.Logging
{
    /// <summary>
    /// Static gateway to the <see cref="IKubunoLog"/> this library logs through. MEF-constructed
    /// components (<see cref="LanguageService.KubunoViewsLanguageClient"/>, the missing-server info
    /// bar) read <see cref="Current"/> rather than importing a logger, since nothing here is built by
    /// the VSIX's package class. See <see cref="IKubunoLog"/>'s remarks for why the VSIX supplies the
    /// implementation instead of this library depending on <c>Kubuno.VisualStudio.Logging.KubunoLog</c>
    /// directly.
    /// </summary>
    public static class KubunoViewsLogHost
    {
        private static IKubunoLog _current = NullKubunoLog.Instance;

        /// <summary>
        /// The active logger. Set by the VSIX during package initialization; defaults to a no-op
        /// logger (never <see langword="null"/>) so components can log unconditionally at any time,
        /// including before that initialization has run.
        /// </summary>
        public static IKubunoLog Current
        {
            get => _current;
            set => _current = value ?? NullKubunoLog.Instance;
        }

        /// <summary>
        /// A <see cref="TraceListener"/> that forwards to <see cref="Current"/>, meant to be attached
        /// to the StreamJsonRpc <c>JsonRpc.TraceSource</c> of the kubuno-views-ls connection so LSP
        /// traffic/errors show up in the same "Kubuno" pane the Rust language client already uses
        /// (mirrors <c>KubunoLog.CreateJsonRpcTraceListener</c> in the VSIX).
        /// </summary>
        public static TraceListener CreateJsonRpcTraceListener() => new ForwardingTraceListener();

        private sealed class ForwardingTraceListener : TraceListener
        {
            public override void Write(string? message)
            {
                if (message != null)
                {
                    Current.WriteLine(message);
                }
            }

            public override void WriteLine(string? message)
            {
                if (message != null)
                {
                    Current.WriteLine(message);
                }
            }
        }
    }
}
