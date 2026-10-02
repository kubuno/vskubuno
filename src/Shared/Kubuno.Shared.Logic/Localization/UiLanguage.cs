using System;
using System.Globalization;

namespace Kubuno.Shared.Logic.Localization
{
    /// <summary>
    /// The UI language of the extension's own strings (French or English), picked from Visual Studio's UI culture
    /// (<see cref="CultureInfo.CurrentUICulture"/>, which Visual Studio sets to its installed language pack). Shared by
    /// every layer's small string tables (the designer's DesignerText, the Rust completion and CodeLens texts...).
    /// </summary>
    public static class UiLanguage
    {
        /// <summary>Test seam: forces a language instead of reading the current UI culture.</summary>
        public static bool? ForceFrench { get; set; }

        /// <summary>True when the extension's strings are shown in French.</summary>
        public static bool IsFrench => ForceFrench ?? string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "fr", StringComparison.OrdinalIgnoreCase);
    }
}
