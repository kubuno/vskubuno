using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Kubuno.VisualStudio.Core.IntelliSense
{
    /// <summary>One tab stop of an expanded snippet: its number (0 = final caret) and its span in the expanded text.</summary>
    public sealed class SnippetStop
    {
        public SnippetStop(int index, int start, int length)
        {
            Index = index;
            Start = start;
            Length = length;
        }

        public int Index { get; }

        public int Start { get; }

        public int Length { get; }

        public override string ToString() => "$" + Index + "@" + Start + "+" + Length;
    }

    /// <summary>
    /// An LSP snippet (<c>InsertTextFormat.Snippet</c>: <c>$1</c>, <c>${1:placeholder}</c>, <c>${1|a,b|}</c>,
    /// <c>$0</c>, <c>\$</c> escapes) expanded to plain text plus its tab stops, in visiting order (1, 2, ..., then 0).
    /// Variables (<c>$TM_SELECTED_TEXT</c>...) expand to nothing.
    /// </summary>
    public sealed class LspSnippet
    {
        private LspSnippet(string text, IReadOnlyList<SnippetStop> stops)
        {
            Text = text;
            Stops = stops;
        }

        public string Text { get; }

        /// <summary>The tab stops in the order Tab visits them; the final <c>$0</c> (if any) is last.</summary>
        public IReadOnlyList<SnippetStop> Stops { get; }

        /// <summary>The final caret offset in <see cref="Text"/>: <c>$0</c>, else the end.</summary>
        public int FinalCaret => Stops.FirstOrDefault(s => s.Index == 0)?.Start ?? Text.Length;

        /// <summary>True when there is at least one stop other than the final caret (worth a Tab session).</summary>
        public bool HasPlaceholders => Stops.Any(s => s.Index != 0);

        public static LspSnippet Parse(string snippet)
        {
            var output = new StringBuilder();
            var stops = new List<SnippetStop>();
            int i = 0;
            ParseInto(snippet ?? string.Empty, ref i, output, stops, terminator: null);

            // Visit 1, 2, ... in order (first occurrence of each number), then $0.
            var ordered = stops
                .GroupBy(s => s.Index)
                .Select(g => g.First())
                .OrderBy(s => s.Index == 0 ? int.MaxValue : s.Index)
                .ToList();
            return new LspSnippet(output.ToString(), ordered);
        }

        private static void ParseInto(string s, ref int i, StringBuilder output, List<SnippetStop> stops, char? terminator)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (terminator.HasValue && c == terminator.Value)
                {
                    return;
                }

                if (c == '\\' && i + 1 < s.Length && (s[i + 1] == '$' || s[i + 1] == '}' || s[i + 1] == '\\' || s[i + 1] == ',' || s[i + 1] == '|'))
                {
                    output.Append(s[i + 1]);
                    i += 2;
                    continue;
                }

                if (c == '$' && i + 1 < s.Length)
                {
                    if (char.IsDigit(s[i + 1]))
                    {
                        int j = i + 1;
                        while (j < s.Length && char.IsDigit(s[j]))
                        {
                            j++;
                        }

                        stops.Add(new SnippetStop(int.Parse(s.Substring(i + 1, j - i - 1)), output.Length, 0));
                        i = j;
                        continue;
                    }

                    if (s[i + 1] == '{')
                    {
                        int j = i + 2;
                        int numberStart = j;
                        while (j < s.Length && char.IsDigit(s[j]))
                        {
                            j++;
                        }

                        if (j > numberStart && j < s.Length)
                        {
                            int index = int.Parse(s.Substring(numberStart, j - numberStart));
                            int start = output.Length;
                            if (s[j] == '}')
                            {
                                stops.Add(new SnippetStop(index, start, 0));
                                i = j + 1;
                                continue;
                            }

                            if (s[j] == ':')
                            {
                                i = j + 1;
                                ParseInto(s, ref i, output, stops, '}');
                                i++; // the closing brace
                                stops.Add(new SnippetStop(index, start, output.Length - start));
                                continue;
                            }

                            if (s[j] == '|')
                            {
                                // A choice: the first option is the placeholder.
                                int end = s.IndexOf("|}", j + 1, StringComparison.Ordinal);
                                if (end > j)
                                {
                                    var first = SplitChoices(s.Substring(j + 1, end - j - 1)).FirstOrDefault() ?? string.Empty;
                                    output.Append(first);
                                    stops.Add(new SnippetStop(index, start, first.Length));
                                    i = end + 2;
                                    continue;
                                }
                            }
                        }
                        else if (j == numberStart)
                        {
                            // ${VARIABLE} or ${VARIABLE:default}: keep the default.
                            int k = j;
                            while (k < s.Length && (char.IsLetterOrDigit(s[k]) || s[k] == '_'))
                            {
                                k++;
                            }

                            if (k > j && k < s.Length && (s[k] == '}' || s[k] == ':'))
                            {
                                i = k + 1;
                                if (s[k] == ':')
                                {
                                    ParseInto(s, ref i, output, stops, '}');
                                    i++;
                                }

                                continue;
                            }
                        }
                    }
                    else if (char.IsLetter(s[i + 1]) || s[i + 1] == '_')
                    {
                        // $VARIABLE: nothing to insert.
                        int k = i + 1;
                        while (k < s.Length && (char.IsLetterOrDigit(s[k]) || s[k] == '_'))
                        {
                            k++;
                        }

                        i = k;
                        continue;
                    }
                }

                output.Append(c);
                i++;
            }
        }

        private static IEnumerable<string> SplitChoices(string choices)
        {
            var current = new StringBuilder();
            for (int i = 0; i < choices.Length; i++)
            {
                if (choices[i] == '\\' && i + 1 < choices.Length)
                {
                    current.Append(choices[++i]);
                }
                else if (choices[i] == ',')
                {
                    yield return current.ToString();
                    current.Clear();
                }
                else
                {
                    current.Append(choices[i]);
                }
            }

            yield return current.ToString();
        }
    }
}
