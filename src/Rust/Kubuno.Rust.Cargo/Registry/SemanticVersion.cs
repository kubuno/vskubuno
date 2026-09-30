using System;
using System.Collections.Generic;
using System.Globalization;

namespace Kubuno.Cargo.Registry
{
    /// <summary>
    /// A SemVer 2.0 version as crates use it (<c>1.0.229</c>, <c>0.4.0-alpha.2</c>, <c>1.2.3+build</c>),
    /// ordered by SemVer precedence (build metadata ignored) - enough to tell the latest version of a
    /// crate and whether a resolved version is outdated, without a SemVer library dependency.
    /// </summary>
    public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
    {
        private readonly string[] _preRelease;

        private SemanticVersion(long major, long minor, long patch, string[] preRelease, string original)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            _preRelease = preRelease;
            Original = original;
        }

        public long Major { get; }

        public long Minor { get; }

        public long Patch { get; }

        public bool IsPreRelease => _preRelease.Length > 0;

        public string Original { get; }

        public static bool TryParse(string? text, out SemanticVersion version)
        {
            version = null!;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var value = text!.Trim();
            var plus = value.IndexOf('+');
            var core = plus >= 0 ? value.Substring(0, plus) : value;
            var dash = core.IndexOf('-');
            var numbers = dash >= 0 ? core.Substring(0, dash) : core;
            var pre = dash >= 0 ? core.Substring(dash + 1).Split('.') : Array.Empty<string>();
            var parts = numbers.Split('.');
            if (parts.Length != 3
                || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
                || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
                || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
            {
                return false;
            }

            version = new SemanticVersion(major, minor, patch, pre, value);
            return true;
        }

        public static SemanticVersion? ParseOrNull(string? text) => TryParse(text, out var version) ? version : null;

        /// <summary>The greatest of <paramref name="versions"/> (parsable ones only), pre-releases only if <paramref name="includePreRelease"/>.</summary>
        public static SemanticVersion? Max(IEnumerable<string> versions, bool includePreRelease)
        {
            SemanticVersion? best = null;
            foreach (var text in versions)
            {
                if (TryParse(text, out var version) && (includePreRelease || !version.IsPreRelease)
                    && (best is null || version.CompareTo(best) > 0))
                {
                    best = version;
                }
            }

            return best;
        }

        public int CompareTo(SemanticVersion? other)
        {
            if (other is null)
            {
                return 1;
            }

            var c = Major.CompareTo(other.Major);
            if (c == 0)
            {
                c = Minor.CompareTo(other.Minor);
            }

            if (c == 0)
            {
                c = Patch.CompareTo(other.Patch);
            }

            if (c != 0)
            {
                return c;
            }

            // A pre-release has lower precedence than the release itself.
            if (_preRelease.Length == 0 || other._preRelease.Length == 0)
            {
                return _preRelease.Length == other._preRelease.Length ? 0 : (_preRelease.Length == 0 ? 1 : -1);
            }

            for (var i = 0; i < Math.Min(_preRelease.Length, other._preRelease.Length); i++)
            {
                var a = _preRelease[i];
                var b = other._preRelease[i];
                var aNumeric = long.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out var an);
                var bNumeric = long.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out var bn);
                if (aNumeric && bNumeric)
                {
                    c = an.CompareTo(bn);
                }
                else if (aNumeric != bNumeric)
                {
                    // Numeric identifiers sort before alphanumeric ones.
                    c = aNumeric ? -1 : 1;
                }
                else
                {
                    c = string.CompareOrdinal(a, b);
                }

                if (c != 0)
                {
                    return c;
                }
            }

            return _preRelease.Length.CompareTo(other._preRelease.Length);
        }

        public bool Equals(SemanticVersion? other) => other is not null && CompareTo(other) == 0;

        public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)Major * 397 ^ (int)Minor * 31 ^ (int)Patch;
                foreach (var part in _preRelease)
                {
                    hash = hash * 31 + StringComparer.Ordinal.GetHashCode(part);
                }

                return hash;
            }
        }

        public override string ToString() => Original;
    }
}
