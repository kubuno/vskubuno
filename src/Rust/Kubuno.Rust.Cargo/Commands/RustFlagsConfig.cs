using System;
using System.Collections.Generic;
using System.Text;

namespace Kubuno.Cargo.Commands
{
    /// <summary>
    /// Turns the rustc flags of a <c>.rsproj</c> (its "Additional rustc flags", "Treat warnings as errors" and
    /// "Conditional compilation flags" build properties) into one <c>--config build.rustflags=[...]</c> override.
    /// A config array given on the command line is merged with the <c>build.rustflags</c> arrays of Cargo's
    /// config files instead of replacing them, unlike the <c>RUSTFLAGS</c> environment variable.
    /// </summary>
    public static class RustFlagsConfig
    {
        /// <summary>Splits flags on whitespace, exactly as Cargo splits a string-valued <c>build.rustflags</c>.</summary>
        public static IReadOnlyList<string> Split(string? flags)
        {
            if (string.IsNullOrWhiteSpace(flags))
            {
                return Array.Empty<string>();
            }

            return flags!.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>
        /// <c>build.rustflags=["-D", "warnings"]</c> for the given whitespace-separated flags (values as TOML
        /// basic strings), or null when there are none.
        /// </summary>
        public static string? Format(string? flags)
        {
            IReadOnlyList<string> tokens = Split(flags);
            if (tokens.Count == 0)
            {
                return null;
            }

            var builder = new StringBuilder("build.rustflags=[");
            for (int i = 0; i < tokens.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }
                AppendTomlString(builder, tokens[i]);
            }
            builder.Append(']');
            return builder.ToString();
        }

        private static void AppendTomlString(StringBuilder builder, string value)
        {
            builder.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    default:
                        if (c < 0x20)
                        {
                            builder.Append("\\u").Append(((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }
                        break;
                }
            }
            builder.Append('"');
        }
    }
}
