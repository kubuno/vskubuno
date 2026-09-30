using System.Text.Json;

namespace Kubuno.Cargo.Internal
{
    /// <summary>Shared JSON options for everything that talks to Cargo's JSON output.</summary>
    internal static class CargoJsonOptions
    {
        /// <summary>
        /// Snake_case-aware and case-insensitive so explicit <c>JsonPropertyName</c> attributes
        /// are only needed for fields that don't follow the snake_case convention (e.g. the
        /// kebab-case "required-features"). Unknown JSON members are ignored by default, which
        /// is exactly the tolerance Cargo's evolving message formats need.
        /// </summary>
        public static readonly JsonSerializerOptions Default = new JsonSerializerOptions
        {
            PropertyNamingPolicy = SnakeCaseNamingPolicy.Instance,
            PropertyNameCaseInsensitive = true,
        };
    }
}
