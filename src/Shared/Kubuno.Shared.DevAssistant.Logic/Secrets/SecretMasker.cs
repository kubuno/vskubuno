using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Kubuno.Shared.DevAssistant.Logic.Secrets
{
    /// <summary>
    /// Replaces each secret <see cref="SecretScanner"/> finds with a stable placeholder <c>«secret:npm-token#a1b2c3»</c>
    /// (docs/AI-ASSISTANT.md section 8.3). The suffix is derived from the value and a per-conversation salt, so the same
    /// secret is masked identically on every turn and after a restart (history and prompt caching stay byte-identical)
    /// without the value ever being stored. The placeholder → value map lives only in memory, never persisted or sent:
    /// it lets an edit that keeps a placeholder restore the original value when the edit is applied
    /// (<see cref="Restore"/>), and flags placeholders the model invented.
    /// </summary>
    public sealed class SecretMasker
    {
        private static readonly Regex PlaceholderPattern = new Regex(@"«secret:[a-z0-9\-]+#[0-9a-f]{6}»", RegexOptions.CultureInvariant);

        private readonly byte[] _salt;
        private readonly ConcurrentDictionary<string, string> _values = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        /// <param name="salt">The conversation's salt (random, stored with the conversation - see <see cref="NewSalt"/>).</param>
        public SecretMasker(string salt)
        {
            _salt = Encoding.UTF8.GetBytes(salt ?? string.Empty);
        }

        /// <summary>A new random salt for a new conversation.</summary>
        public static string NewSalt()
        {
            var bytes = new byte[16];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }

            return Convert.ToBase64String(bytes);
        }

        /// <summary>How many distinct secrets this masker has seen (and can restore).</summary>
        public int KnownCount => _values.Count;

        /// <summary>The text with every probable secret replaced by its placeholder; <paramref name="count"/> secrets were masked.</summary>
        public string Mask(string? text, out int count)
        {
            count = 0;
            if (string.IsNullOrEmpty(text))
            {
                return text ?? string.Empty;
            }

            var findings = SecretScanner.Scan(text);
            if (findings.Count == 0)
            {
                return text!;
            }

            var builder = new StringBuilder(text!.Length);
            int position = 0;
            foreach (var finding in findings)
            {
                builder.Append(text, position, finding.Start - position);
                builder.Append(PlaceholderFor(finding.Kind, text.Substring(finding.Start, finding.Length)));
                position = finding.End;
                count++;
            }

            builder.Append(text, position, text.Length - position);
            return builder.ToString();
        }

        /// <summary>The text with every probable secret replaced by its placeholder.</summary>
        public string Mask(string? text) => Mask(text, out _);

        /// <summary>The placeholder of <paramref name="value"/> (registering it for <see cref="Restore"/>).</summary>
        public string PlaceholderFor(string kind, string value)
        {
            byte[] hash;
            using (var hmac = new HMACSHA256(_salt.Length == 0 ? new byte[] { 0 } : _salt))
            {
                hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
            }

            var suffix = BitConverter.ToString(hash, 0, 3).Replace("-", string.Empty).ToLowerInvariant();
            var placeholder = "«secret:" + kind + "#" + suffix + "»";
            _values[placeholder] = value;
            return placeholder;
        }

        /// <summary>
        /// Puts the original values back in place of known placeholders (an edit applied to a file); placeholders this
        /// masker never produced are left as they are and reported, so the caller can refuse the hunk instead of writing a
        /// placeholder over a real secret.
        /// </summary>
        public RestoreResult Restore(string? text)
        {
            if (string.IsNullOrEmpty(text) || text!.IndexOf("«secret:", StringComparison.Ordinal) < 0)
            {
                return new RestoreResult(text ?? string.Empty, Array.Empty<string>());
            }

            var unknown = new List<string>();
            var restored = PlaceholderPattern.Replace(text, match =>
            {
                if (_values.TryGetValue(match.Value, out var value))
                {
                    return value;
                }

                unknown.Add(match.Value);
                return match.Value;
            });
            return new RestoreResult(restored, unknown);
        }

        /// <summary>True when <paramref name="text"/> holds a placeholder (masked or invented).</summary>
        public static bool ContainsPlaceholder(string? text) => text is not null && PlaceholderPattern.IsMatch(text);
    }

    /// <summary>The result of <see cref="SecretMasker.Restore"/>.</summary>
    public sealed class RestoreResult
    {
        public RestoreResult(string text, IReadOnlyList<string> unknownPlaceholders)
        {
            Text = text;
            UnknownPlaceholders = unknownPlaceholders;
        }

        public string Text { get; }

        /// <summary>Placeholders this masker cannot restore (invented by the model, or from before a restart).</summary>
        public IReadOnlyList<string> UnknownPlaceholders { get; }

        public bool IsComplete => UnknownPlaceholders.Count == 0;
    }
}
