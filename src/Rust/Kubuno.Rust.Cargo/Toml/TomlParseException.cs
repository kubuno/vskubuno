using System;

namespace Kubuno.Cargo.Toml
{
    /// <summary>Thrown when a document is not valid TOML. <see cref="Line"/> and <see cref="Column"/> are 1-based.</summary>
    public sealed class TomlParseException : FormatException
    {
        /// <summary>Creates the exception for an error at the given 1-based position.</summary>
        public TomlParseException(string message, int line, int column)
            : base(message + " (line " + line + ", column " + column + ")")
        {
            Line = line;
            Column = column;
        }

        /// <summary>1-based line of the error.</summary>
        public int Line { get; }

        /// <summary>1-based column (UTF-16 units) of the error.</summary>
        public int Column { get; }
    }
}
