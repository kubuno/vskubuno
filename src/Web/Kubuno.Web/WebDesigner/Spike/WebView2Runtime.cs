using System;
using System.IO;
using System.Threading.Tasks;
using Kubuno.Shared.Logging;
using Kubuno.Web.Logic.WebDesigner;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.Web.WebView2.Core;

namespace Kubuno.Web.WebDesigner.Spike
{
    /// <summary>
    /// SPIKE (docs/WEB-VIEWS.md, WV-9a, question 6): the WebView2 runtime and the one <see cref="CoreWebView2Environment"/>
    /// every design surface of this Visual Studio process shares (one browser process, one profile in
    /// <c>%LOCALAPPDATA%\Kubuno\webview2\&lt;hive&gt;</c> - never Visual Studio's own WebView2 folder). The WebView2
    /// assemblies are Visual Studio's own (<c>PrivateAssemblies</c>, not shipped in the VSIX); the browser is the Evergreen
    /// runtime installed with Windows 11.
    /// </summary>
    internal static class WebView2Runtime
    {
        private static Task<CoreWebView2Environment>? s_environment;

        /// <summary>The runtime folder forced by <see cref="WebDesignSpikeConstants.BrowserFolderVariable"/> (tests), else null (the installed Evergreen runtime).</summary>
        private static string? BrowserFolder =>
            Environment.GetEnvironmentVariable(WebDesignSpikeConstants.BrowserFolderVariable) is { Length: > 0 } folder ? folder : null;

        /// <summary>The installed runtime's version, or null with <paramref name="error"/> saying why WebView2 cannot run.</summary>
        public static string? AvailableVersion(out string? error)
        {
            try
            {
                var version = CoreWebView2Environment.GetAvailableBrowserVersionString(BrowserFolder);
                error = string.IsNullOrEmpty(version) ? "no WebView2 runtime was found" : null;
                return string.IsNullOrEmpty(version) ? null : version;
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                error = "the WebView2 runtime is not installed (" + ex.Message + ")";
                return null;
            }
            catch (Exception ex) when (ex is FileNotFoundException or DllNotFoundException or BadImageFormatException or System.Runtime.InteropServices.COMException or ArgumentException)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return null;
            }
        }

        /// <summary>The shared environment (created on first use, on the UI thread). A failed creation is not cached: the next pane retries.</summary>
        public static async Task<CoreWebView2Environment> EnvironmentAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (s_environment is not { IsFaulted: false, IsCanceled: false })
            {
                var folder = UserDataFolder();
                Directory.CreateDirectory(folder);
                KubunoLog.WriteLine($"[web-spike] WebView2 environment: user data folder {folder}" + (BrowserFolder is { } b ? ", runtime folder " + b : string.Empty));
                s_environment = CoreWebView2Environment.CreateAsync(BrowserFolder, folder, new CoreWebView2EnvironmentOptions());
            }

            return await s_environment;
        }

        /// <summary><c>%LOCALAPPDATA%\Kubuno\webview2\&lt;hive&gt;</c> (<see cref="WebView2Folders"/>).</summary>
        public static string UserDataFolder()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string? root = null;
            if (Package.GetGlobalService(typeof(SVsShell)) is IVsShell shell &&
                shell.GetProperty((int)__VSSPROPID.VSSPROPID_VirtualRegistryRoot, out var value) == 0)
            {
                root = value as string;
            }

            return WebView2Folders.UserDataFolder(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), root);
        }
    }
}
