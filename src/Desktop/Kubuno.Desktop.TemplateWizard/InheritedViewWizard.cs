using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using EnvDTE;
using Microsoft.VisualStudio.TemplateWizard;

namespace Kubuno.Desktop.TemplateWizard
{
    /// <summary>
    /// The wizard of the inherited form and inherited user control item templates (visual inheritance, docs/EVENTS.md
    /// "User controls"): Windows Forms' Inheritance Picker - it lists the project's forms (or user controls), and writes
    /// the new view inheriting the chosen one (<c>x:Inherits</c>, an override element for each control of the base the
    /// new view may change) with its code-behind naming the base type; then declares the module like
    /// <see cref="ControlItemWizard"/>.
    /// </summary>
    public sealed class InheritedViewWizard : IWizard
    {
        private readonly List<string> _generatedRustFiles = new List<string>();

        public void RunStarted(object automationObject, Dictionary<string, string> replacementsDictionary, WizardRunKind runKind, object[] customParams)
        {
            if (replacementsDictionary is null)
            {
                return;
            }

            var entered = replacementsDictionary.TryGetValue("$rootname$", out var root) && !string.IsNullOrWhiteSpace(root)
                ? root
                : replacementsDictionary.TryGetValue("$fileinputname$", out var input) ? input : "InheritedForm";
            replacementsDictionary["$classname$"] = ControlItemNames.ClassName(entered);
            replacementsDictionary["$modulename$"] = ControlItemNames.ModuleName(entered);

            var userControl = replacementsDictionary.TryGetValue("$wizarddata$", out var data) && data.IndexOf("user_control", StringComparison.OrdinalIgnoreCase) >= 0;
            var (projectDirectory, targetDirectory) = Directories(automationObject);
            if (projectDirectory is null || targetDirectory is null)
            {
                throw new WizardCancelledException("No project to inherit a view from.");
            }

            var files = InheritedViewNames.ViewFiles(projectDirectory).Select(f => (Path: f, Text: SafeRead(f))).ToList();
            var candidates = InheritedViewNames.Candidates(files, userControl);
            var chosen = InheritancePickerDialog.Ask(replacementsDictionary["$classname$"], candidates.Select(c => c.Path).ToList(), projectDirectory, userControl);
            if (chosen is null)
            {
                throw new WizardCancelledException("No base view chosen.");
            }

            var baseView = candidates.First(c => string.Equals(c.Path, chosen, StringComparison.OrdinalIgnoreCase));
            var baseText = SafeRead(baseView.Path);
            var baseRs = Path.ChangeExtension(baseView.Path, ".rs");
            var typeName = (File.Exists(baseRs) ? InheritedViewNames.BaseTypeName(SafeRead(baseRs), userControl) : null)
                ?? ControlItemNames.ClassName(Path.GetFileNameWithoutExtension(baseView.Path));
            replacementsDictionary["$baseview$"] = InheritedViewNames.RelativePath(targetDirectory, baseView.Path);
            replacementsDictionary["$baseroot$"] = baseView.RootName;
            replacementsDictionary["$overrides$"] = InheritedViewNames.Overrides(baseText);
            replacementsDictionary["$basetypename$"] = typeName;
            replacementsDictionary["$basetype$"] = InheritedViewNames.BaseTypePath(baseView.Path, typeName);
        }

        private static string SafeRead(string path)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return string.Empty;
            }
        }

        /// <summary>The project's folder and the folder the new item goes to (the selected folder, else the project's).</summary>
        private static (string? Project, string? Target) Directories(object automationObject)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            if (automationObject is not DTE dte || dte.SelectedItems is null || dte.SelectedItems.Count == 0)
            {
                return (null, null);
            }

            var selected = dte.SelectedItems.Item(1);
            var project = selected.Project ?? selected.ProjectItem?.ContainingProject;
            var projectDirectory = project?.FullName is { Length: > 0 } full ? Path.GetDirectoryName(full) : null;
            string? target = projectDirectory;
            if (selected.ProjectItem is { } item)
            {
                var path = item.FileNames[1];
                target = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            }

            return (projectDirectory, target);
        }

        public void ProjectItemFinishedGenerating(ProjectItem projectItem)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var path = projectItem?.FileNames[1];
                if (path is not null && path.EndsWith(".rs", StringComparison.OrdinalIgnoreCase))
                {
                    _generatedRustFiles.Add(path);
                }
            }
            catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.COMException)
            {
                // No file name: nothing to declare.
            }
        }

        public void RunFinished()
        {
            foreach (var file in _generatedRustFiles)
            {
                try
                {
                    ControlItemNames.DeclareModuleOnDisk(file);
                    ControlItemNames.AdaptToProjectOnDisk(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The module can still be declared by hand.
                }
            }
        }

        public void ProjectFinishedGenerating(Project project)
        {
        }

        public bool ShouldAddProjectItem(string filePath) => true;

        public void BeforeOpeningFile(ProjectItem projectItem)
        {
        }
    }

    /// <summary>The Inheritance Picker: the views of the project a new view may inherit from.</summary>
    internal static class InheritancePickerDialog
    {
        public static string? Ask(string className, IReadOnlyList<string> views, string projectDirectory, bool userControl)
        {
            var french = string.Equals(System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "fr", StringComparison.OrdinalIgnoreCase);
            var window = new Kubuno.Core.UI.ThemedDialog
            {
                Title = french ? "Sélecteur d'héritage" : "Inheritance Picker",
                Width = 460,
                Height = 400,
                ResizeMode = ResizeMode.NoResize,
            };
            var list = new ListBox { Margin = new Thickness(0, 6, 0, 10) };
            foreach (var view in views)
            {
                list.Items.Add(new ListBoxItem { Content = InheritedViewNames.RelativePath(projectDirectory, view), Tag = view });
            }

            list.SelectedIndex = views.Count > 0 ? 0 : -1;
            var ok = new Button { Content = "OK", IsDefault = true, IsEnabled = views.Count > 0 };
            var cancel = new Button { Content = french ? "Annuler" : "Cancel", IsCancel = true };
            ok.Click += (_, _) => window.DialogResult = true;
            list.MouseDoubleClick += (_, _) => window.DialogResult = list.SelectedItem is not null;
            StackPanel buttons = Kubuno.Core.UI.ThemedControls.ButtonRow(ok, cancel);
            var root = new DockPanel { Margin = new Thickness(12) };
            var kind = userControl ? (french ? "le contrôle utilisateur" : "the user control") : (french ? "le formulaire" : "the form");
            var text = views.Count == 0
                ? (french ? $"Le projet n'a aucun {(userControl ? "contrôle utilisateur" : "formulaire")} dont {className} puisse hériter." : $"The project has no {(userControl ? "user control" : "form")} {className} can inherit from.")
                : (french ? $"Choisissez {kind} dont {className} hérite :" : $"Choose {kind} {className} inherits from:");
            var label = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
            DockPanel.SetDock(label, Dock.Top);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(label);
            root.Children.Add(buttons);
            root.Children.Add(list);
            window.Content = root;
            return window.ShowModal() == true ? (list.SelectedItem as ListBoxItem)?.Tag as string : null;
        }
    }
}
