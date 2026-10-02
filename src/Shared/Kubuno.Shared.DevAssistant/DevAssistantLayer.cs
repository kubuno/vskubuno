using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using Kubuno.Core.DevAssistant.Extensibility;
using Kubuno.Core.DevAssistant.Logic.Changes;
using Kubuno.Core.DevAssistant.Settings;
using Kubuno.Core.DevAssistant.UI;
using Kubuno.Core.Extensibility;
using Kubuno.Core.Logging;
using Kubuno.Core.UI;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Core.DevAssistant
{
    /// <summary>
    /// The Kubuno Dev Assistant's registration in the package (docs/AI-ASSISTANT.md section 9.6): the commands that open
    /// the tool window and its settings, the dialogs of the Dialog Gallery, and - at idle - the layers' startup parts
    /// (e.g. the Desktop layer following the designer's selection for <c>#élément</c>). A Core-layer
    /// <see cref="KubunoLayer"/>: it knows no product layer; they contribute through the MEF contracts of
    /// <see cref="Extensibility"/>.
    /// </summary>
    public sealed class DevAssistantLayer : KubunoLayer
    {
        public override string Name => "DevAssistant";

        public override void InitializeOnUIThread(KubunoLayerContext context)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (context.CommandService is { } commands)
            {
                var show = new OleMenuCommand((_, _) => ShowToolWindow(context), new CommandID(DevAssistantGuids.CommandSet, DevAssistantGuids.ShowToolWindow));
                show.BeforeQueryStatus += (_, _) => show.Text = AssistantText.WindowTitle;
                commands.AddCommand(show);
                var settings = new OleMenuCommand((_, _) => ShowSettings(), new CommandID(DevAssistantGuids.CommandSet, DevAssistantGuids.ShowSettings));
                settings.BeforeQueryStatus += (_, _) => settings.Text = AssistantText.S("Paramètres de l'assistant de développement Kubuno…", "Kubuno Dev Assistant settings…");
                commands.AddCommand(settings);
            }

            DialogGallery.Register(() => GalleryEntries());
        }

        public override void InitializeOnIdle(KubunoLayerContext context)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (context.ComponentModel is null)
            {
                return;
            }

            IEnumerable<IDevAssistantStartup> parts;
            try
            {
                parts = context.ComponentModel.GetExtensions<IDevAssistantStartup>().ToList();
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException("Kubuno Dev Assistant: loading the startup parts failed", exception);
                return;
            }

            foreach (var part in parts)
            {
                try
                {
                    part.Start();
                }
                catch (Exception exception)
                {
                    KubunoLog.WriteException("Kubuno Dev Assistant: a startup part failed", exception);
                }
            }
        }

        private static void ShowToolWindow(KubunoLayerContext context)
        {
            context.JoinableTaskFactory.RunAsync(async () =>
            {
                var window = await context.Package.ShowToolWindowAsync(typeof(DevAssistantToolWindow), 0, create: true, context.DisposalToken);
                if (window?.Frame is null)
                {
                    KubunoLog.WriteLine("Kubuno Dev Assistant: the tool window could not be created.");
                }
            }).FileAndForget("Kubuno/DevAssistant/ShowToolWindow");
        }

        private static void ShowSettings()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var settings = DevAssistantSettings.Load();
            if (new DevAssistantSettingsDialog(settings).ShowModal() == true)
            {
                settings.Save();
            }
        }

        private static IEnumerable<KeyValuePair<string, Func<bool?>>> GalleryEntries()
        {
            bool stored = false;
            var sampleChanges = new ChangeSet();
            sampleChanges.Propose(@"C:\exemple\src\settings_view.kbview", "<Panel>\n  <Label x:Name=\"lang\" Text=\"Langue\"/>\n  <Button x:Name=\"ok\" Text=\"OK\"/>\n</Panel>\n", "<Panel>\n  <Label x:Name=\"lang\" Text=\"Langue\"/>\n  <Label x:Name=\"notif\" Text=\"Notifications\"/>\n  <Switch x:Name=\"notifications\"/>\n  <Button x:Name=\"ok\" Text=\"OK\" Variant=\"Primary\"/>\n</Panel>\n");
            return new[]
            {
                new KeyValuePair<string, Func<bool?>>("Dev Assistant: settings", () => new DevAssistantSettingsDialog(new DevAssistantSettings(), _ => stored, (_, _) => stored = true, _ => !(stored = false)).ShowModal()),
                new KeyValuePair<string, Func<bool?>>("Dev Assistant: data notice", () => new ConsentDialog().ShowModal()),
                new KeyValuePair<string, Func<bool?>>("Dev Assistant: proposed changes", () => new ChangeReviewDialog(sampleChanges).ShowModal()),
                new KeyValuePair<string, Func<bool?>>("Dev Assistant: view request", () => new RequestViewerDialog(new[] { "// Appel 1 → api.anthropic.com\n{\n  \"model\": \"claude-opus-5-5\",\n  \"messages\": [ { \"role\": \"user\", \"content\": \"token = «secret:npm-token#a1b2c3»\" } ]\n}" }).ShowModal()),
            };
        }
    }
}
