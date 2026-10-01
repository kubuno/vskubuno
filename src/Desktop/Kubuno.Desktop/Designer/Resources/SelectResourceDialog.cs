using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Kubuno.Desktop.Logic.Resources;
using Kubuno.Desktop.Resources;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Desktop.Designer.Resources
{
    /// <summary>
    /// The "Select Resource" dialog of image and icon properties, like Windows Forms' (docs/RESOURCES.md): a
    /// <b>local resource</b> (an image file next to the view, written as its relative path) or a <b>project resource
    /// file</b> entry (written <c>{Res key}</c>), each with Import…, and a preview pane. Importing into a project
    /// resource file adds the picture to that <c>.kbres</c> (linked, copied into its <c>Resources</c> folder when it
    /// comes from outside the project), creating <c>src/resources.kbres</c> when the project has none yet.
    /// </summary>
    internal sealed class SelectResourceDialog : UI.ThemedEditorDialog
    {
        private const int IdYes = 6;
        private const int IdCancel = 2;

        private readonly string? _viewFile;
        private readonly ResourceKindFilter _filter;
        private readonly RadioButton _localRadio;
        private readonly RadioButton _projectRadio;
        private readonly TextBlock _localPath;
        private readonly ComboBox _files;
        private readonly ListView _entries;
        private readonly Image _preview;
        private readonly TextBlock _details;
        private readonly List<string> _neutralFiles = new List<string>();
        private string _local = string.Empty;
        private IReadOnlyList<ResourceItem> _allItems = Array.Empty<ResourceItem>();

        public SelectResourceDialog(string? viewFile, string? current, ResourceKindFilter filter)
            : base(ResourceText.SelectResourceTitle, 720, 480)
        {
            _viewFile = viewFile;
            _filter = filter;
            Result = current;

            _localRadio = new RadioButton { Content = ResourceText.LocalResource, GroupName = "context", Margin = new Thickness(0, 6, 0, 4) };
            _projectRadio = new RadioButton { Content = ResourceText.ProjectResourceFile, GroupName = "context", Margin = new Thickness(0, 10, 0, 4) };
            _localPath = MakeText(string.Empty);
            _localPath.Margin = new Thickness(20, 0, 0, 4);
            _localPath.TextTrimming = TextTrimming.CharacterEllipsis;
            var importLocal = MakeButton(ResourceText.Import);
            var clearLocal = MakeButton(ResourceText.Clear);
            clearLocal.Margin = new Thickness(8, 0, 0, 0);
            var localButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 0, 0, 0) };
            localButtons.Children.Add(importLocal);
            localButtons.Children.Add(clearLocal);

            _files = MakeComboBox(editable: false);
            _files.Margin = new Thickness(20, 0, 0, 6);
            _entries = MakeList();
            _entries.Margin = new Thickness(20, 0, 0, 6);
            var importProject = MakeButton(ResourceText.Import);
            importProject.HorizontalAlignment = HorizontalAlignment.Left;
            importProject.Margin = new Thickness(20, 0, 0, 0);

            var header = MakeText(ResourceText.ResourceContext);
            header.FontWeight = FontWeights.SemiBold;
            var left = new DockPanel { Width = 330, Margin = new Thickness(0, 0, 12, 0), LastChildFill = true };
            foreach (var top in new UIElement[] { header, _localRadio, _localPath, localButtons, _projectRadio, _files })
            {
                DockPanel.SetDock(top, Dock.Top);
                left.Children.Add(top);
            }

            DockPanel.SetDock(importProject, Dock.Bottom);
            left.Children.Add(importProject);
            left.Children.Add(_entries);

            _preview = new Image { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(8) };
            _details = SecondaryText(string.Empty);
            var right = new DockPanel();
            DockPanel.SetDock(_details, Dock.Bottom);
            right.Children.Add(_details);
            right.Children.Add(MakeFrame(_preview));

            var body = new DockPanel();
            DockPanel.SetDock(left, Dock.Left);
            body.Children.Add(left);
            body.Children.Add(right);
            SetBody(body);

            LoadFiles();
            var reference = ResourceNames.ParseReference(current);
            if (reference is { } r)
            {
                var item = ProjectResources.Find(_allItems, current);
                var index = item is null ? -1 : _neutralFiles.FindIndex(f => string.Equals(f, item.NeutralPath, StringComparison.OrdinalIgnoreCase));
                _files.SelectedIndex = index >= 0 ? index : (_files.Items.Count > 0 ? 0 : -1);
                FillEntries(r.Key);
                _projectRadio.IsChecked = true;
            }
            else
            {
                _local = current ?? string.Empty;
                _files.SelectedIndex = _files.Items.Count > 0 ? 0 : -1;
                FillEntries(null);
                // A value that is a path stays local; a new value goes to the project resource file, like Windows Forms.
                _localRadio.IsChecked = !string.IsNullOrEmpty(_local);
                _projectRadio.IsChecked = _localRadio.IsChecked != true;
            }

            _localPath.Text = _local.Length > 0 ? _local : ResourceText.None;
            _files.SelectionChanged += (_, _) => FillEntries(null);
            _entries.SelectionChanged += (_, _) => UpdateState();
            _entries.MouseDoubleClick += (_, _) =>
            {
                if (_entries.SelectedItem is not null && Accept())
                {
                    DialogResult = true;
                }
            };
            _localRadio.Checked += (_, _) => UpdateState();
            _projectRadio.Checked += (_, _) => UpdateState();
            importLocal.Click += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                ImportLocal();
            };
            clearLocal.Click += (_, _) =>
            {
                _local = string.Empty;
                _localPath.Text = ResourceText.None;
                UpdateState();
            };
            importProject.Click += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                ImportIntoProject();
            };
            UpdateState();
        }

        /// <summary>The attribute value chosen: <c>{Res key}</c>, a path relative to the view, or empty (cleared).</summary>
        public string? Result { get; private set; }

        protected override bool Accept()
        {
            if (_localRadio.IsChecked == true)
            {
                Result = _local;
                return true;
            }

            if (_entries.SelectedItem is ListViewItem { Tag: ResourceItem item })
            {
                var qualified = _allItems.Count(i => i.Key == item.Key) > 1;
                Result = item.Reference(qualified);
            }
            else
            {
                Result = string.Empty;
            }

            return true;
        }

        private static TextBlock SecondaryText(string text)
        {
            var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            block.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
            return block;
        }

        private string ProjectRoot => _viewFile is null ? Environment.CurrentDirectory : ProjectResources.ProjectRoot(_viewFile);

        /// <summary>Where a project without resource files gets its first one.</summary>
        private string DefaultNeutralFile => Path.Combine(ProjectRoot, Directory.Exists(Path.Combine(ProjectRoot, "src")) ? "src" : string.Empty, "resources" + ResourceNames.Extension);

        private void LoadFiles()
        {
            _neutralFiles.Clear();
            _files.Items.Clear();
            if (_viewFile is not null)
            {
                _neutralFiles.AddRange(ProjectResources.NeutralFiles(ProjectRoot));
                _allItems = ProjectResources.Items(_viewFile);
            }

            foreach (var f in _neutralFiles)
            {
                _files.Items.Add(Relative(f));
            }

            if (_neutralFiles.Count == 0 && _viewFile is not null)
            {
                _neutralFiles.Add(DefaultNeutralFile);
                _files.Items.Add(ResourceText.NewResourceFile(Relative(DefaultNeutralFile)));
            }
        }

        private string Relative(string file)
        {
            var root = ProjectRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return file.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? file.Substring(root.Length).Replace('\\', '/') : file;
        }

        private bool Accepts(ResourceKind kind) => _filter switch
        {
            ResourceKindFilter.Any => true,
            _ => kind is ResourceKind.Image or ResourceKind.Icon,
        };

        private void FillEntries(string? select)
        {
            _entries.Items.Clear();
            _entries.Items.Add(new ListViewItem { Content = ResourceText.None });
            var file = _files.SelectedIndex >= 0 && _files.SelectedIndex < _neutralFiles.Count ? _neutralFiles[_files.SelectedIndex] : null;
            var items = _allItems.Where(i => file is not null && string.Equals(i.NeutralPath, file, StringComparison.OrdinalIgnoreCase) && Accepts(i.Kind));
            if (_filter == ResourceKindFilter.Icons)
            {
                items = items.OrderBy(i => i.Kind == ResourceKind.Icon ? 0 : 1);
            }

            var index = 0;
            foreach (var item in items)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var thumb = new Image { Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0), Source = ResourcePicker.Decode(item.ReadBytes()) };
                row.Children.Add(thumb);
                row.Children.Add(new TextBlock { Text = item.Key });
                _entries.Items.Add(new ListViewItem { Content = row, Tag = item });
                index++;
                if (select is not null && item.Key == select)
                {
                    _entries.SelectedIndex = index;
                }
            }

            if (_entries.SelectedIndex < 0)
            {
                _entries.SelectedIndex = 0;
            }

            UpdateState();
        }

        private void UpdateState()
        {
            var project = _projectRadio.IsChecked == true;
            _files.IsEnabled = project;
            _entries.IsEnabled = project;
            byte[]? bytes = null;
            string details = string.Empty;
            if (project && _entries.SelectedItem is ListViewItem { Tag: ResourceItem item })
            {
                bytes = item.ReadBytes();
                var size = ResourcePicker.Decode(bytes) is { } bmp ? bmp.PixelWidth + "×" + bmp.PixelHeight : string.Empty;
                details = ResourceText.Details(item.Kind.ToString(), item.Entry.EffectiveFormat, size, item.Entry.Persistence == ResourcePersistence.Embedded ? ResourceText.DetailEmbedded : ResourceText.DetailLinked)
                    + (item.Entry.Comment is { } c ? "\n" + c : string.Empty)
                    + "\n" + item.Reference(_allItems.Count(i => i.Key == item.Key) > 1);
            }
            else if (!project && _local.Length > 0 && PropertyBrowser.ImageResources.Resolve(_viewFile, _local) is { } full)
            {
                try
                {
                    bytes = File.ReadAllBytes(full);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }

                details = _local;
            }

            _preview.Source = ResourcePicker.Decode(bytes);
            _details.Text = _preview.Source is null && ResourcePicker.IsSvg(bytes) ? ResourceText.SvgPreview + "\n" + details : details;
        }

        private Microsoft.Win32.OpenFileDialog FileDialog()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = _filter == ResourceKindFilter.Icons ? ResourceText.IconFilter : ResourceText.ImageFilter, CheckFileExists = true };
            if (_viewFile is not null)
            {
                dialog.InitialDirectory = Path.GetDirectoryName(_viewFile);
            }

            return dialog;
        }

        private void ImportLocal()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dialog = FileDialog();
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            var file = dialog.FileName;
            if (_viewFile is not null && !PropertyBrowser.ImageResources.IsBesideView(_viewFile, file))
            {
                var target = PropertyBrowser.ImageResources.CopyTarget(_viewFile, file);
                var answer = AskYesNoCancel(ResourceText.CopyIntoProject(Path.GetFileName(file), Path.GetDirectoryName(target) ?? string.Empty));
                if (answer == IdCancel)
                {
                    return;
                }

                if (answer == IdYes)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target);
                    file = target;
                }
            }

            _local = _viewFile is null ? file.Replace('\\', '/') : PropertyBrowser.ImageResources.RelativePath(_viewFile, file);
            _localPath.Text = _local;
            _localRadio.IsChecked = true;
            UpdateState();
        }

        private void ImportIntoProject()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_files.SelectedIndex < 0 || _files.SelectedIndex >= _neutralFiles.Count)
            {
                return;
            }

            var dialog = FileDialog();
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            var neutral = _neutralFiles[_files.SelectedIndex];
            try
            {
                var text = File.Exists(neutral) ? File.ReadAllText(neutral) : new KbresFile().ToText();
                var model = new ResourceSetModel(neutral, text, Array.Empty<(string, string)>());
                var entry = model.AddFile(dialog.FileName);
                Directory.CreateDirectory(Path.GetDirectoryName(neutral)!);
                File.WriteAllText(neutral, model.Texts()[neutral]);
                LoadFiles();
                _files.SelectedIndex = Math.Max(0, _neutralFiles.FindIndex(f => string.Equals(f, neutral, StringComparison.OrdinalIgnoreCase)));
                FillEntries(entry.Name);
                _projectRadio.IsChecked = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider, ResourceText.ImportFailed(ex.Message), Title, Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_WARNING, Microsoft.VisualStudio.Shell.Interop.OLEMSGBUTTON.OLEMSGBUTTON_OK, Microsoft.VisualStudio.Shell.Interop.OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
        }
    }
}
