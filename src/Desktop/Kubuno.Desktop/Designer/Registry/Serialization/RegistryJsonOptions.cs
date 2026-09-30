using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kubuno.Desktop.Designer.Registry.Serialization
{
    /// <summary>Shared <see cref="JsonSerializerOptions"/> for reading a <c>kubuno/registry</c> export (docs/DESIGNER.md §5).</summary>
    public static class RegistryJsonOptions
    {
        /// <summary>
        /// Snake_case field names (Rust's own convention, see <see cref="SnakeCaseNamingPolicy"/>) plus
        /// case-insensitive matching so a hand-written fixture that drifts slightly in casing still
        /// loads; <see cref="PropKindJsonConverter"/> for <see cref="PropKind"/>'s polymorphic shape; and
        /// a plain <see cref="JsonStringEnumConverter"/> for <see cref="ChildrenModel"/>/
        /// <see cref="LayoutKind"/>, which works with no extra naming policy because both enums' C#
        /// member names are spelled exactly like the Rust variants they mirror (their own doc comments
        /// say so).
        /// </summary>
        public static readonly JsonSerializerOptions Default = new JsonSerializerOptions
        {
            PropertyNamingPolicy = SnakeCaseNamingPolicy.Instance,
            PropertyNameCaseInsensitive = true,
            Converters = { new PropKindJsonConverter(), new JsonStringEnumConverter() },
        };
    }
}
