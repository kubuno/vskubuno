using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Kubuno.Views.Logic.Resources;
using Kubuno.Views.Logging;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Views.Designer.DesignSurface
{
    /// <summary>The <c>setResources</c> message (see <c>RustDesignSurfaceHost</c>'s resources part).</summary>
    public static class DesignSurfaceResourcesProtocol
    {
        private static readonly JsonSerializerOptions Wire = new JsonSerializerOptions();

        /// <summary>
        /// The design culture a designer opens with: the project's culture matching Windows' UI culture <paramref name="ui"/>
        /// (<c>fr-FR</c>, else its language <c>fr</c>, else another region of it), <c>""</c> (the neutral values) when the
        /// project has none - the language the application starts in on this machine.
        /// </summary>
        public static string DefaultDesignCulture(IEnumerable<string> projectCultures, System.Globalization.CultureInfo ui)
        {
            var cultures = projectCultures?.ToList() ?? new List<string>();
            var name = ui?.Name ?? string.Empty;
            var language = ui?.TwoLetterISOLanguageName ?? string.Empty;
            return cultures.FirstOrDefault(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase))
                ?? cultures.FirstOrDefault(c => string.Equals(c, language, StringComparison.OrdinalIgnoreCase))
                ?? cultures.FirstOrDefault(c => c.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase))
                ?? string.Empty;
        }

        /// <summary>
        /// <c>setResources {culture, sets: [{name, baseDir, neutral, satellites: [{culture, text}]}]}</c> for the neutral files
        /// <paramref name="neutralFiles"/> and their satellites beside them; <paramref name="readText"/> reads a file (tests).
        /// </summary>
        public static string EncodeSetResources(string culture, IEnumerable<string> neutralFiles, Func<string, string?>? readText = null, Func<string, IEnumerable<string>>? listFiles = null)
        {
            readText ??= p =>
            {
                try
                {
                    return File.ReadAllText(p);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return null;
                }
            };
            var sets = new List<object>();
            foreach (var neutral in neutralFiles)
            {
                if (readText(neutral) is not { } text)
                {
                    continue;
                }

                var satellites = ResourceNames.Satellites(neutral, listFiles)
                    .Select(s => (s.Culture, Text: readText(s.Path)))
                    .Where(s => s.Text is not null)
                    .Select(s => new { culture = s.Culture, text = s.Text })
                    .ToList();
                sets.Add(new
                {
                    name = ResourceNames.SplitFileName(Path.GetFileName(neutral))?.Stem ?? Path.GetFileNameWithoutExtension(neutral),
                    baseDir = Path.GetDirectoryName(neutral) ?? string.Empty,
                    neutral = text,
                    satellites,
                });
            }

            return JsonSerializer.Serialize(new { type = "setResources", culture = culture ?? string.Empty, sets }, Wire);
        }
    }
}
