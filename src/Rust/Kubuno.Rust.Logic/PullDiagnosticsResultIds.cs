using System.Threading;

namespace Kubuno.Rust.Logic
{
    /// <summary>
    /// Makes every <c>textDocument/diagnostic</c> (pull diagnostics) report from rust-analyzer carry a
    /// distinct <c>resultId</c>.
    ///
    /// Why: rust-analyzer answers every document pull with the SAME constant <c>resultId</c>
    /// (<c>"rust-analyzer"</c>) and always a full report. Visual Studio's LSP client (decompiled
    /// <c>RemoteDiagnosticsBrokerHelper.SendDiagnosticReportsToDiagnosticBrokerAsync</c>) treats a report
    /// that has NO diagnostics and the SAME <c>resultId</c> as the previous one as "unchanged" and drops it
    /// without clearing anything. So once a file had errors (typing <c>vm.set(</c> reports E0107 and a
    /// syntax error), fixing the code produced an empty report with the same id, and the old errors stayed
    /// in the editor and the Error List for good - although rust-analyzer's copy of the file was in sync
    /// and reported nothing. A fresh id per report makes Visual Studio apply every report, including the
    /// empty one that clears the errors.
    /// </summary>
    public sealed class PullDiagnosticsResultIds
    {
        private long _counter;

        /// <summary>
        /// A <c>resultId</c> unique to this report, derived from the server's own; <c>null</c> stays
        /// <c>null</c> (a report without an id is never treated as "unchanged" by Visual Studio).
        /// </summary>
        public string? MakeUnique(string? serverResultId)
        {
            if (serverResultId is null)
            {
                return null;
            }

            return serverResultId + "#" + Interlocked.Increment(ref _counter);
        }
    }
}
