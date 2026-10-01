using System;
using System.IO;
using System.Linq;

namespace Kubuno.Web.Logic.WebDesigner
{
    /// <summary>
    /// Where the web design surface's WebView2 keeps its profile (cookies, cache, local storage): never Visual Studio's
    /// own WebView2 folder, and one folder per Visual Studio hive so that an experimental instance never shares (and
    /// locks) the regular instance's browser profile - <c>%LOCALAPPDATA%\Kubuno\webview2\&lt;hive&gt;</c>.
    /// </summary>
    public static class WebView2Folders
    {
        /// <summary>
        /// The hive name of a Visual Studio registry root (<c>VSSPROPID_VirtualRegistryRoot</c>, e.g.
        /// <c>Software\Microsoft\VisualStudio\18.0_6f0e0a8bKubunoWV</c> -&gt; <c>18.0_6f0e0a8bKubunoWV</c>), reduced to
        /// characters safe in a folder name; <c>default</c> when unknown.
        /// </summary>
        public static string HiveName(string? registryRoot)
        {
            var last = (registryRoot ?? string.Empty).TrimEnd('\\', '/').Split('\\', '/').LastOrDefault() ?? string.Empty;
            var safe = new string(last.Where(c => char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-').ToArray());
            return safe.Length == 0 ? "default" : safe;
        }

        /// <summary>The user data folder: <c>&lt;localAppData&gt;\Kubuno\webview2\&lt;hive&gt;</c>.</summary>
        public static string UserDataFolder(string localAppData, string? registryRoot)
        {
            if (string.IsNullOrEmpty(localAppData))
            {
                throw new ArgumentException("The local application data folder is required.", nameof(localAppData));
            }

            return Path.Combine(localAppData, "Kubuno", "webview2", HiveName(registryRoot));
        }
    }
}
