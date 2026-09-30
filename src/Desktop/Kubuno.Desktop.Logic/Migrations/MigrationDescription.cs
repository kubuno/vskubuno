using System.Text;

namespace Kubuno.Desktop.Logic.Migrations
{
    /// <summary>
    /// The rules <c>kubuno-data-tool</c>'s <c>migrate.add</c> applies to a description (<c>migrate.rs</c>
    /// <c>snake_description</c>), checked in the "Add Migration" dialog before the request: 1 to 100 characters, ASCII
    /// letters, digits, spaces, <c>_</c> and <c>-</c>, at least one letter or digit.
    /// </summary>
    public static class MigrationDescription
    {
        public const int MaxLength = 100;

        /// <summary>Why <paramref name="description"/> is refused (localized), or null when it is valid.</summary>
        public static string? Validate(string? description)
        {
            var text = (description ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                return MigrationText.DescriptionEmpty;
            }

            if (text.Length > MaxLength)
            {
                return MigrationText.DescriptionTooLong;
            }

            foreach (var c in text)
            {
                if (!IsAsciiLetterOrDigit(c) && c != ' ' && c != '_' && c != '-')
                {
                    return MigrationText.DescriptionInvalidCharacters;
                }
            }

            return ToSnake(text).Length == 0 ? MigrationText.DescriptionNeedsAlphanumeric : null;
        }

        /// <summary><c>Create  customers-table</c> → <c>create_customers_table</c> (the file name part, as the helper computes it).</summary>
        public static string ToSnake(string description)
        {
            var builder = new StringBuilder();
            foreach (var c in description.Trim())
            {
                if (IsAsciiLetterOrDigit(c))
                {
                    builder.Append(char.ToLowerInvariant(c));
                }
                else if (builder.Length > 0 && builder[builder.Length - 1] != '_')
                {
                    builder.Append('_');
                }
            }

            return builder.ToString().TrimEnd('_');
        }

        /// <summary>The files <c>migrate.add</c> will create, for the dialog's preview (<c>&lt;version&gt;_create_customers.up.sql</c>...).</summary>
        public static string FilesPreview(string description, bool reversible)
        {
            var snake = ToSnake(description);
            if (snake.Length == 0)
            {
                return string.Empty;
            }

            return reversible
                ? $"<version>_{snake}.up.sql, <version>_{snake}.down.sql"
                : $"<version>_{snake}.sql";
        }

        private static bool IsAsciiLetterOrDigit(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
    }
}
