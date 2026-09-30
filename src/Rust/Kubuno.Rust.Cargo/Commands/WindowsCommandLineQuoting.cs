using System.Collections.Generic;
using System.Text;

namespace Kubuno.Rust.Cargo.Commands
{
    /// <summary>
    /// Joins argv-style arguments into a single Windows command-line string using the same
    /// quoting rules as the Microsoft C runtime / <c>CommandLineToArgvW</c> (and, notably, the
    /// same rules .NET's own <c>Process</c> applies when given an argument list) — so an
    /// argument round-trips correctly however many literal backslashes and quotes it contains.
    /// </summary>
    internal static class WindowsCommandLineQuoting
    {
        public static string Join(IEnumerable<string> arguments)
        {
            var builder = new StringBuilder();
            bool first = true;
            foreach (string argument in arguments)
            {
                if (!first)
                {
                    builder.Append(' ');
                }
                first = false;
                AppendArgument(builder, argument);
            }
            return builder.ToString();
        }

        private static void AppendArgument(StringBuilder builder, string argument)
        {
            // Arguments that contain none of the characters requiring quoting can be emitted verbatim.
            if (argument.Length != 0 && !NeedsQuoting(argument))
            {
                builder.Append(argument);
                return;
            }

            builder.Append('"');
            for (int i = 0; i < argument.Length;)
            {
                char c = argument[i++];
                if (c == '\\')
                {
                    int backslashCount = 1;
                    while (i < argument.Length && argument[i] == '\\')
                    {
                        backslashCount++;
                        i++;
                    }

                    if (i == argument.Length)
                    {
                        // Backslashes right before the closing quote must be doubled, since the
                        // closing quote that follows would otherwise escape the last one.
                        builder.Append('\\', backslashCount * 2);
                    }
                    else if (argument[i] == '"')
                    {
                        // Backslashes immediately followed by a literal quote must be doubled,
                        // plus one extra backslash to escape that quote.
                        builder.Append('\\', backslashCount * 2 + 1);
                        builder.Append('"');
                        i++;
                    }
                    else
                    {
                        // Backslashes not followed by a quote are literal: left untouched.
                        builder.Append('\\', backslashCount);
                    }
                }
                else if (c == '"')
                {
                    builder.Append('\\').Append('"');
                }
                else
                {
                    builder.Append(c);
                }
            }
            builder.Append('"');
        }

        private static bool NeedsQuoting(string argument)
        {
            foreach (char c in argument)
            {
                if (c == ' ' || c == '\t' || c == '\n' || c == '\v' || c == '"')
                {
                    return true;
                }
            }
            return false;
        }
    }
}
