using System;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Desktop.Logic.Overrides;
using Kubuno.Desktop.Designer;
using Microsoft.VisualStudio.Core.Imaging;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace Kubuno.Desktop.LanguageService.Overrides
{
    /// <summary>
    /// Kubuno completion items in Rust files, next to rust-analyzer's (docs/EVENTS.md EVT-7b): inside
    /// <c>impl Control for X</c> (any level trait) the members the class can still override, with their exact
    /// signature and a body calling the base (like typing <c>override</c> in C#), and the snippets <c>onpaint</c>,
    /// <c>handler</c> (in an impl), <c>event</c>, <c>prop</c> (in a control's struct). Works without rust-analyzer.
    /// </summary>
    [Export(typeof(IAsyncCompletionSourceProvider))]
    [Name("Kubuno Rust Control Members")]
    [ContentType(Kubuno.Rust.Constants.RustContentType)]
    internal sealed class KubunoRustCompletionSourceProvider : IAsyncCompletionSourceProvider
    {
        public IAsyncCompletionSource GetOrCreate(ITextView textView) =>
            textView.Properties.GetOrCreateSingletonProperty(() => new KubunoRustCompletionSource());
    }

    internal sealed class KubunoRustCompletionSource : IAsyncCompletionSource
    {
        private static readonly ImageElement MethodIcon = new ImageElement(KnownMonikers.MethodProtected.ToImageId(), "method");
        private static readonly ImageElement SnippetIcon = new ImageElement(KnownMonikers.Snippet.ToImageId(), "snippet");
        private const string MemberKey = "Kubuno.Member";
        private const string SnippetKey = "Kubuno.Snippet";

        public CompletionStartData InitializeCompletion(CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token)
        {
            if (trigger.Reason == CompletionTriggerReason.Insertion && !(char.IsLetter(trigger.Character) || trigger.Character == '_'))
            {
                return CompletionStartData.DoesNotParticipateInCompletion;
            }

            var snapshot = triggerLocation.Snapshot;
            var site = OverrideAssistant.CompletionSiteAt(snapshot.GetText(), triggerLocation.Position, OverrideCatalog.Default);
            if (site is null)
            {
                return CompletionStartData.DoesNotParticipateInCompletion;
            }

            // The span: the identifier being typed (rust-analyzer's own items use the same).
            var start = triggerLocation.Position;
            while (start > 0 && (char.IsLetterOrDigit(snapshot[start - 1]) || snapshot[start - 1] == '_'))
            {
                start--;
            }

            return new CompletionStartData(CompletionParticipation.ProvidesItems, new SnapshotSpan(snapshot, start, triggerLocation.Position - start));
        }

        public Task<CompletionContext> GetCompletionContextAsync(IAsyncCompletionSession session, CompletionTrigger trigger, SnapshotPoint triggerLocation, SnapshotSpan applicableToSpan, CancellationToken token)
        {
            var snapshot = triggerLocation.Snapshot;
            var site = OverrideAssistant.CompletionSiteAt(snapshot.GetText(), triggerLocation.Position, OverrideCatalog.Default);
            if (site is null)
            {
                return Task.FromResult(CompletionContext.Empty);
            }

            var french = DesignerText.IsFrench;
            var newline = snapshot.GetText().Contains("\r\n") ? "\r\n" : "\n";
            var items = ImmutableArray.CreateBuilder<CompletionItem>();

            // What the line already holds before the identifier (`fn `): the inserted text replaces it too.
            var prefix = snapshot.GetText(site.ReplaceStart, applicableToSpan.Start.Position - site.ReplaceStart);
            string Insert(string text) => text.StartsWith(prefix, StringComparison.Ordinal) ? text.Substring(prefix.Length) : text;

            if (site.Context is { } context)
            {
                foreach (var member in context.Available)
                {
                    var stub = member.Stub(site.Indent, newline).Substring(site.Indent.Length).TrimEnd('\r', '\n');
                    var item = new CompletionItem(member.Name, this, MethodIcon, ImmutableArray<CompletionFilter>.Empty, member.Level, Insert(stub), "0" + member.Name, member.Name, ImmutableArray<ImageElement>.Empty);
                    item.Properties.AddProperty(MemberKey, member);
                    items.Add(item);
                }
            }

            foreach (var snippet in RustSnippets.All.Where(s => s.InImpl == site.InImpl && (s.Shortcut != "onpaint" || site.Context is not null)))
            {
                var text = snippet.Text(site.Indent, newline);
                var item = new CompletionItem(snippet.Shortcut, this, SnippetIcon, ImmutableArray<CompletionFilter>.Empty, french ? "extrait Kubuno" : "Kubuno snippet", Insert(text), "1" + snippet.Shortcut, snippet.Shortcut, ImmutableArray<ImageElement>.Empty);
                item.Properties.AddProperty(SnippetKey, snippet);
                items.Add(item);
            }

            return Task.FromResult(new CompletionContext(items.ToImmutable()));
        }

        public Task<object> GetDescriptionAsync(IAsyncCompletionSession session, CompletionItem item, CancellationToken token)
        {
            var french = DesignerText.IsFrench;
            if (item.Properties.TryGetProperty(MemberKey, out OverridableMember member))
            {
                return Task.FromResult<object>(member.Signature + Environment.NewLine + member.LocalizedDoc(french));
            }

            if (item.Properties.TryGetProperty(SnippetKey, out RustSnippet snippet))
            {
                return Task.FromResult<object>((french ? snippet.DescriptionFr : snippet.Description) + Environment.NewLine + string.Join(Environment.NewLine, snippet.Lines));
            }

            return Task.FromResult<object>(string.Empty);
        }
    }
}
