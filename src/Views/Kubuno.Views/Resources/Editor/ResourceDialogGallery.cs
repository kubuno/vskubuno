using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using Kubuno.Core.UI;
using Kubuno.Desktop.Logic.Resources;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Desktop.Resources.Editor
{
    /// <summary>
    /// The resource editor with an in-memory sample set, for Kubuno.VisualStudio's "Kubuno: Dialog Gallery" developer
    /// command (docs/ARCHITECTURE.md, "Themed dialogs"): re-checking the editor in each Visual Studio theme.
    /// Add <c>Entries</c> to the gallery's provider list (DesktopLayer: <c>entries.AddRange(ResourceDialogGallery.Entries)</c>).
    /// </summary>
    public static class ResourceDialogGallery
    {
        public static IReadOnlyList<KeyValuePair<string, Func<bool?>>> Entries { get; } = new[]
        {
            new KeyValuePair<string, Func<bool?>>(ResourceText.ResourceEditorGallery, () => new SampleDialog().ShowModal()),
            // The Select Resource dialog on the resources sample (an image property of a view of that project).
            new KeyValuePair<string, Func<bool?>>(ResourceText.SelectResourceTitle + " (SelectResourceDialog)", () => new Kubuno.Desktop.Designer.Resources.SelectResourceDialog(SampleView, "{Res logo}", Kubuno.Desktop.Designer.Resources.ResourceKindFilter.Images).ShowModal()),
        };

        /// <summary>A view of the resources sample (samples/resources-desktop of the extension's repository; KUBUNO_RESOURCES_SAMPLE overrides it).</summary>
        private static string SampleView => Path.Combine(Environment.GetEnvironmentVariable("KUBUNO_RESOURCES_SAMPLE") ?? Path.Combine("Z:" + Path.DirectorySeparatorChar, "src", "vskubuno", "samples", "resources-desktop"), "src", "main_view.kbview");

        /// <summary>A file system where nothing exists on disk: the sample only embeds data.</summary>
        private sealed class MemoryFileSystem : IResourceFileSystem
        {
            public bool FileExists(string path) => false;

            public byte[] ReadAllBytes(string path) => throw new FileNotFoundException(path);

            public void WriteAllBytes(string path, byte[] bytes)
            {
            }

            public void CopyFile(string from, string to)
            {
            }
        }

        private sealed class SampleHost : IResourceEditorHost
        {
            public void OpenFile(string path)
            {
            }

            public void ShowError(string message)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider, message, ResourceText.EditorTitle, Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_WARNING, Microsoft.VisualStudio.Shell.Interop.OLEMSGBUTTON.OLEMSGBUTTON_OK, Microsoft.VisualStudio.Shell.Interop.OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }

            public bool Confirm(string message)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                return VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider, message, ResourceText.EditorTitle, Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_QUERY, Microsoft.VisualStudio.Shell.Interop.OLEMSGBUTTON.OLEMSGBUTTON_YESNO, Microsoft.VisualStudio.Shell.Interop.OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST) == 6;
            }

            public void ViewCode()
            {
            }
        }

        private sealed class SampleDialog : ThemedDialog
        {
            public SampleDialog()
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                Title = ResourceText.EditorTitle;
                Width = 960;
                Height = 560;
                ResizeMode = ResizeMode.CanResize;

                var model = new ResourceSetModel(@"C:\sample\Strings.kbres", new KbresFile().ToText(), new (string, string)[0], new MemoryFileSystem());
                model.AddString("welcome_text", "Welcome to Kubuno", "The greeting");
                model.AddString("ok_button.text", "OK");
                model.AddString("cancel_button.text", "Cancel");
                model.AddText(ResourceKind.Color, "accent", "#3366FF");
                model.AddCulture("fr");
                model.AddCulture("de");
                model.SetValue("welcome_text", "fr", "Bienvenue dans Kubuno");
                model.SetValue("ok_button.text", "fr", "OK");
                model.SetValue("welcome_text", "de", "Willkommen bei Kubuno");

                var view = new ResourceEditorView(new SampleHost());
                view.SetModel(model);
                Content = view;
            }
        }
    }
}
