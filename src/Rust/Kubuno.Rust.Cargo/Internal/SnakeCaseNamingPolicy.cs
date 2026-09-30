using System.Text;
using System.Text.Json;

namespace Kubuno.Cargo.Internal
{
    /// <summary>
    /// Converts PascalCase C# property names to Cargo's snake_case JSON field names
    /// (e.g. "SrcPath" -&gt; "src_path"). A handful of fields that don't follow this rule
    /// (like "required-features", which is kebab-case) carry an explicit
    /// <see cref="System.Text.Json.Serialization.JsonPropertyNameAttribute"/> instead, which
    /// always wins over the naming policy.
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
