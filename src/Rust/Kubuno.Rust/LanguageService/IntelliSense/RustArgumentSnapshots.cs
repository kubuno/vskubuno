using System;
using System.Collections.Concurrent;
using Microsoft.VisualStudio.Text;

namespace Kubuno.Rust.LanguageService.IntelliSense
{
    /// <summary>Reads the text that follows an inlay hint in the open document (callable from any thread).</summary>
    internal static class RustArgumentSnapshots
    {
        /// <summary>How much of the line after a hint is looked at: enough for any literal argument.</summary>
        private const int MaxLength = 400;

        private static readonly ConcurrentDictionary<string, WeakReference<ITextBuffer>> Buffers = new ConcurrentDictionary<string, WeakReference<ITextBuffer>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Remembers the buffer of an open .rs document (called when its view is created).</summary>
        public static void Register(string filePath, ITextBuffer buffer) => Buffers[filePath] = new WeakReference<ITextBuffer>(buffer);

        /// <summary>The current snapshot of the open document with this file URI, or null.</summary>
        public static ITextSnapshot? Find(string uri)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || !parsed.IsFile)
            {
                return null;
            }

            return Buffers.TryGetValue(parsed.LocalPath, out var reference) && reference.TryGetTarget(out var buffer) ? buffer.CurrentSnapshot : null;
        }

        /// <summary>The line's text from the LSP position on, or null when the position is outside the snapshot.</summary>
        public static string? TextAt(ITextSnapshot snapshot, int line, int character)
        {
            if (line < 0 || character < 0 || line >= snapshot.LineCount)
            {
                return null;
            }

            var text = snapshot.GetLineFromLineNumber(line);
            if (character > text.Length)
            {
                return null;
            }

            return snapshot.GetText(text.Start.Position + character, Math.Min(MaxLength, text.Length - character));
        }
    }
}
