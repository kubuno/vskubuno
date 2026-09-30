using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Kubuno.Rust.Commands
{
    /// <summary>
    /// The five item-template entries of the extended "Ajouter" submenu on a <c>.rsproj</c> project
    /// node (docs/RSPROJ.md addendum "lot 7" shipped the templates themselves; this wires the
    /// WinForms-style direct entries - "Formulaire (Windows Forms)...", "Contrôle utilisateur...",
    /// etc. - that jump straight to one preselected template instead of the developer picking it
    /// from the generic "Ajouter &gt; Nouvel élément..." tree). Each prompts for a name
    /// (<see cref="NewItemNameDialog"/>) and adds the corresponding item template directly via
    /// <see cref="EnvDTE.ProjectItems.AddFromTemplate"/>, pointed at this extension's own installed,
    /// loose <c>.vstemplate</c> file (the same file the packaged Add New Item catalog is built from -
    /// no separate copy to keep in sync).
    ///
    /// This intentionally does not reopen the classic <c>IVsAddProjectItemDlg2</c> browsable dialog:
    /// live-verified in the experimental instance (both via this extension's own commands and via
    /// the stock "Ajouter &gt; Nouvel élément..." item), Visual Studio 2026 itself no longer uses that
    /// dialog for a project-scoped Add Item gesture either - its own "Nouvel élément..." now opens a
    /// small single-line name prompt, not a browsable template catalog. Reviving the classic COM
    /// dialog only got stuck forever on "Chargement des modèles..." (confirmed even after a full
    /// template-cache rebuild - restart-once, delete `ExtensionMetadata*.mpack`, restart again), so
    /// this instead matches the *current* stock command's own shape with a themed equivalent.
    ///
    /// The entries come from the layers: the Rust layer adds the plain Rust items (module, integration test, example,
    /// binary), a target layer adds its own (the desktop layer: Kubuno views, controls, components), each with a
    /// command ID of <c>KubunoCommands.vsct</c>'s "Ajouter" submenu.
    /// </summary>
    public static class AddProjectItemCommands
    {
        /// <summary>Wires one command per template (UI thread).</summary>
        public static void Initialize(OleMenuCommandService commandService, IEnumerable<ProjectItemTemplate> templates)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            foreach (var template in templates)
            {
                AddCommand(commandService, template);
            }
        }

        private static void AddCommand(OleMenuCommandService commandService, ProjectItemTemplate template)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var id = new CommandID(PackageGuids.KubunoCommandSet, template.CommandId);
#pragma warning disable VSTHRD100, VSTHRD010 // both handlers always fire on the UI thread (OleMenuCommand invoke/query events), like GenerateRustProjectsCommand's own suppression.
            var command = new OleMenuCommand((sender, args) => Execute(template), id);
            command.BeforeQueryStatus += (sender, args) => OnBeforeQueryStatus((OleMenuCommand)sender!);
#pragma warning restore VSTHRD100, VSTHRD010
            commandService.AddCommand(command);
        }

        private static void OnBeforeQueryStatus(OleMenuCommand command)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            // Belt and braces on top of KubunoCommands.vsct's own VisibilityConstraints
            // (guidRustProjectUIContext): only enabled when a .rsproj is genuinely selected, so a
            // stale cached menu state (or a selection change the UIContext rule has not yet caught
            // up with) never fires against the wrong project.
            command.Visible = command.Enabled = RsprojSelection.TryGetCurrent() is not null;
        }

        private static void Execute(ProjectItemTemplate template)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var context = RsprojSelection.TryGetCurrent();
            if (context is null)
            {
                return;
            }

            var templatePath = TryResolveTemplatePath(template.FolderName);
            if (templatePath is null)
            {
                VsShellUtilities.ShowMessageBox(
                    ServiceProvider.GlobalProvider,
                    $"Le modèle \"{template.DisplayName}\" est introuvable dans le dossier d'installation de l'extension.",
                    "Kubuno",
                    OLEMSGICON.OLEMSGICON_WARNING,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                return;
            }

            var dialog = new NewItemNameDialog(template.DisplayName, template.PromptLabel, template.DefaultName);
            if (dialog.ShowModal() != true || string.IsNullOrEmpty(dialog.EnteredName))
            {
                return;
            }

            var fileName = template.FileStem is { } fileStem
                ? fileStem(dialog.EnteredName) + template.PrimaryExtension
                : dialog.EnteredName.EndsWith(template.PrimaryExtension, StringComparison.OrdinalIgnoreCase)
                    ? dialog.EnteredName
                    : dialog.EnteredName + template.PrimaryExtension;

            try
            {
                var items = template.IntoSourceFolder ? SourceFolderItems(context.Project) ?? context.Project.ProjectItems : context.Project.ProjectItems;
                items.AddFromTemplate(templatePath, fileName);
            }
            catch (Exception exception)
            {
                Kubuno.Core.Logging.KubunoLog.WriteException($"Kubuno: adding \"{fileName}\" from the {template.DisplayName} template", exception);
                VsShellUtilities.ShowMessageBox(
                    ServiceProvider.GlobalProvider,
                    $"Impossible d'ajouter \"{fileName}\" - voir le panneau de sortie \"Kubuno\" pour le détail.",
                    "Kubuno",
                    OLEMSGICON.OLEMSGICON_WARNING,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
        }

        /// <summary>
        /// This extension's own <c>ItemTemplates\&lt;folder&gt;\&lt;folder&gt;.vstemplate</c>, loose
        /// on disk next to the running assembly (the same file the VSIX's packaged
        /// <c>Microsoft.VisualStudio.ItemTemplate</c> catalog asset is built from at build time -
        /// verified present post-deploy in docs/RSPROJ.md's own addendum).
        /// </summary>
        /// <summary>The items of the project's <c>src</c> folder (the crate root's folder), when it has one.</summary>
        private static EnvDTE.ProjectItems? SourceFolderItems(EnvDTE.Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                foreach (EnvDTE.ProjectItem item in project.ProjectItems)
                {
                    if (string.Equals(item.Name, "src", StringComparison.OrdinalIgnoreCase) && item.ProjectItems is { } children)
                    {
                        return children;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // No folder list: the project root.
            }

            return null;
        }

        private static string? TryResolveTemplatePath(string folderName)
        {
            try
            {
                var installDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(installDirectory))
                {
                    return null;
                }

                var path = Path.Combine(installDirectory!, "ItemTemplates", folderName, folderName + ".vstemplate");
                return File.Exists(path) ? path : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>One entry of the extended "Ajouter" submenu of a <c>.rsproj</c>: a loose item template of the VSIX.</summary>
    public sealed class ProjectItemTemplate
    {
        public ProjectItemTemplate(int commandId, string folderName, string displayName, string promptLabel, string defaultName, string primaryExtension, Func<string, string>? fileStem = null, bool intoSourceFolder = false)
        {
            CommandId = commandId;
            FolderName = folderName;
            DisplayName = displayName;
            PromptLabel = promptLabel;
            DefaultName = defaultName;
            PrimaryExtension = primaryExtension;
            FileStem = fileStem;
            IntoSourceFolder = intoSourceFolder;
        }

        /// <summary>The command ID (<c>KubunoCommands.vsct</c>), in <see cref="Kubuno.Core.KubunoGuids.CommandSet"/>.</summary>
        public int CommandId { get; }

        /// <summary>The template's own folder name under <c>ItemTemplates\</c> - also its <c>.vstemplate</c> file's base name.</summary>
        public string FolderName { get; }

        public string DisplayName { get; }

        public string PromptLabel { get; }

        public string DefaultName { get; }

        /// <summary>
        /// The extension of the file whose name identifies the whole template to
        /// <c>AddFromTemplate</c> (e.g. <c>.kbview</c> for Kubuno View, even though it also
        /// creates a same-stem <c>.rs</c> code-behind - <c>$fileinputname$</c> is derived from
        /// this one file name).
        /// </summary>
        public string PrimaryExtension { get; }

        /// <summary>
        /// The file name (without extension) for the entered name, when the template names its files itself (the
        /// desktop control templates, docs/EVENTS.md EVT-7b: <c>RoundButton</c> becomes <c>round_button.rs</c>); null
        /// to use the entered name.
        /// </summary>
        public Func<string, string>? FileStem { get; }

        /// <summary>The files go to the crate's <c>src</c> folder (where a control template's wizard declares the module).</summary>
        public bool IntoSourceFolder { get; }
    }
}