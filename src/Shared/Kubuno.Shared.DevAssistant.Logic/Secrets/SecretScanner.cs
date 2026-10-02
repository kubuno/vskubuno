using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kubuno.Shared.DevAssistant.Logic.Secrets
{
    /// <summary>One secret found in a text: the exact span of the value to mask and its kind.</summary>
    public readonly struct SecretFinding
    {
        public SecretFinding(int start, int length, string kind)
        {
            Start = start;
            Length = length;
            Kind = kind;
        }

        public int Start { get; }

        public int Length { get; }

        /// <summary>Short kind used in the placeholder: <c>anthropic-key</c>, <c>npm-token</c>, <c>password</c>...</summary>
        public string Kind { get; }

        public int End => Start + Length;
    }

    /// <summary>
    /// Finds probable secrets in outgoing text (docs/AI-ASSISTANT.md section 8.3): known token formats (Anthropic,
    /// OpenAI, GitHub, npm, AWS, Slack, JWT, PEM private keys), credentials inside URLs and connection strings, and
    /// assignments to secret-like names whose value is not a placeholder or a code expression. Runs on every piece of
    /// text that leaves Visual Studio: the user's message, resolved references and tool results.
    /// </summary>
    public static class SecretScanner
    {
        private const RegexOptions Options = RegexOptions.CultureInvariant;

        /// <summary>Token formats whose whole match is the secret.</summary>
        private static readonly (string Kind, Regex Pattern)[] TokenFormats =
        {
            ("private-key", new Regex(@"-----BEGIN ((?:[A-Z]+ )*)PRIVATE KEY-----[\s\S]*?-----END \1PRIVATE KEY-----", Options)),
            ("anthropic-key", new Regex(@"\bsk-ant-[A-Za-z0-9_\-]{20,}", Options)),
            ("openai-key", new Regex(@"\bsk-(?:proj-|svcacct-)?[A-Za-z0-9_\-]{32,}", Options)),
            ("github-token", new Regex(@"\b(?:gh[pousr]_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{22,})", Options)),
            ("npm-token", new Regex(@"\bnpm_[A-Za-z0-9]{36}\b", Options)),
            ("aws-key", new Regex(@"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b", Options)),
            ("slack-token", new Regex(@"\bxox[abprs]-[A-Za-z0-9\-]{10,}", Options)),
            ("jwt", new Regex(@"\beyJ[A-Za-z0-9_\-]{8,}\.eyJ[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}", Options)),
        };

        /// <summary>scheme://user:PASSWORD@host - group "v" is the password.</summary>
        private static readonly Regex UrlCredentials = new Regex(@"\b[a-zA-Z][a-zA-Z0-9+.\-]*://[^\s:/@'""]+:(?<v>[^\s@/'""]+)@", Options);

        /// <summary>Password=... / Pwd=... in connection strings - group "v".</summary>
        private static readonly Regex ConnectionStringPassword = new Regex(@"(?i)\b(?:password|pwd)\s*=\s*(?<v>[^;'""\s]+)", Options);

        /// <summary>name = value / name: value / "name": "value" for secret-like names - group "v".</summary>
        private static readonly Regex Assignment = new Regex(
            @"(?i)(?<name>[A-Za-z0-9_\-\.]*(?:password|passwd|passphrase|secret|token|api[_\-]?key|apikey|auth[_\-]?token|access[_\-]?key|private[_\-]?key|credential)[A-Za-z0-9_\-\.]*)[""']?\s*(?::=|=|:)\s*(?<q>[""'`]?)(?<v>[^\s""'`,;]{6,})\k<q>",
            Options);

        /// <summary>A line naming a password or token and showing a quoted or back-ticked value (prose, Markdown) - group "v".</summary>
        private static readonly Regex ProseSecret = new Regex(
            @"(?i)(?:password|passwd|mot de passe|mdp|secret|token|jeton|api key|clé d'api)[^\r\n`'""]{0,60}?[`'""](?<v>[^\s`'""]{8,})[`'""]",
            Options);

        private static readonly Regex IdentifierExpression = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*(?:(?:\.|::|->)[A-Za-z_][A-Za-z0-9_]*)*(?:\(.*\)?|\[.*\]?|<.*>?|;)?$", Options);

        private static readonly string[] PlaceholderValues =
        {
            "changeme", "change-me", "password", "passwd", "secret", "token", "example", "placeholder", "your", "xxx", "****", "none", "null", "undefined", "redacted", "dummy", "test",
        };

        /// <summary>Every probable secret in <paramref name="text"/>, sorted and non-overlapping.</summary>
        public static IReadOnlyList<SecretFinding> Scan(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return Array.Empty<SecretFinding>();
            }

            var findings = new List<SecretFinding>();
            foreach (var (kind, pattern) in TokenFormats)
            {
                foreach (Match match in pattern.Matches(text))
                {
                    findings.Add(new SecretFinding(match.Index, match.Length, kind));
                }
            }

            AddValues(findings, UrlCredentials, text!, "url-password", requireEntropy: false);
            AddValues(findings, ConnectionStringPassword, text!, "password", requireEntropy: false);
            AddValues(findings, Assignment, text!, null, requireEntropy: true);
            AddValues(findings, ProseSecret, text!, "password", requireEntropy: true);

            // Keep the earliest, longest finding of overlapping ones (a token format wins over a generic assignment).
            var result = new List<SecretFinding>();
            foreach (var finding in findings.OrderBy(f => f.Start).ThenByDescending(f => f.Length))
            {
                if (result.Count > 0 && finding.Start < result[result.Count - 1].End)
                {
                    continue;
                }

                result.Add(finding);
            }

            return result;
        }

        /// <summary>True when <paramref name="text"/> contains a probable secret.</summary>
        public static bool ContainsSecret(string? text) => Scan(text).Count > 0;

        private static void AddValues(List<SecretFinding> findings, Regex pattern, string text, string? kind, bool requireEntropy)
        {
            foreach (Match match in pattern.Matches(text))
            {
                var value = match.Groups["v"];
                if (!value.Success || value.Length == 0 || IsPlaceholder(value.Value))
                {
                    continue;
                }

                if (requireEntropy && !LooksRandom(value.Value))
                {
                    continue;
                }

                findings.Add(new SecretFinding(value.Index, value.Length, kind ?? KindFromName(match.Groups["name"].Value)));
            }
        }

        private static string KindFromName(string name)
        {
            var lower = name.ToLowerInvariant();
            if (lower.Contains("pass"))
            {
                return "password";
            }

            if (lower.Contains("key"))
            {
                return "api-key";
            }

            if (lower.Contains("token"))
            {
                return "token";
            }

            return "secret";
        }

        /// <summary>Values that are obviously not real secrets: placeholders, environment/template references, our own markers.</summary>
        internal static bool IsPlaceholder(string value)
        {
            if (value.StartsWith("«secret:", StringComparison.Ordinal) || value.StartsWith("$", StringComparison.Ordinal) ||
                value.StartsWith("%", StringComparison.Ordinal) || value.StartsWith("{", StringComparison.Ordinal) ||
                value.StartsWith("<", StringComparison.Ordinal) || value.StartsWith("&", StringComparison.Ordinal))
            {
                return true;
            }

            var lower = value.ToLowerInvariant();
            if (PlaceholderValues.Contains(lower) || lower.StartsWith("your", StringComparison.Ordinal))
            {
                return true;
            }

            if (lower.All(c => c == 'x' || c == '*' || c == '.' || c == '-' || c == '_'))
            {
                return true;
            }

            if (lower.Contains("env") && (lower.Contains("::") || lower.Contains(".") || lower.Contains("(")))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// A value that could be a real secret rather than a code expression or a word: not an identifier chain (
        /// <c>self.token.clone()</c>, <c>String</c>), and either mixes character classes or has high entropy.
        /// </summary>
        internal static bool LooksRandom(string value)
        {
            if (value.Length < 6)
            {
                return false;
            }

            // A code expression (self.token.clone(), String, Option<String>) - but a random token is also a valid
            // identifier, so only identifiers without digits, or with member/call syntax, are taken as code.
            if (IdentifierExpression.IsMatch(value) && (!value.Any(char.IsDigit) || value.IndexOfAny(new[] { '.', ':', '(', '<', '[', '-' }) >= 0))
            {
                return false;
            }

            int classes = 0;
            if (value.Any(char.IsLower))
            {
                classes++;
            }

            if (value.Any(char.IsUpper))
            {
                classes++;
            }

            if (value.Any(char.IsDigit))
            {
                classes++;
            }

            if (value.Any(c => !char.IsLetterOrDigit(c)))
            {
                classes++;
            }

            return (classes >= 3 && value.Length >= 8) || ShannonEntropy(value) >= 3.5;
        }

        internal static double ShannonEntropy(string value)
        {
            var counts = value.GroupBy(c => c).Select(g => (double)g.Count());
            double entropy = 0;
            foreach (var count in counts)
            {
                var p = count / value.Length;
                entropy -= p * Math.Log(p, 2);
            }

            return entropy;
        }
    }
}
