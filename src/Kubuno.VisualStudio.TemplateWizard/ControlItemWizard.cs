using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using EnvDTE;
using Microsoft.VisualStudio.TemplateWizard;

namespace Kubuno.VisualStudio.TemplateWizard
{
    /// <summary>
    /// The wizard of the Kubuno control item templates (docs/EVENTS.md EVT-7b: Custom Control, User Control, Inherited
    /// Control, Component): names the struct (<c>$classname$</c>, PascalCase) and the module (<c>$modulename$</c>,
    /// snake_case) after the entered name (the files keep it: the "Ajouter" commands pass it in snake_case), asks the base class of an inherited control (<c>$baseclass$</c>), and
    /// declares the new module in the crate root (<c>mod round_button;</c> in <c>src\main.rs</c>) so the control is
    /// compiled into the program - and registered, usable in the views and listed in the Toolbox after the next build.
    /// </summary>
    public sealed class ControlItemWizard : IWizard
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
                : replacementsDictionary.TryGetValue("$fileinputname$", out var input) ? input : "CustomControl";
            replacementsDictionary["$classname$"] = ControlItemNames.ClassName(entered);
            replacementsDictionary["$modulename$"] = ControlItemNames.ModuleName(entered);

            var wizardData = replacementsDictionary.TryGetValue("$wizarddata$", out var data) ? data : string.Empty;
            if (wizardData.IndexOf("inherited", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var chosen = BaseClassDialog.Ask(replacementsDictionary["$classname$"]);
                if (chosen is null)
                {
                    throw new WizardCancelledException("No base class chosen.");
                }

                replacementsDictionary["$baseclass$"] = chosen;
            }
        }

        public void ProjectItemFinishedGenerating(ProjectItem projectItem)
        {
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
                    // The module can still be declared by hand; the template's comment says so.
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

    /// <summary>The small dialog picking the base class of an inherited control.</summary>
    internal static class BaseClassDialog
    {
        public static string? Ask(string className)
        {
            var french = string.Equals(System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "fr", StringComparison.OrdinalIgnoreCase);
            // Themed like Visual Studio's own dialogs (docs/ARCHITECTURE.md, "Themed dialogs"); owned by the IDE's main
            // window through DialogWindow.ShowModal, so it no longer needs Topmost.
            var window = new Kubuno.VisualStudio.UI.ThemedDialog
            {
                Title = french ? "Contrôle hérité Kubuno" : "Kubuno Inherited Control",
                Width = 360,
                Height = 420,
                ResizeMode = ResizeMode.NoResize,
            };
            var list = new ListBox { Margin = new Thickness(0, 6, 0, 10) };
            foreach (var name in ControlItemNames.BaseClasses)
            {
                list.Items.Add(name);
            }

            list.SelectedIndex = 0;
            var ok = new Button { Content = "OK", IsDefault = true };
            var cancel = new Button { Content = french ? "Annuler" : "Cancel", IsCancel = true };
            ok.Click += (_, _) => window.DialogResult = true;
            list.MouseDoubleClick += (_, _) => window.DialogResult = true;
            StackPanel buttons = Kubuno.VisualStudio.UI.ThemedControls.ButtonRow(ok, cancel);
            var root = new DockPanel { Margin = new Thickness(12) };
            var label = new TextBlock { Text = french ? $"Contrôle de base de {className} :" : $"Base control of {className}:" };
            DockPanel.SetDock(label, Dock.Top);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(label);
            root.Children.Add(buttons);
            root.Children.Add(list);
            window.Content = root;
            return window.ShowModal() == true ? list.SelectedItem as string : null;
        }
    }
}
