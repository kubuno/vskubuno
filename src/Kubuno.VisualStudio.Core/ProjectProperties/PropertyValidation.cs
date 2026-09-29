using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Kubuno.VisualStudio.Core.ProjectProperties
{
    /// <summary>
    /// Validation of the values typed in the <c>.rsproj</c> Project Properties editor before anything is
    /// written to Cargo.toml / rustfmt.toml (docs/RSPROJ.md, "Project properties like .NET"). The rules are
    /// Cargo's and crates.io's own, so an accepted value never produces a manifest Cargo rejects.
    /// </summary>
    public static class PropertyValidation
    {
        private static readonly Regex CrateNameRegex = new Regex("^[A-Za-z][A-Za-z0-9_-]{0,63}$", RegexOptions.CultureInvariant);

        // https://semver.org/#is-there-a-suggested-regular-expression-regex-to-check-a-semver-string
        private static readonly Regex SemVerRegex = new Regex(
            @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$",
            RegexOptions.CultureInvariant);

        private static readonly Regex RustVersionRegex = new Regex(@"^(0|[1-9]\d*)(\.(0|[1-9]\d*)){1,2}$", RegexOptions.CultureInvariant);

        private static readonly Regex KeywordRegex = new Regex("^[A-Za-z][A-Za-z0-9_+-]{0,19}$", RegexOptions.CultureInvariant);

        private static readonly Regex SpdxIdRegex = new Regex(@"^[A-Za-z0-9.+-]+$", RegexOptions.CultureInvariant);

        /// <summary>A Cargo package name crates.io accepts: ASCII letters, digits, '-' and '_', starting with a letter, 64 characters at most.</summary>
        public static bool IsValidCrateName(string value) => CrateNameRegex.IsMatch(value) && !IsReservedWindowsName(value);

        /// <summary>A SemVer 2.0 version (major.minor.patch[-pre][+build]), as Cargo requires for [package] version.</summary>
        public static bool IsValidSemVer(string value) => SemVerRegex.IsMatch(value);

        /// <summary>A rust-version (MSRV): two or three numbers, e.g. 1.80 or 1.80.1.</summary>
        public static bool IsValidRustVersion(string value) => RustVersionRegex.IsMatch(value);

        /// <summary>An absolute http(s) URL.</summary>
        public static bool IsValidUrl(string value) =>
            Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        /// <summary>A crates.io keyword: ASCII, starts with a letter, letters/digits/'_'/'-'/'+', 20 characters at most.</summary>
        public static bool IsValidKeyword(string value) => KeywordRegex.IsMatch(value);

        /// <summary>A whole number within [min, max].</summary>
        public static bool IsIntegerInRange(string value, long min, long max) =>
            long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long number) && number >= min && number <= max;

        /// <summary>
        /// An SPDX license expression as crates.io accepts it: license identifiers (optionally with a trailing
        /// '+'), combined with AND / OR, an exception after WITH, and balanced parentheses. The identifiers
        /// themselves are not checked against the SPDX list (it keeps growing); the syntax is.
        /// </summary>
        public static bool IsValidSpdxExpression(string value)
        {
            // crates.io still accepts the legacy "MIT/Apache-2.0" form, meaning "MIT OR Apache-2.0".
            List<string>? tokens = TokenizeSpdx(value.Replace("/", " OR "));
            if (tokens is null || tokens.Count == 0)
            {
                return false;
            }

            int position = 0;
            return ParseOr(tokens, ref position) && position == tokens.Count;
        }

        private static bool IsReservedWindowsName(string value)
        {
            switch (value.ToLowerInvariant())
            {
                case "con":
                case "prn":
                case "aux":
                case "nul":
                case "com1":
                case "com2":
                case "com3":
                case "com4":
                case "com5":
                case "com6":
                case "com7":
                case "com8":
                case "com9":
                case "lpt1":
                case "lpt2":
                case "lpt3":
                case "lpt4":
                case "lpt5":
                case "lpt6":
                case "lpt7":
                case "lpt8":
                case "lpt9":
                    return true;
                default:
                    return false;
            }
        }

        private static List<string>? TokenizeSpdx(string value)
        {
            var tokens = new List<string>();
            int i = 0;
            while (i < value.Length)
            {
                char c = value[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }
                if (c == '(' || c == ')')
                {
                    tokens.Add(c.ToString());
                    i++;
                    continue;
                }
                int start = i;
                while (i < value.Length && !char.IsWhiteSpace(value[i]) && value[i] != '(' && value[i] != ')')
                {
                    i++;
                }
                string token = value.Substring(start, i - start);
                if (!SpdxIdRegex.IsMatch(token))
                {
                    return null;
                }
                tokens.Add(token);
            }
            return tokens;
        }

        private static bool ParseOr(List<string> tokens, ref int position)
        {
            if (!ParseAnd(tokens, ref position))
            {
                return false;
            }
            while (position < tokens.Count && tokens[position] == "OR")
            {
                position++;
                if (!ParseAnd(tokens, ref position))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool ParseAnd(List<string> tokens, ref int position)
        {
            if (!ParseWith(tokens, ref position))
            {
                return false;
            }
            while (position < tokens.Count && tokens[position] == "AND")
            {
                position++;
                if (!ParseWith(tokens, ref position))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool ParseWith(List<string> tokens, ref int position)
        {
            if (!ParsePrimary(tokens, ref position))
            {
                return false;
            }
            if (position < tokens.Count && tokens[position] == "WITH")
            {
                position++;
                return position < tokens.Count && IsIdentifier(tokens[position++]);
            }
            return true;
        }

        private static bool ParsePrimary(List<string> tokens, ref int position)
        {
            if (position >= tokens.Count)
            {
                return false;
            }
            string token = tokens[position];
            if (token == "(")
            {
                position++;
                if (!ParseOr(tokens, ref position) || position >= tokens.Count || tokens[position] != ")")
                {
                    return false;
                }
                position++;
                return true;
            }
            position++;
            return IsIdentifier(token);
        }

        private static bool IsIdentifier(string token) =>
            token != "AND" && token != "OR" && token != "WITH" && token != "(" && token != ")";
    }
}
