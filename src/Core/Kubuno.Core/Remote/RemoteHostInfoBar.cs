using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Kubuno.Core.Logging;
using Kubuno.Core.Logic.Localization;
using Kubuno.Core.Logic.Remote;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace Kubuno.Core.Remote
{
    /// <summary>
    /// The main-window info bar of the remote Linux host (docs/WEB.md, "The development database"): an SSH failure with
    /// its fix (never a modal dialog), and for a host not known yet, its key fingerprint with an "Accept" action that adds
    /// it to the Kubuno-specific known_hosts file. One such bar at a time: a new one replaces the previous.
    /// </summary>
    public static class RemoteHostInfoBar
    {
        private static IVsInfoBarUIElement? _current;

        /// <summary>Shows the failure of <paramref name="result"/> (for an unknown host, after reading its fingerprint). Any thread.</summary>
        public static void ShowFailure(SshTunnelResult result, RemoteHostSettings settings)
        {
            KubunoHost.JoinableTaskFactory.RunAsync(async () =>
            {
                HostKey? key = null;
                string? keyscanError = null;
                if (result.Failure == SshFailureKind.UnknownHostKey)
                {
                    await TaskScheduler.Default;
                    (key, keyscanError) = await ScanAsync(settings).ConfigureAwait(false);
                }

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                Show(result, settings, key, keyscanError);
            }).Task.Forget();
        }

        /// <summary>Closes the bar when it is up (the tunnel works again). UI thread.</summary>
        public static void Close()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _current?.Close();
            _current = null;
        }

        private static void Show(SshTunnelResult result, RemoteHostSettings settings, HostKey? key, string? keyscanError)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var fr = UiLanguage.IsFrench;
            var spans = new List<IVsInfoBarTextSpan> { new InfoBarTextSpan(fr ? "Kubuno : " : "Kubuno: ", true, false, false) };
            var actions = new List<IVsInfoBarActionItem>();
            ImageMoniker icon = KnownMonikers.StatusError;
            var hostPattern = KnownHosts.HostPattern(settings.Host, settings.Port);

            if (result.Failure == SshFailureKind.UnknownHostKey && key?.Fingerprint is { } fingerprint)
            {
                icon = KnownMonikers.StatusSecurityWarning;
                spans.Add(new InfoBarTextSpan(fr
                    ? "l'hôte " + settings.Host + " n'est pas encore connu. Empreinte de sa clé " + key.ShortType + " : "
                    : "the host " + settings.Host + " is not known yet. Fingerprint of its " + key.ShortType + " key: ", false, false, false));
                spans.Add(new InfoBarTextSpan(fingerprint, true, false, false));
                spans.Add(new InfoBarTextSpan(fr
                    ? ". Acceptez-la seulement si elle correspond à celle du serveur (sur l'hôte : ssh-keygen -lf /etc/ssh/ssh_host_" + key.ShortType.ToLowerInvariant() + "_key.pub)."
                    : ". Accept it only if it matches the server's (on the host: ssh-keygen -lf /etc/ssh/ssh_host_" + key.ShortType.ToLowerInvariant() + "_key.pub).", false, false, false));
                actions.Add(new InfoBarButton(fr ? "Accepter" : "Accept", new Action<IVsInfoBarUIElement>(element => Accept(element, settings, key, hostPattern))));
            }
            else
            {
                var message = result.Failure == SshFailureKind.UnknownHostKey
                    ? result.Message + " " + (fr ? "Impossible de lire sa clé (" : "Its key could not be read (") + (keyscanError ?? "ssh-keyscan") + ")."
                    : result.Message;
                spans.Add(new InfoBarTextSpan(message, false, false, false));
                if (result.Failure == SshFailureKind.KeyNotAuthorized && File.Exists(settings.PrivateKeyPath + ".pub"))
                {
                    actions.Add(new InfoBarHyperlink(fr ? "Copier la clé publique" : "Copy the public key", new Action<IVsInfoBarUIElement>(_ => CopyPublicKey(settings))));
                }
            }

            actions.Add(new InfoBarHyperlink(fr ? "Options" : "Options", new Action<IVsInfoBarUIElement>(_ => ShowOptions())));
            actions.Add(new InfoBarHyperlink(fr ? "Journal" : "Log", new Action<IVsInfoBarUIElement>(_ => KubunoLog.Activate())));
            AddToMainWindow(new InfoBarModel(spans, actions, icon, isCloseButtonVisible: true));
        }

        private static void Accept(IVsInfoBarUIElement element, RemoteHostSettings settings, HostKey key, string hostPattern)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var fr = UiLanguage.IsFrench;
            try
            {
                KnownHosts.Accept(settings.KnownHostsPath, key, hostPattern);
                KubunoLog.WriteLine("Kubuno remote: host key " + key.ShortType + " " + key.Fingerprint + " of " + hostPattern + " accepted, added to " + settings.KnownHostsPath + ".");
                element.Close();
                var spans = new List<IVsInfoBarTextSpan>
                {
                    new InfoBarTextSpan(fr ? "Kubuno : " : "Kubuno: ", true, false, false),
                    new InfoBarTextSpan(fr
                        ? "clé de " + settings.Host + " acceptée. Relancez le débogage (F5) pour ouvrir le tunnel."
                        : "key of " + settings.Host + " accepted. Start debugging again (F5) to open the tunnel.", false, false, false),
                };
                AddToMainWindow(new InfoBarModel(spans, Array.Empty<IVsInfoBarActionItem>(), KnownMonikers.StatusOK, isCloseButtonVisible: true));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                KubunoLog.WriteLine("Kubuno remote: could not write " + settings.KnownHostsPath + ": " + exception.Message);
                KubunoLog.Activate();
            }
        }

        private static void CopyPublicKey(RemoteHostSettings settings)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                Clipboard.SetText(File.ReadAllText(settings.PrivateKeyPath + ".pub").Trim());
                KubunoLog.WriteLine("Kubuno remote: public key " + settings.PrivateKeyPath + ".pub copied to the clipboard (add it to ~/.ssh/authorized_keys of " + settings.Destination + ").");
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is System.Runtime.InteropServices.ExternalException)
            {
                KubunoLog.WriteLine("Kubuno remote: could not copy the public key: " + exception.Message);
            }
        }

        private static void ShowOptions()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                KubunoHost.Package?.ShowOptionPage(typeof(RemoteHostOptionsPage));
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is System.Runtime.InteropServices.COMException)
            {
                KubunoLog.WriteLine("Kubuno remote: could not open the options: " + exception.Message);
            }
        }

        private static void AddToMainWindow(InfoBarModel model)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (ServiceProvider.GlobalProvider.GetService(typeof(SVsShell)) is not IVsShell shell
                || ErrorHandler.Failed(shell.GetProperty((int)__VSSPROPID7.VSSPROPID_MainWindowInfoBarHost, out var hostObject))
                || hostObject is not IVsInfoBarHost host
                || ServiceProvider.GlobalProvider.GetService(typeof(SVsInfoBarUIFactory)) is not IVsInfoBarUIFactory factory)
            {
                return;
            }

            _current?.Close();
            var element = factory.CreateInfoBar(model);
            element.Advise(new Events(), out _);
            host.AddInfoBar(element);
            _current = element;
        }

        /// <summary>The host's public keys through <c>ssh-keyscan</c> (no authentication), the preferred one or why there is none.</summary>
        private static async Task<(HostKey? Key, string? Error)> ScanAsync(RemoteHostSettings settings)
        {
            var keyscan = SshCommandLine.DefaultKeyscanExecutable;
            if (!File.Exists(keyscan))
            {
                return (null, keyscan + " not found");
            }

            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo(keyscan, SshCommandLine.ToCommandLine(SshCommandLine.KeyscanArguments(settings)))
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.ASCII,
                    },
                };
                process.Start();
                var reading = process.StandardOutput.ReadToEndAsync();
                var readingErrors = process.StandardError.ReadToEndAsync();
                var output = await reading.ConfigureAwait(false);
                var errors = await readingErrors.ConfigureAwait(false);
                process.WaitForExit(15000);
                var key = KnownHosts.Preferred(KnownHosts.Parse(output));
                return key is null ? (null, string.IsNullOrWhiteSpace(errors) ? "no key returned" : errors.Trim()) : (key, null);
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception || exception is InvalidOperationException || exception is IOException)
            {
                return (null, exception.Message);
            }
        }

        private sealed class Events : IVsInfoBarUIEvents
        {
            public void OnClosed(IVsInfoBarUIElement infoBarUIElement)
            {
                if (ReferenceEquals(_current, infoBarUIElement))
                {
                    _current = null;
                }
            }

            public void OnActionItemClicked(IVsInfoBarUIElement infoBarUIElement, IVsInfoBarActionItem actionItem)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (actionItem.ActionContext is Action<IVsInfoBarUIElement> action)
                {
                    action(infoBarUIElement);
                }
            }
        }
    }
}
