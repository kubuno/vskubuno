using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Kubuno.VisualStudio.Core.Overrides;
using Kubuno.VisualStudio.Core.SolutionExplorer;
using Kubuno.VisualStudio.LanguageService.Overrides;
using Kubuno.VisualStudio.Logging;
using Kubuno.VisualStudio.UI;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.VisualStudio.Commands
{
    /// <summary>
    /// Tools &gt; "Kubuno: Dialog Gallery" - a developer command, shown only in an instance started with
    /// <c>/rootsuffix</c> (the experimental instance): lists every dialog of the extension with sample data and
    /// opens the chosen one, so their theming (<see cref="ThemedDialog"/>, docs/ARCHITECTURE.md "Themed dialogs")
    /// can be re-checked in each Visual Studio theme in a minute. Canonical name for DTE:
    /// <c>Kubuno.DialogGallery</c>.
    /// </summary>
    internal static class DialogGalleryCommand
    {
        public static void Initialize(OleMenuCommandService commandService)
        {
            var id = new CommandID(PackageGuids.KubunoCommandSet, PackageIds.DialogGalleryCommand);
#pragma warning disable VSTHRD010 // OleMenuCommand invoke/query events fire on the UI thread.
            var command = new OleMenuCommand((sender, args) => Show(), id);
            command.BeforeQueryStatus += (sender, args) =>
            {
                var menuCommand = (OleMenuCommand)sender!;
                menuCommand.Visible = IsDeveloperInstance;
            };
#pragma warning restore VSTHRD010
            commandService.AddCommand(command);
        }

        private static bool IsDeveloperInstance =>
            Environment.GetCommandLineArgs().Any(a => a.TrimStart('/', '-').Equals("rootsuffix", StringComparison.OrdinalIgnoreCase));

        /// <summary>Every dialog with a sample: display name and "show it modally".</summary>
        internal static IReadOnlyList<KeyValuePair<string, Func<bool?>>> Entries()
        {
            var entries = new List<KeyValuePair<string, Func<bool?>>>
            {
                new KeyValuePair<string, Func<bool?>>("Ajouter › (NewItemNameDialog)", () => new NewItemNameDialog("Ajouter - Vue Kubuno", "Nom :", "MainView").ShowModal()),
                new KeyValuePair<string, Func<bool?>>(DependenciesText.ReferenceManagerTitle("Sample"), () => new ReferenceManagerDialog("Sample", new[]
                {
                    new ReferenceCandidate("kubuno-ui", @"C:\src\desktop\windows\src\crates\kubuno-ui\Cargo.toml", isReferenced: true),
                    new ReferenceCandidate("kubuno-controls", @"C:\src\desktop\windows\src\crates\kubuno-controls\Cargo.toml", isReferenced: false),
                    new ReferenceCandidate("helper", @"C:\src\helper\Cargo.toml", isReferenced: false),
                }).ShowModal()),
                new KeyValuePair<string, Func<bool?>>(DependenciesText.RemoveUnusedTitle, () => new UnusedDependenciesDialog("cargo-machete", new[] { "itoa", "serde_json", "once_cell" }).ShowModal()),
                // docs/DATA.md DATA-5 (no helper calls from the gallery: Test and OK only validate).
                new KeyValuePair<string, Func<bool?>>(Kubuno.VisualStudio.Core.Data.DataText.AddConnectionTitle + " (AddConnectionDialog)", () => new Kubuno.VisualStudio.DataExplorer.AddConnectionDialog(new[] { "Shop" }, Kubuno.VisualStudio.Core.Data.CredentialStoreKind.CredentialManager, test: null, save: null).ShowModal()),
                // docs/DATA.md DATA-7 (no helper calls from the gallery).
                new KeyValuePair<string, Func<bool?>>(Kubuno.VisualStudio.Core.Migrations.MigrationText.AddMigrationTitle + " (AddMigrationDialog)", () => new Kubuno.VisualStudio.Migrations.AddMigrationDialog("shop-app", "shop").ShowModal()),
                new KeyValuePair<string, Func<bool?>>(Kubuno.VisualStudio.Core.Migrations.MigrationText.ConnectionDialogTitle + " (ConnectionNameDialog)", () => new Kubuno.VisualStudio.Migrations.ConnectionNameDialog(
                    Kubuno.VisualStudio.Core.Migrations.MigrationText.ConnectionDialogIntro("shop-app"), new[] { "Shop", "Sales" }, "Shop", userSecretsId: null, listExplorer: null, copyToSecrets: null).ShowModal()),
                // docs/DATA.md DATA-6 (sample connections and schema; Finish only validates).
                new KeyValuePair<string, Func<bool?>>(Kubuno.VisualStudio.Core.DataSources.DataSourcesText.WizardTitle + " (DataSourceWizardDialog)", () => new Kubuno.VisualStudio.DataSources.DataSourceWizardDialog(
                    new Kubuno.VisualStudio.Core.DataSources.DataSourceWizardModel(Kubuno.VisualStudio.DataSources.SampleDataSourceWizardBackend.Connections, new[] { "shop" }, moduleSchema: null),
                    new Kubuno.VisualStudio.DataSources.SampleDataSourceWizardBackend()).ShowModal()),
            };

            OverrideContext? overrides = OverrideAssistant.Analyze(SampleComponent, SampleComponent.IndexOf("base: Button", StringComparison.Ordinal), OverrideCatalog.Default);
            if (overrides != null)
            {
                entries.Add(new KeyValuePair<string, Func<bool?>>("Substituer des membres…", () => new OverrideMembersDialog(overrides).ShowModal()));
            }

            entries.AddRange(Kubuno.VisualStudio.Designer.UI.DesignerDialogGallery.Entries);
            entries.AddRange(Kubuno.VisualStudio.RustProjectSystem.ProjectProperties.RustProjectSystemDialogGallery.Entries);
            entries.AddRange(Kubuno.VisualStudio.TemplateWizard.TemplateWizardDialogGallery.Entries);
            return entries;
        }

        private const string SampleComponent =
            "use kubuno_views::prelude::*;\n\n#[derive(Component, Default)]\n#[kubuno(extends = Button)]\npub struct RoundButton {\n    base: Button,\n}\n";

        private static void Show()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                new GalleryDialog(Entries()).ShowModal();
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException("Dialog gallery failed", exception);
            }
        }

        /// <summary>One gallery row (its text is also its accessible name).</summary>
        private sealed class GalleryEntry
        {
            public GalleryEntry(string name, Func<bool?> show)
            {
                Name = name;
                Show = show;
            }

            public string Name { get; }

            public Func<bool?> Show { get; }

            public override string ToString() => Name;
        }

        /// <summary>The gallery itself: a themed list of the dialogs; Open (or double-click) shows the selected one.</summary>
        private sealed class GalleryDialog : ThemedDialog
        {
            public GalleryDialog(IReadOnlyList<KeyValuePair<string, Func<bool?>>> entries)
            {
                Title = "Kubuno: Dialog Gallery";
                Width = 460;
                Height = 420;
                ResizeMode = ResizeMode.CanResizeWithGrip;

                var root = new DockPanel { Margin = new Thickness(12) };
                TextBlock hint = ThemedControls.SecondaryText("Every dialog of the extension with sample data. Switch the theme (Tools > Theme) and reopen them to check it.");
                hint.Margin = new Thickness(0, 0, 0, 8);
                DockPanel.SetDock(hint, Dock.Top);
                root.Children.Add(hint);

                var list = new ListBox { ItemsSource = entries.Select(e => new GalleryEntry(e.Key, e.Value)).ToList() };
                System.Windows.Automation.AutomationProperties.SetName(list, "Dialogs");
                var open = new Button { Content = "Open", IsDefault = true };
                var close = new Button { Content = "Close", IsCancel = true };
                void OpenSelected()
                {
                    if (list.SelectedItem is GalleryEntry entry)
                    {
                        try
                        {
                            entry.Show();
                        }
                        catch (Exception exception)
                        {
                            KubunoLog.WriteException("Dialog gallery: " + entry.Name, exception);
                        }
                    }
                }
                open.Click += (_, _) => OpenSelected();
                list.MouseDoubleClick += (_, _) => OpenSelected();
                list.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter)
                    {
                        OpenSelected();
                        e.Handled = true;
                    }
                };
                StackPanel buttons = ThemedControls.ButtonRow(open, close);
                DockPanel.SetDock(buttons, Dock.Bottom);
                root.Children.Add(buttons);
                root.Children.Add(list);
                list.SelectedIndex = 0;
                Content = root;
            }
        }
    }
}
