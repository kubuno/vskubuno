using System.ComponentModel.Composition;
using Kubuno.Rust.Extensibility;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Text;

namespace Kubuno.Desktop.LanguageService.Sql
{
    /// <summary>
    /// SQL in Rust query strings (docs/DATA.md DATA-8) as a language embedded in Rust: rust-analyzer's completion stays out
    /// of a query string, where <see cref="SqlCompletionSource"/> lists the schema, and a SQL session commits with its own
    /// characters (the Rust layer's completion asks every <see cref="IRustEmbeddedLanguage"/>).
    /// </summary>
    [Export(typeof(IRustEmbeddedLanguage))]
    internal sealed class SqlEmbeddedLanguage : IRustEmbeddedLanguage
    {
        public bool OwnsPosition(SnapshotPoint point) => SqlCompletionSource.IsInSql(point);

        public string? CommitCharactersFor(IAsyncCompletionSession session) =>
            SqlCompletionSource.IsSqlSession(session) ? SqlCompletionSource.CommitCharacters : null;
    }
}
