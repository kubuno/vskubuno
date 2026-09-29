using System;
using System.Security;
using System.Text;

namespace Kubuno.Cargo.Win32
{
    /// <summary>Generates an application manifest (RT_MANIFEST payload) from <see cref="Win32ManifestOptions"/>.</summary>
    public static class Win32ManifestBuilder
    {
        private const string Win10 = "{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}";
        private const string Win81 = "{1f676c76-80e1-4239-95bb-83d0f6d0da78}";
        private const string Win8 = "{4a2f28e3-53b9-4441-ba9c-d69d4a4a6e38}";
        private const string Win7 = "{35138b9a-5d96-4fbd-8e2d-a2440225f93a}";

        /// <summary>Builds the manifest XML text.</summary>
        public static string Build(Win32ManifestOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n");
            sb.Append("<assembly xmlns=\"urn:schemas-microsoft-com:asm.v1\" manifestVersion=\"1.0\">\n");

            if (!string.IsNullOrEmpty(options.AssemblyName))
            {
                sb.Append("  <assemblyIdentity type=\"win32\" name=\"").Append(Esc(options.AssemblyName!))
                  .Append("\" version=\"").Append(Esc(options.AssemblyVersion)).Append("\" processorArchitecture=\"amd64\"/>\n");
            }

            if (options.CommonControlsV6)
            {
                sb.Append("  <dependency>\n    <dependentAssembly>\n");
                sb.Append("      <assemblyIdentity type=\"win32\" name=\"Microsoft.Windows.Common-Controls\" version=\"6.0.0.0\" processorArchitecture=\"*\" publicKeyToken=\"6595b64144ccf1df\" language=\"*\"/>\n");
                sb.Append("    </dependentAssembly>\n  </dependency>\n");
            }

            string level;
            switch (options.ExecutionLevel)
            {
                case Win32ExecutionLevel.HighestAvailable: level = "highestAvailable"; break;
                case Win32ExecutionLevel.RequireAdministrator: level = "requireAdministrator"; break;
                default: level = "asInvoker"; break;
            }

            sb.Append("  <trustInfo xmlns=\"urn:schemas-microsoft-com:asm.v3\">\n    <security>\n      <requestedPrivileges>\n");
            sb.Append("        <requestedExecutionLevel level=\"").Append(level).Append("\" uiAccess=\"false\"/>\n");
            sb.Append("      </requestedPrivileges>\n    </security>\n  </trustInfo>\n");

            sb.Append("  <compatibility xmlns=\"urn:schemas-microsoft-com:compatibility.v1\">\n    <application>\n");
            Win32MinimumWindows min = options.MinimumWindows;
            if (min <= Win32MinimumWindows.Windows7) sb.Append("      <supportedOS Id=\"").Append(Win7).Append("\"/>\n");
            if (min <= Win32MinimumWindows.Windows8) sb.Append("      <supportedOS Id=\"").Append(Win8).Append("\"/>\n");
            if (min <= Win32MinimumWindows.Windows81) sb.Append("      <supportedOS Id=\"").Append(Win81).Append("\"/>\n");
            sb.Append("      <supportedOS Id=\"").Append(Win10).Append("\"/>\n");
            sb.Append("    </application>\n  </compatibility>\n");

            string dpiAware;
            string? dpiAwareness;
            switch (options.DpiAwareness)
            {
                case Win32DpiAwareness.PerMonitorV2:
                    dpiAware = "true/pm";
                    dpiAwareness = "PerMonitorV2, PerMonitor";
                    break;
                case Win32DpiAwareness.PerMonitor:
                    dpiAware = "true/pm";
                    dpiAwareness = "PerMonitor";
                    break;
                case Win32DpiAwareness.System:
                    dpiAware = "true";
                    dpiAwareness = null;
                    break;
                default:
                    dpiAware = "false";
                    dpiAwareness = null;
                    break;
            }

            sb.Append("  <application xmlns=\"urn:schemas-microsoft-com:asm.v3\">\n    <windowsSettings>\n");
            sb.Append("      <dpiAware xmlns=\"http://schemas.microsoft.com/SMI/2005/WindowsSettings\">").Append(dpiAware).Append("</dpiAware>\n");
            if (dpiAwareness != null)
            {
                sb.Append("      <dpiAwareness xmlns=\"http://schemas.microsoft.com/SMI/2016/WindowsSettings\">").Append(dpiAwareness).Append("</dpiAwareness>\n");
            }

            if (options.LongPathAware)
            {
                sb.Append("      <longPathAware xmlns=\"http://schemas.microsoft.com/SMI/2016/WindowsSettings\">true</longPathAware>\n");
            }

            if (options.UseUtf8CodePage)
            {
                sb.Append("      <activeCodePage xmlns=\"http://schemas.microsoft.com/SMI/2019/WindowsSettings\">UTF-8</activeCodePage>\n");
            }

            sb.Append("    </windowsSettings>\n  </application>\n");
            sb.Append("</assembly>\n");
            return sb.ToString();
        }

        private static string Esc(string s) => SecurityElement.Escape(s) ?? string.Empty;
    }
}
