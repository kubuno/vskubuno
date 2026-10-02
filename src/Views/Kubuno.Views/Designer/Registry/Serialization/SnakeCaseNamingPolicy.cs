using System.Text;
using System.Text.Json;

namespace Kubuno.Views.Designer.Registry.Serialization
{
    /// <summary>
    /// Converts PascalCase C# property names to Rust's own snake_case field names (e.g.
    /// "LayoutKind" -&gt; "layout_kind"). A local copy of the exact same policy
    /// <c>Kubuno.Rust.Cargo.Internal.SnakeCaseNamingPolicy</c> already uses for Cargo's own JSON - not a
    /// shared reference, since this library and <c>Kubuno.Rust.Cargo</c> don't reference each other, but the
    /// same convention: Rust identifiers (both here and Cargo's) are snake_case by convention, and
    /// <c>serde</c> serializes a struct field using its literal Rust name unless the struct carries an
    /// explicit <c>#[serde(rename...)]</c> - which docs/DESIGNER.md §5's registry export does not.
    /// </summary>
    internal sealed class SnakeCaseNamingPolicy : JsonNamingPolicy
    {
        public static readonly SnakeCaseNamingPolicy Instance = new SnakeCaseNamingPolicy();

        private SnakeCaseNamingPolicy()
        {
        }

        public override string ConvertName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }

            var builder = new StringBuilder(name.Length + 8);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (char.IsUpper(c))
                {
                    if (i > 0)
                    {
                        builder.Append('_');
                    }

                    builder.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }
    }
}
