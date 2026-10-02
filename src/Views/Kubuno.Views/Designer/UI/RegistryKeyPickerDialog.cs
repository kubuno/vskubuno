using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using Kubuno.Shared.Logic.Localization;
using Kubuno.Views.Logic.Settings;
using Microsoft.Win32;

// A WPF dialog: every member runs on the UI thread that shows it.
#pragma warning disable VSTHRD010

namespace Kubuno.Views.Designer.UI
{
    /// <summary>
    /// The Registry key picker of a <c>&lt;RegistryKey&gt;</c>'s <c>Path</c> (docs/STORAGE-COMPONENTS.md §5.2): the hive and
    /// the WOW64 view, a tree of the real Registry's keys (read-only, loaded as they are expanded), the selected key's
    /// values with their types, and the path, which can also be typed or pasted (<c>HKLM\SOFTWARE\…</c> selects the
    /// hive too). Nothing is ever written to the Registry.
    /// </summary>
    internal sealed class RegistryKeyPickerDialog : ThemedEditorDialog
    {
        private const int MaxChildren = 5000;
        private const int MaxValues = 500;

        private readonly ComboBox _hive;
        private readonly ComboBox _view;
        private readonly TreeView _tree;
        private readonly ListView _values;
        private readonly TextBox _path;
        private bool _syncing;

        private static string T(string english, string french) => UiLanguage.IsFrench ? french : english;

