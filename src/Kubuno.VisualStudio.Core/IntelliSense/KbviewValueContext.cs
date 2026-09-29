using System;

namespace Kubuno.VisualStudio.Core.IntelliSense
{
    /// <summary>What the caret is on inside a <c>.kbview</c> attribute value, for the cross-language completions.</summary>
    public enum KbviewValueKind
    {
        /// <summary>The value of an event attribute (<c>OnClick="|"</c>): a code-behind handler name.</summary>
        Handler,

        /// <summary>The path of a binding (<c>Text="{Binding |}"</c>): a view-model property.</summary>
        BindingPath,
    }

    /// <summary>
    /// The attribute value around a caret in <c>.kbview</c> text: which attribute, and the span of the name being
    /// typed (identifier characters around the caret) that a completion replaces.
    /// </summary>
    public sealed class KbviewValueContext
    {
        private KbviewValueContext(KbviewValueKind kind, string attribute, int replaceStart, int replaceLength)
        {
            Kind = kind;
            Attribute = attribute;
            ReplaceStart = replaceStart;
            ReplaceLength = replaceLength;
        }

        public KbviewValueKind Kind { get; }

        /// <summary>The attribute's name (<c>OnClick</c>, <c>Text</c>...).</summary>
        public string Attribute { get; }

        public int ReplaceStart { get; }

        public int ReplaceLength { get; }

        /// <summary>The same context, its offsets moved by <paramref name="offset"/> (a line-relative context made buffer-relative).</summary>
        public KbviewValueContext Shift(int offset) => new KbviewValueContext(Kind, Attribute, ReplaceStart + offset, ReplaceLength);

        /// <summary>The context at <paramref name="caret"/> in <paramref name="text"/>, or null outside an event value or a binding path.</summary>
        public static KbviewValueContext? At(string text, int caret)
        {
            if (text is null || caret < 0 || caret > text.Length)
            {
                return null;
            }

            // The opening quote of the value the caret is in: the nearest quote before it, with `=` (and the name) before that.
            int quote = -1;
            for (int i = caret - 1; i >= 0; i--)
            {
                char c = text[i];
                if (c == '"' || c == '\'')
                {
                    quote = i;
                    break;
                }

                if (c == '<' || c == '>' || c == '\n')
                {
                    return null;
                }
            }

            if (quote < 1)
            {
                return null;
            }

            int eq = quote - 1;
            while (eq >= 0 && char.IsWhiteSpace(text[eq]))
            {
                eq--;
            }

            if (eq < 0 || text[eq] != '=')
            {
                return null;
            }

            int nameEnd = eq;
            while (nameEnd > 0 && char.IsWhiteSpace(text[nameEnd - 1]))
            {
                nameEnd--;
            }

            int nameStart = nameEnd;
            while (nameStart > 0 && (char.IsLetterOrDigit(text[nameStart - 1]) || text[nameStart - 1] == '_' || text[nameStart - 1] == ':' || text[nameStart - 1] == '.'))
            {
                nameStart--;
            }

            if (nameStart == nameEnd || nameStart == 0 || !char.IsWhiteSpace(text[nameStart - 1]))
            {
                return null;
            }

            var attribute = text.Substring(nameStart, nameEnd - nameStart);
            var valueBefore = text.Substring(quote + 1, caret - quote - 1);

            int start = caret;
            while (start > quote + 1 && IsNameChar(text[start - 1]))
            {
                start--;
            }

            int end = caret;
            while (end < text.Length && IsNameChar(text[end]))
            {
                end++;
            }

            var trimmed = valueBefore.TrimStart();
            if (trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                // {Binding Path} / {Binding Path=Path}: only the path part completes.
                var inside = trimmed.Substring(1).TrimStart();
                if (!inside.StartsWith("Binding", StringComparison.Ordinal))
                {
                    return null;
                }

                var afterKeyword = inside.Substring("Binding".Length);
                if (afterKeyword.Length == 0 || !char.IsWhiteSpace(afterKeyword[0]))
                {
                    return null;
                }

                var path = afterKeyword.TrimStart();
                if (path.StartsWith("Path=", StringComparison.Ordinal))
                {
                    path = path.Substring("Path=".Length);
                }

                // Only the first argument (the path) - not Mode=..., Converter=...
                if (path.IndexOfAny(new[] { ',', '=', '}' }) >= 0)
                {
                    return null;
                }

                return new KbviewValueContext(KbviewValueKind.BindingPath, attribute, start, end - start);
            }

            if (attribute.Length > 2 && attribute.StartsWith("On", StringComparison.Ordinal) && char.IsUpper(attribute[2]))
            {
                return new KbviewValueContext(KbviewValueKind.Handler, attribute, start, end - start);
            }

            return null;
        }

        private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c == '_';
    }
}
