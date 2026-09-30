using System;
using System.Collections.Generic;

namespace Kubuno.Desktop.Designer.Registry
{
    /// <summary>Discriminant for <see cref="PropKind"/> - see that type's own doc comment.</summary>
    public enum PropKindTag
    {
        Bool,
        F32,
        String,
        Enum,
    }

    /// <summary>
    /// Mirrors <c>kubuno_views::registry::PropKind</c> (docs/DESIGNER.md §5: "kind serializes PropKind
    /// as 'Bool'/'F32'/'String'/{'Enum': [...]}"): the three scalar variants are plain JSON strings, and
    /// <c>Enum</c> alone carries data (its variant list), so it round-trips as a one-key object. This is
    /// a Rust-style closed sum type reimplemented as a class with a <see cref="Tag"/> discriminant
    /// because C# has no native tagged union - Properties/PropertyRowViewModel.cs and
    /// PropertyBrowser/AttributeValueRules.cs switch on <see cref="Tag"/> exactly the way Rust code
    /// would <c>match</c> on the real enum. See <see cref="Serialization.PropKindJsonConverter"/> for the
    /// read/write logic.
    /// </summary>
    public sealed class PropKind : IEquatable<PropKind>
    {
        private static readonly IReadOnlyList<string> NoVariants = Array.Empty<string>();

        public static readonly PropKind Bool = new PropKind(PropKindTag.Bool, NoVariants);
        public static readonly PropKind F32 = new PropKind(PropKindTag.F32, NoVariants);
        public static readonly PropKind String = new PropKind(PropKindTag.String, NoVariants);

        private PropKind(PropKindTag tag, IReadOnlyList<string> enumVariants)
        {
            Tag = tag;
            EnumVariants = enumVariants;
        }

        /// <summary>Which of the four variants this value is.</summary>
        public PropKindTag Tag { get; }

        /// <summary>
        /// The exact variant list for an <see cref="PropKindTag.Enum"/> kind (the same list the language
        /// server already offers as completion, per §1); empty for the three scalar kinds.
        /// </summary>
        public IReadOnlyList<string> EnumVariants { get; }

        public static PropKind CreateEnum(IReadOnlyList<string> variants)
        {
            if (variants is null)
            {
                throw new ArgumentNullException(nameof(variants));
            }

            return new PropKind(PropKindTag.Enum, variants);
        }

        public bool Equals(PropKind? other)
        {
            if (other is null)
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (Tag != other.Tag)
            {
                return false;
            }

            if (Tag != PropKindTag.Enum)
            {
                return true;
            }

            if (EnumVariants.Count != other.EnumVariants.Count)
            {
                return false;
            }

            for (int i = 0; i < EnumVariants.Count; i++)
            {
                if (!string.Equals(EnumVariants[i], other.EnumVariants[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj) => Equals(obj as PropKind);

        public override int GetHashCode()
        {
            // Manual combine, not System.HashCode: that type needs the Microsoft.Bcl.HashCode
            // polyfill package on net48, which nothing in this library references.
            unchecked
            {
                int hash = 17 * 31 + (int)Tag;
                if (Tag == PropKindTag.Enum)
                {
                    foreach (var variant in EnumVariants)
                    {
                        hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(variant);
                    }
                }

                return hash;
            }
        }

        public override string ToString() =>
            Tag == PropKindTag.Enum ? $"Enum({string.Join(", ", EnumVariants)})" : Tag.ToString();
    }
}
