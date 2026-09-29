using System;
using System.Collections.Generic;

namespace Kubuno.VisualStudio.RustProjectSystem.ProjectProperties
{
    /// <summary>
    /// This assembly's dialogs with sample data, for Kubuno.VisualStudio's "Kubuno: Dialog Gallery" developer
    /// command (docs/ARCHITECTURE.md, "Themed dialogs"): re-checking every dialog in each Visual Studio theme.
    /// </summary>
    public static class RustProjectSystemDialogGallery
    {
        /// <summary>Display name and "show it modally" for each dialog (UI thread).</summary>
        public static IReadOnlyList<KeyValuePair<string, Func<bool?>>> Entries { get; } = new[]
        {
            new KeyValuePair<string, Func<bool?>>(PropertiesText.TargetsTitle, () => new RustTargetsDialog().ShowModal()),
            new KeyValuePair<string, Func<bool?>>(PropertiesText.LaunchTitle, () => new RustLaunchProfileDialog(new RustLaunchProfile
            {
                Executable = @"C:\src\hello\target\debug\hello.exe",
                Arguments = "--verbose",
                WorkingDirectory = @"C:\src\hello",
                Environment = "RUST_LOG=debug\r\nKUBUNO_THEME=dark",
                Backtrace = true,
            }).ShowModal()),
        };
    }
}