        public RegistryKeyPickerDialog(string hive, string view, string path)
            : base(T("Select a Registry Key", "Sélectionner une clé du Registre"), 820, 560)
        {
            _hive = MakeComboBox(editable: false);
            foreach (var h in RegistryKeyPath.Hives)
            {
                _hive.Items.Add(h.Name);
            }

            _hive.SelectedItem = RegistryKeyPath.HiveName(hive) ?? "CurrentUser";
            _view = MakeComboBox(editable: false);
            foreach (var v in RegistryKeyPath.Views)
            {
                _view.Items.Add(v);
            }

            _view.SelectedItem = RegistryKeyPath.Views.Contains(view) ? view : "Default";
            _tree = new TreeView();
            _tree.SetResourceReference(StyleProperty, Microsoft.VisualStudio.Shell.VsResourceKeys.ThemedDialogTreeViewStyleKey);
            System.Windows.Automation.AutomationProperties.SetName(_tree, T("Keys", "Clés"));
            _values = MakeList();
            var grid = new GridView();
            grid.Columns.Add(new GridViewColumn { Header = T("Name", "Nom"), DisplayMemberBinding = new System.Windows.Data.Binding("Name"), Width = 160 });
            grid.Columns.Add(new GridViewColumn { Header = T("Type", "Type"), DisplayMemberBinding = new System.Windows.Data.Binding("Type"), Width = 110 });
            grid.Columns.Add(new GridViewColumn { Header = T("Data", "Données"), DisplayMemberBinding = new System.Windows.Data.Binding("Data"), Width = 220 });
            _values.View = grid;
            System.Windows.Automation.AutomationProperties.SetName(_values, T("Values", "Valeurs"));
            _path = MakeTextBox(RegistryKeyPath.Parse(path).Path);

            var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            top.Children.Add(MakeLabel(T("Hive:", "Ruche :"), _hive));
            _hive.MinWidth = 150;
            top.Children.Add(_hive);
            var viewLabel = MakeLabel(T("View:", "Vue :"), _view);
            viewLabel.Margin = new Thickness(16, 0, 0, 0);
            top.Children.Add(viewLabel);
            _view.MinWidth = 120;
            top.Children.Add(_view);
            top.Children.Add(new TextBlock { Width = 16 });
            top.Children.Add(MakeText(T("Read-only: nothing is written to the Registry.", "Lecture seule : rien n'est écrit dans le Registre.")));

            var middle = new Grid();
            middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            var treeFrame = MakeFrame(_tree);
            Grid.SetColumn(treeFrame, 0);
            middle.Children.Add(treeFrame);
            var valuesFrame = MakeFrame(_values);
            Grid.SetColumn(valuesFrame, 2);
            middle.Children.Add(valuesFrame);

            var bottom = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var pathLabel = MakeLabel(T("Path:", "Chemin :"), _path);
            DockPanel.SetDock(pathLabel, Dock.Left);
            bottom.Children.Add(pathLabel);
            bottom.Children.Add(_path);

            var body = new DockPanel();
            DockPanel.SetDock(top, Dock.Top);
            DockPanel.SetDock(bottom, Dock.Bottom);
            body.Children.Add(top);
            body.Children.Add(bottom);
            body.Children.Add(middle);
            SetBody(body);

            _hive.SelectionChanged += (_, _) => Rebuild(string.Empty);
            _view.SelectionChanged += (_, _) => Rebuild(_path.Text);
            _tree.SelectedItemChanged += (_, _) => OnSelected();
            _path.LostKeyboardFocus += (_, _) => OnPathTyped();
            _path.KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    OnPathTyped();
                    e.Handled = true;
                }
            };
            Loaded += (_, _) => Rebuild(_path.Text);
        }

        /// <summary>The chosen hive (<c>CurrentUser</c>…).</summary>
        public string Hive => _hive.SelectedItem as string ?? "CurrentUser";

        /// <summary>The chosen WOW64 view.</summary>
        public string View => _view.SelectedItem as string ?? "Default";

        /// <summary>The chosen path below the hive.</summary>
        public string Path => RegistryKeyPath.Parse(_path.Text).Path;

        private RegistryKey? OpenBase()
        {
            var hive = Hive switch
            {
                "LocalMachine" => RegistryHive.LocalMachine,
                "ClassesRoot" => RegistryHive.ClassesRoot,
                "Users" => RegistryHive.Users,
                "CurrentConfig" => RegistryHive.CurrentConfig,
                _ => RegistryHive.CurrentUser,
            };
            var view = View switch
            {
                "Registry32" => Microsoft.Win32.RegistryView.Registry32,
                "Registry64" => Microsoft.Win32.RegistryView.Registry64,
                _ => Microsoft.Win32.RegistryView.Default,
            };
            try
            {
                return RegistryKey.OpenBaseKey(hive, view);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or System.IO.IOException)
            {
                return null;
            }
        }

        private static RegistryKey? Open(RegistryKey root, string path)
        {
            try
            {
                return path.Length == 0 ? root : root.OpenSubKey(path, writable: false);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or System.IO.IOException)
            {
                return null;
            }
        }

        private static IReadOnlyList<string> SubKeys(RegistryKey? key)
        {
            if (key is null)
            {
                return Array.Empty<string>();
            }

            try
            {
                return key.GetSubKeyNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Take(MaxChildren).ToArray();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or System.IO.IOException)
            {
                return Array.Empty<string>();
            }
        }

        private TreeViewItem MakeItem(string name, string path)
        {
            var item = new TreeViewItem { Header = name, Tag = path };
            item.SetResourceReference(StyleProperty, Microsoft.VisualStudio.Shell.VsResourceKeys.ThemedDialogTreeViewItemStyleKey);
            item.Items.Add(new TreeViewItem { Header = "…", Tag = null });
            item.Expanded += (_, _) => Fill(item);
            return item;
        }

        /// <summary>Loads the children of <paramref name="item"/> the first time it is expanded.</summary>
        private void Fill(TreeViewItem item)
        {
            if (!(item.Items.Count == 1 && item.Items[0] is TreeViewItem { Tag: null }))
            {
                return;
            }

            item.Items.Clear();
            using var root = OpenBase();
            if (root is null)
            {
                return;
            }

            var path = (string)item.Tag;
            var key = Open(root, path);
            foreach (var name in SubKeys(key))
            {
                item.Items.Add(MakeItem(name, RegistryKeyPath.Join(path, name)));
            }

            if (!ReferenceEquals(key, root))
            {
                key?.Dispose();
            }
        }

        /// <summary>Rebuilds the tree for the hive and view, expanded down to <paramref name="path"/>.</summary>
        private void Rebuild(string path)
        {
            _tree.Items.Clear();
            var rootItem = MakeItem(RegistryKeyPath.ShortName(Hive), string.Empty);
            _tree.Items.Add(rootItem);
            rootItem.IsExpanded = true;
            var current = rootItem;
            foreach (var segment in RegistryKeyPath.Segments(path))
            {
                Fill(current);
                var next = current.Items.OfType<TreeViewItem>().FirstOrDefault(i => string.Equals((string)i.Header, segment, StringComparison.OrdinalIgnoreCase));
                if (next is null)
                {
                    break;
                }

                current = next;
                current.IsExpanded = true;
            }

            _syncing = true;
            current.IsSelected = true;
            current.BringIntoView();
            _syncing = false;
            _path.Text = RegistryKeyPath.Parse(path).Path;
            ShowValues((string)current.Tag);
        }

        private void OnSelected()
        {
            if (_tree.SelectedItem is not TreeViewItem { Tag: string path })
            {
                return;
            }

            if (!_syncing)
            {
                _path.Text = path;
            }

            ShowValues(path);
        }

        private void OnPathTyped()
        {
            var (hive, path) = RegistryKeyPath.Parse(_path.Text);
            if (hive is not null && hive != Hive)
            {
                _hive.SelectedItem = hive;
            }

            Rebuild(path);
        }

        private sealed class ValueRow
        {
            public string Name { get; set; } = string.Empty;

            public string Type { get; set; } = string.Empty;

            public string Data { get; set; } = string.Empty;
        }

        private void ShowValues(string path)
        {
            var rows = new List<ValueRow>();
            using (var root = OpenBase())
            {
                var key = root is null ? null : Open(root, path);
                if (key is not null)
                {
                    try
                    {
                        foreach (var name in key.GetValueNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Take(MaxValues))
                        {
                            var kind = key.GetValueKind(name);
                            var data = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                            rows.Add(new ValueRow { Name = name.Length == 0 ? T("(Default)", "(par défaut)") : name, Type = TypeName(kind), Data = Text(data) });
                        }
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or System.IO.IOException)
                    {
                        rows.Add(new ValueRow { Name = T("(access denied)", "(accès refusé)") });
                    }

                    if (!ReferenceEquals(key, root))
                    {
                        key.Dispose();
                    }
                }
            }

            _values.ItemsSource = rows;
        }

        private static string TypeName(RegistryValueKind kind) => kind switch
        {
            RegistryValueKind.String => "REG_SZ",
            RegistryValueKind.ExpandString => "REG_EXPAND_SZ",
            RegistryValueKind.MultiString => "REG_MULTI_SZ",
            RegistryValueKind.DWord => "REG_DWORD",
            RegistryValueKind.QWord => "REG_QWORD",
            RegistryValueKind.Binary => "REG_BINARY",
            _ => kind.ToString(),
        };

        private static string Text(object? data) => data switch
        {
            null => string.Empty,
            string s => s,
            string[] lines => string.Join(" | ", lines),
            byte[] bytes => BitConverter.ToString(bytes.Take(32).ToArray()).Replace("-", " ") + (bytes.Length > 32 ? " …" : string.Empty),
            _ => Convert.ToString(data, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }
}
