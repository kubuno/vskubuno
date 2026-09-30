using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kubuno.Desktop.Designer.PropertyBrowser;
using Kubuno.Desktop.Designer.Registry;
using Kubuno.Desktop.Designer.Selection;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Kubuno.Desktop.Designer.UI
{
    /// <summary>
    /// The base of the Properties window's modal editors (image, binding, string list, collection): the extension's shared
    /// <see cref="Kubuno.Core.UI.ThemedDialog"/> (themed-dialog colours, implicit <see cref="VsResourceKeys"/>
    /// styles, themed title bar), so it looks native in the dark, light, blue and high contrast themes, with the
    /// content above an OK/Cancel row at the bottom right, like every Visual Studio dialog.
    /// </summary>
    internal abstract class ThemedEditorDialog : Kubuno.Core.UI.ThemedDialog
    {
        private readonly DockPanel _root;

        protected ThemedEditorDialog(string title, double width, double height)
        {
            Title = title;
            Width = width;
            Height = height;
            MinWidth = Math.Min(width, 320);
            MinHeight = Math.Min(height, 200);
            ResizeMode = ResizeMode.CanResizeWithGrip;
            HasMaximizeButton = false;

            _root = new DockPanel { Margin = new Thickness(12), LastChildFill = true };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = MakeButton(DesignerText.Ok);
            ok.IsDefault = true;
            ok.Margin = new Thickness(0, 0, 8, 0);
            var cancel = MakeButton(DesignerText.Cancel);
            cancel.IsCancel = true;
            ok.Click += (_, _) =>
            {
                if (Accept())
                {
                    DialogResult = true;
                }
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            DockPanel.SetDock(buttons, Dock.Bottom);
            _root.Children.Add(buttons);
            Content = _root;
        }

        /// <summary>Shows the dialog modally over Visual Studio; true when it was accepted.</summary>
        public bool Ask()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return ShowModal() == true;
        }

        /// <summary>The dialog's content, above the OK/Cancel row.</summary>
        protected void SetBody(UIElement body) => _root.Children.Add(body);

        /// <summary>Called on OK: reads the result; false keeps the dialog open.</summary>
        protected virtual bool Accept() => true;

        public static Button MakeButton(string text)
        {
            var button = new Button { Content = text, MinWidth = 80, Padding = new Thickness(10, 2, 10, 2) };
            button.SetResourceReference(StyleProperty, VsResourceKeys.ThemedDialogButtonStyleKey);
            return button;
        }

        public static TextBlock MakeText(string text, bool wrap = false)
        {
            var block = new TextBlock { Text = text, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
            block.SetResourceReference(TextBlock.ForegroundProperty, ThemedDialogColors.WindowPanelTextBrushKey);
            return block;
        }

        public static Label MakeLabel(string text, UIElement? target = null)
        {
            var label = new Label { Content = text, Target = target, Padding = new Thickness(0, 4, 8, 4) };
            label.SetResourceReference(StyleProperty, VsResourceKeys.ThemedDialogLabelStyleKey);
            return label;
        }

        public static TextBox MakeTextBox(string text)
        {
            var box = new TextBox { Text = text };
            box.SetResourceReference(StyleProperty, VsResourceKeys.ThemedDialogTextBoxStyleKey);
            return box;
        }

        public static ComboBox MakeComboBox(bool editable)
        {
            var combo = new ComboBox { IsEditable = editable, IsTextSearchEnabled = true };
            combo.SetResourceReference(StyleProperty, VsResourceKeys.ThemedDialogComboBoxStyleKey);
            return combo;
        }

        public static ListView MakeList()
        {
            var list = new ListView { SelectionMode = SelectionMode.Single };
            list.SetResourceReference(StyleProperty, VsResourceKeys.ThemedDialogListViewStyleKey);
            list.SetResourceReference(ItemsControl.ItemContainerStyleProperty, VsResourceKeys.ThemedDialogListViewItemStyleKey);
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            return list;
        }

        /// <summary>A thin frame in the themed list border colour.</summary>
        public static Border MakeFrame(UIElement child)
        {
            var border = new Border { BorderThickness = new Thickness(1), Child = child };
            border.SetResourceReference(Border.BorderBrushProperty, ThemedDialogColors.ListBoxBorderBrushKey);
            border.SetResourceReference(Border.BackgroundProperty, ThemedDialogColors.ListBoxBrushKey);
            return border;
        }

        /// <summary>Asks a yes/no/cancel question with Visual Studio's own message box: 6 = yes, 7 = no, 2 = cancel.</summary>
        protected int AskYesNoCancel(string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider, text, Title, OLEMSGICON.OLEMSGICON_QUERY, OLEMSGBUTTON.OLEMSGBUTTON_YESNOCANCEL, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }

    /// <summary>The image editor: the project's images, a preview, and Browse (a file outside the view's folder can be copied into its <c>resources</c> folder).</summary>
    internal sealed class ImagePickerDialog : ThemedEditorDialog
    {
        private const int IdYes = 6;
        private const int IdCancel = 2;

        private readonly string? _viewFile;
        private readonly ListView _list;
        private readonly Image _preview;

        public ImagePickerDialog(string? viewFile, string? current)
            : base(DesignerText.ImageEditorTitle, 600, 440)
        {
            _viewFile = viewFile;
            _list = MakeList();
            _list.Items.Add(DesignerText.ImageNone);
            foreach (var image in ImageResources.ProjectImages(viewFile))
            {
                _list.Items.Add(image);
            }

            if (!string.IsNullOrEmpty(current) && !_list.Items.Contains(current!))
            {
                _list.Items.Add(current!);
            }

            _list.SelectedIndex = string.IsNullOrEmpty(current) ? 0 : _list.Items.IndexOf(current!);
            _list.SelectionChanged += (_, _) => ShowPreview();
            _list.MouseDoubleClick += (_, _) =>
            {
                if (_list.SelectedItem is not null && Accept())
                {
                    DialogResult = true;
                }
            };

            var browse = MakeButton(DesignerText.Browse);
            browse.HorizontalAlignment = HorizontalAlignment.Left;
            browse.Margin = new Thickness(0, 8, 0, 0);
            browse.Click += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                BrowseFile();
            };

            var left = new DockPanel { Width = 260, Margin = new Thickness(0, 0, 12, 0) };
            var label = MakeLabel(DesignerText.ImageProjectResources, _list);
            DockPanel.SetDock(label, Dock.Top);
            DockPanel.SetDock(browse, Dock.Bottom);
            left.Children.Add(label);
            left.Children.Add(browse);
            left.Children.Add(_list);

            _preview = new Image { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(8) };
            var body = new DockPanel();
            DockPanel.SetDock(left, Dock.Left);
            body.Children.Add(left);
            body.Children.Add(MakeFrame(_preview));
            SetBody(body);
            Result = current;
            ShowPreview();
        }

        public string? Result { get; private set; }

        protected override bool Accept()
        {
            Result = _list.SelectedIndex <= 0 ? string.Empty : _list.SelectedItem as string;
            return true;
        }

        private void ShowPreview()
        {
            _preview.Source = null;
            if (_list.SelectedIndex > 0 && _list.SelectedItem is string path && ImageResources.Resolve(_viewFile, path) is { } full)
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad; // Never keeps the file locked.
                    bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                    bitmap.UriSource = new Uri(full, UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    _preview.Source = bitmap;
                }
                catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or ArgumentException or InvalidOperationException or FileFormatException or UnauthorizedAccessException)
                {
                }
            }
        }

        private void BrowseFile()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = DesignerText.ImageFilter, CheckFileExists = true };
            if (_viewFile is not null)
            {
                dialog.InitialDirectory = Path.GetDirectoryName(_viewFile);
            }

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            var file = dialog.FileName;
            if (_viewFile is null)
            {
                AddAndSelect(file.Replace('\\', '/'));
                return;
            }

            if (!ImageResources.IsBesideView(_viewFile, file))
            {
                var target = ImageResources.CopyTarget(_viewFile, file);
                var answer = AskYesNoCancel(DesignerText.ImageCopyPrompt(Path.GetFileName(file), Path.GetDirectoryName(target) ?? string.Empty));
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

            AddAndSelect(ImageResources.RelativePath(_viewFile, file));
        }

        private void AddAndSelect(string path)
        {
            if (!_list.Items.Contains(path))
            {
                _list.Items.Add(path);
            }

            _list.SelectedItem = path;
            _list.ScrollIntoView(path);
        }
    }

    /// <summary>The binding editor: the view model field (an editable list, from the language server) and the mode.</summary>
    internal sealed class BindingDialog : ThemedEditorDialog
    {
        private readonly ComboBox _path;
        private readonly ComboBox _mode;

        public BindingDialog(string property, string? current, IReadOnlyList<string> paths)
            : base(DesignerText.BindingEditorTitle + " - " + property, 440, 240)
        {
            var (path, mode) = BindingText.Parse(current);
            _path = MakeComboBox(editable: true);
            foreach (var p in paths)
            {
                _path.Items.Add(p);
            }

            _path.Text = path ?? string.Empty;
            _mode = MakeComboBox(editable: false);
            foreach (var m in BindingText.Modes)
            {
                _mode.Items.Add(m);
            }

            _mode.SelectedItem = mode;
            var help = MakeText(DesignerText.BindingModeDoc(mode), wrap: true);
            help.Margin = new Thickness(0, 8, 0, 0);
            _mode.SelectionChanged += (_, _) => help.Text = DesignerText.BindingModeDoc(_mode.SelectedItem as string ?? "OneWay");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            AddRow(grid, 0, MakeLabel(DesignerText.BindingPath, _path), _path);
            AddRow(grid, 1, MakeLabel(DesignerText.BindingMode, _mode), _mode);
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(help, 2);
            Grid.SetColumn(help, 1);
            grid.Children.Add(help);
            SetBody(grid);
            Result = current;
            Loaded += (_, _) => _path.Focus();
        }

        public string? Result { get; private set; }

        protected override bool Accept()
        {
            Result = BindingText.Build(_path.Text, _mode.SelectedItem as string);
            return true;
        }

        internal static void AddRow(Grid grid, int row, UIElement label, UIElement editor)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(label, row);
            grid.Children.Add(label);
            if (editor is FrameworkElement fe)
            {
                fe.Margin = new Thickness(0, 3, 0, 3);
            }

            Grid.SetRow(editor, row);
            Grid.SetColumn(editor, 1);
            grid.Children.Add(editor);
        }
    }

    /// <summary>"(Advanced)" binding: every property with its binding; selecting one edits it on the right.</summary>
    internal sealed class AdvancedBindingsDialog : ThemedEditorDialog
    {
        private readonly Dictionary<string, string> _results;
        private readonly Dictionary<string, TextBlock> _rows = new Dictionary<string, TextBlock>(StringComparer.Ordinal);
        private readonly ListView _properties;
        private readonly ComboBox _path;
        private readonly ComboBox _mode;
        private bool _loading;

        public AdvancedBindingsDialog(IReadOnlyList<string> properties, IReadOnlyDictionary<string, string> current, IReadOnlyList<string> paths)
            : base(DesignerText.BindingsAdvancedTitle, 620, 420)
        {
            _results = properties.ToDictionary(p => p, p => current.TryGetValue(p, out var b) ? b : string.Empty, StringComparer.Ordinal);
            _properties = MakeList();
            _properties.Width = 240;
            _properties.Margin = new Thickness(0, 0, 12, 0);
            foreach (var name in properties)
            {
                var row = new TextBlock { Tag = name };
                _rows[name] = row;
                Refresh(name);
                _properties.Items.Add(row);
            }

            _path = MakeComboBox(editable: true);
            _path.Items.Add(DesignerText.BindingNone);
            foreach (var p in paths)
            {
                _path.Items.Add(p);
            }

            _mode = MakeComboBox(editable: false);
            foreach (var m in BindingText.Modes)
            {
                _mode.Items.Add(m);
            }

            var right = new Grid { VerticalAlignment = VerticalAlignment.Top };
            right.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            right.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            BindingDialog.AddRow(right, 0, MakeLabel(DesignerText.BindingPath, _path), _path);
            BindingDialog.AddRow(right, 1, MakeLabel(DesignerText.BindingMode, _mode), _mode);

            _properties.SelectionChanged += (_, _) => ShowSelected();
            _path.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => Store()));
            _path.SelectionChanged += (_, _) => Store(_path.SelectedItem as string);
            _mode.SelectionChanged += (_, _) => Store();

            var body = new DockPanel();
            DockPanel.SetDock(_properties, Dock.Left);
            body.Children.Add(_properties);
            body.Children.Add(right);
            SetBody(body);
            if (_properties.Items.Count > 0)
            {
                _properties.SelectedIndex = 0;
            }
        }

        /// <summary>Each property's binding text (empty = none).</summary>
        public IReadOnlyDictionary<string, string> Results => _results;

        private string? SelectedName => (_properties.SelectedItem as TextBlock)?.Tag as string;

        private void Refresh(string name)
        {
            var row = _rows[name];
            var bound = _results[name].Length > 0;
            row.Text = bound ? name + "  " + _results[name] : name;
            row.FontWeight = bound ? FontWeights.Bold : FontWeights.Normal;
        }

        private void ShowSelected()
        {
            if (SelectedName is not { } name)
            {
                return;
            }

            _loading = true;
            var (path, mode) = BindingText.Parse(_results[name]);
            _path.Text = path ?? DesignerText.BindingNone;
            _mode.SelectedItem = mode;
            _loading = false;
        }

        private void Store(string? chosen = null)
        {
            if (_loading || SelectedName is not { } name)
            {
                return;
            }

            // A pick from the list arrives before the combo box shows it as its text.
            var text = chosen ?? _path.Text ?? string.Empty;
            var path = text == DesignerText.BindingNone ? string.Empty : text;
            _results[name] = BindingText.Build(path, _mode.SelectedItem as string);
            Refresh(name);
        }
    }

    /// <summary>The string collection editor: one item per line.</summary>
    internal sealed class StringListDialog : ThemedEditorDialog
    {
        private readonly TextBox _box;

        public StringListDialog(IReadOnlyList<string> lines)
            : base(DesignerText.StringListTitle, 440, 380)
        {
            _box = MakeTextBox(string.Join(Environment.NewLine, lines));
            _box.AcceptsReturn = true;
            _box.AcceptsTab = false;
            _box.TextWrapping = TextWrapping.NoWrap;
            _box.VerticalContentAlignment = VerticalAlignment.Top;
            _box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _box.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            var hint = MakeLabel(DesignerText.StringListHint, _box);
            var body = new DockPanel();
            DockPanel.SetDock(hint, Dock.Top);
            body.Children.Add(hint);
            body.Children.Add(_box);
            SetBody(body);
            Loaded += (_, _) => _box.Focus();
        }

        public IReadOnlyList<string> Lines => (_box.Text ?? string.Empty).Replace("\r\n", "\n").Split('\n');
    }

    /// <summary>
    /// The collection editor, like WinForms': the members on the left (Add, Remove, Up, Down), the selected member's
    /// properties on the right, the description of the focused property under them. <see cref="Members"/> is the edited
    /// collection (existing children keep their index and only the attributes that changed).
    /// </summary>
    internal sealed class CollectionEditorDialog : ThemedEditorDialog
    {
        private readonly string _childTag;
        private readonly ComponentMeta? _childMeta;
        private readonly List<MemberProxy> _members;
        private readonly ListView _list;
        private readonly Grid _properties;
        private readonly TextBlock _description;
        private bool _relabeling;

        public CollectionEditorDialog(string rowName, string childTag, ComponentMeta? childMeta, IReadOnlyList<ViewNode> originals)
            : base(DesignerText.CollectionEditorTitle(rowName), 700, 480)
        {
            _childTag = childTag;
            _childMeta = childMeta;
            _members = originals.Select((node, i) => new MemberProxy(childTag, childMeta, i, node.Attributes.Where(a => a.Value is not null).ToDictionary(a => a.Name, a => a.Value!, StringComparer.Ordinal))).ToList();

            _list = MakeList();
            var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            var add = MakeButton(DesignerText.Add);
            var remove = MakeButton(DesignerText.Remove);
            var up = MakeButton("▲");
            var down = MakeButton("▼");
            up.MinWidth = down.MinWidth = 36;
            up.ToolTip = DesignerText.MoveUp;
            down.ToolTip = DesignerText.MoveDown;
            foreach (var b in new[] { add, remove, up, down })
            {
                b.Margin = new Thickness(0, 0, 6, 0);
                buttons.Children.Add(b);
            }

            var left = new DockPanel { Width = 260, Margin = new Thickness(0, 0, 12, 0) };
            var membersLabel = MakeLabel(DesignerText.CollectionMembers, _list);
            DockPanel.SetDock(membersLabel, Dock.Top);
            DockPanel.SetDock(buttons, Dock.Bottom);
            left.Children.Add(membersLabel);
            left.Children.Add(buttons);
            left.Children.Add(_list);

            _properties = new Grid { Margin = new Thickness(6) };
            _properties.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            _properties.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _properties };
            _description = MakeText(string.Empty, wrap: true);
            _description.Margin = new Thickness(0, 8, 0, 0);
            _description.MinHeight = 36;
            var right = new DockPanel();
            var rightLabel = MakeLabel(DesignerText.CollectionProperties(childTag));
            DockPanel.SetDock(rightLabel, Dock.Top);
            DockPanel.SetDock(_description, Dock.Bottom);
            right.Children.Add(rightLabel);
            right.Children.Add(_description);
            right.Children.Add(MakeFrame(scroller));

            var body = new DockPanel();
            DockPanel.SetDock(left, Dock.Left);
            body.Children.Add(left);
            body.Children.Add(right);
            SetBody(body);

            add.Click += (_, _) =>
            {
                var member = new MemberProxy(_childTag, _childMeta, null, new Dictionary<string, string>(StringComparer.Ordinal));
                member.SeedNew(_members.Count + 1);
                _members.Add(member);
                RefreshList(_members.Count - 1);
            };
            remove.Click += (_, _) =>
            {
                var i = _list.SelectedIndex;
                if (i >= 0)
                {
                    _members.RemoveAt(i);
                    RefreshList(Math.Min(i, _members.Count - 1));
                }
            };
            up.Click += (_, _) => MoveMember(-1);
            down.Click += (_, _) => MoveMember(1);
            _list.SelectionChanged += (_, _) =>
            {
                if (!_relabeling)
                {
                    ShowMember(_list.SelectedIndex >= 0 && _list.SelectedIndex < _members.Count ? _members[_list.SelectedIndex] : null);
                }
            };
            RefreshList(_members.Count > 0 ? 0 : -1);
        }

        /// <summary>The edited collection, for <see cref="ChildCollectionPlanner.Plan"/>.</summary>
        public IReadOnlyList<CollectionMember> Members => _members.Select(m => m.ToMember()).ToList();

        private void MoveMember(int delta)
        {
            var i = _list.SelectedIndex;
            var j = i + delta;
            if (i < 0 || j < 0 || j >= _members.Count)
            {
                return;
            }

            (_members[i], _members[j]) = (_members[j], _members[i]);
            RefreshList(j);
        }

        private void RefreshList(int select)
        {
            _list.Items.Clear();
            foreach (var (member, i) in _members.Select((m, i) => (m, i)))
            {
                _list.Items.Add(i.ToString(CultureInfo.InvariantCulture) + "  " + member.Label);
            }

            if (select >= 0 && select < _list.Items.Count)
            {
                _list.SelectedIndex = select;
            }
            else
            {
                ShowMember(null);
            }
        }

        /// <summary>Relabels the selected member in the list without rebuilding its property rows.</summary>
        private void RelabelSelected()
        {
            var i = _list.SelectedIndex;
            if (i >= 0 && i < _members.Count)
            {
                _relabeling = true;
                _list.Items[i] = i.ToString(CultureInfo.InvariantCulture) + "  " + _members[i].Label;
                _list.SelectedIndex = i;
                _relabeling = false;
            }
        }

        private void ShowMember(MemberProxy? member)
        {
            _properties.Children.Clear();
            _properties.RowDefinitions.Clear();
            _description.Text = string.Empty;
            if (member is null)
            {
                return;
            }

            var row = 0;
            foreach (var descriptor in member.GetProperties().Cast<MemberPropertyDescriptor>())
            {
                var name = MakeText(descriptor.DisplayName);
                name.Margin = new Thickness(0, 3, 8, 3);
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                var value = descriptor.GetValue(member) as string ?? string.Empty;
                FrameworkElement editor;
                if (descriptor.Choices is { } choices)
                {
                    var combo = MakeComboBox(editable: true);
                    foreach (var choice in choices)
                    {
                        combo.Items.Add(choice);
                    }

                    combo.Text = value;
                    combo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => Write(member, descriptor, combo.Text)));
                    combo.SelectionChanged += (_, _) => Write(member, descriptor, combo.SelectedItem as string ?? combo.Text);
                    editor = combo;
                }
                else
                {
                    var box = MakeTextBox(value);
                    box.TextChanged += (_, _) => Write(member, descriptor, box.Text);
                    editor = box;
                }

                editor.GotKeyboardFocus += (_, _) => _description.Text = descriptor.DisplayName + " — " + descriptor.Description;
                BindingDialog.AddRow(_properties, row++, name, editor);
            }
        }

        private void Write(MemberProxy member, MemberPropertyDescriptor descriptor, string text)
        {
            if (string.Equals(descriptor.GetValue(member) as string ?? string.Empty, text, StringComparison.Ordinal))
            {
                return;
            }

            descriptor.SetValue(member, text);
            RelabelSelected();
        }

        /// <summary>One member as the right-hand property list edits it: the child element's own properties, as text.</summary>
        internal sealed class MemberProxy : ICustomTypeDescriptor
        {
            private readonly string _tag;
            private readonly ComponentMeta? _meta;
            private readonly Dictionary<string, string> _original;

            public MemberProxy(string tag, ComponentMeta? meta, int? originalIndex, Dictionary<string, string> attributes)
            {
                _tag = tag;
                _meta = meta;
                OriginalIndex = originalIndex;
                _original = new Dictionary<string, string>(attributes, StringComparer.Ordinal);
                Values = new Dictionary<string, string>(attributes, StringComparer.Ordinal);
            }

            public int? OriginalIndex { get; }

            public Dictionary<string, string> Values { get; }

            /// <summary>What the members list shows: the text/header/label, else the tag.</summary>
            public string Label =>
                new[] { "Text", "Header", "Label", "x:Name", "Value" }.Select(n => Values.TryGetValue(n, out var v) ? v : null).FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? _tag;

            /// <summary>A new member's starting text (<c>Column3</c>), in its label attribute.</summary>
            public void SeedNew(int number)
            {
                var label = _meta?.Properties.Select(p => p.Name).FirstOrDefault(n => n is "Text" or "Header" or "Label");
                if (label is not null)
                {
                    Values[label] = _tag + number.ToString(CultureInfo.InvariantCulture);
                }
            }

            public CollectionMember ToMember()
            {
                if (OriginalIndex is null)
                {
                    return new CollectionMember(null, Values.Where(p => p.Value.Length > 0).Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)));
                }

                var changes = Values.Where(p => !_original.TryGetValue(p.Key, out var o) || o != p.Value)
                    .Select(p => new KeyValuePair<string, string?>(p.Key, p.Value))
                    .Concat(_original.Keys.Where(k => !Values.ContainsKey(k)).Select(k => new KeyValuePair<string, string?>(k, null)));
                return new CollectionMember(OriginalIndex, changes);
            }

            public AttributeCollection GetAttributes() => AttributeCollection.Empty;

            public string GetClassName() => _tag;

            public string? GetComponentName() => null;

            public TypeConverter GetConverter() => new TypeConverter();

            public EventDescriptor? GetDefaultEvent() => null;

            public PropertyDescriptor? GetDefaultProperty() => null;

            public object? GetEditor(Type editorBaseType) => null;

            public EventDescriptorCollection GetEvents() => EventDescriptorCollection.Empty;

            public EventDescriptorCollection GetEvents(Attribute[]? attributes) => EventDescriptorCollection.Empty;

            public PropertyDescriptorCollection GetProperties() => GetProperties(null);

            public PropertyDescriptorCollection GetProperties(Attribute[]? attributes)
            {
                var names = new List<(string Name, PropertyMeta? Meta)> { ("x:Name", null) };
                names.AddRange((_meta?.Properties ?? new List<PropertyMeta>()).Where(p => p.Browsable && !p.RootOnly && p.InheritedFrom is null).Select(p => (p.Name, (PropertyMeta?)p)));
                return new PropertyDescriptorCollection(names.Select(n => (PropertyDescriptor)new MemberPropertyDescriptor(n.Name, n.Meta)).ToArray(), readOnly: true);
            }

            public object GetPropertyOwner(PropertyDescriptor? pd) => this;
        }

        internal sealed class MemberPropertyDescriptor : PropertyDescriptor
        {
            private readonly PropertyMeta? _meta;

            public MemberPropertyDescriptor(string name, PropertyMeta? meta)
                : base(name, new Attribute[] { new DescriptionAttribute(meta?.LocalizedDoc ?? DesignerText.NameDescription), new DisplayNameAttribute(name == "x:Name" ? "(Name)" : name) })
            {
                _meta = meta;
            }

            public override Type ComponentType => typeof(MemberProxy);

            public override bool IsReadOnly => false;

            public override Type PropertyType => typeof(string);

            /// <summary>The values offered in the drop-down (a switch or a list of choices), else null.</summary>
            public IReadOnlyList<string>? Choices => _meta?.Kind.Tag switch
            {
                PropKindTag.Bool => new[] { "true", "false" },
                PropKindTag.Enum => _meta.Kind.EnumVariants,
                _ => null,
            };

            public override TypeConverter Converter => _meta?.Kind.Tag switch
            {
                PropKindTag.Bool => new AttributeValuesConverter(new[] { "true", "false" }),
                PropKindTag.Enum => new AttributeValuesConverter(_meta.Kind.EnumVariants),
                _ => new StringConverter(),
            };

            public override object GetValue(object? component) =>
                component is MemberProxy m && m.Values.TryGetValue(Name, out var v) ? v : _meta?.Default ?? string.Empty;

            public override void SetValue(object? component, object? value)
            {
                if (component is not MemberProxy m)
                {
                    return;
                }

                var text = (value as string ?? string.Empty).Trim();
                if (text.Length == 0)
                {
                    m.Values.Remove(Name);
                    return;
                }

                m.Values[Name] = _meta is null ? AttributeValueRules.NormalizeName(text) : RichEditors.Normalize(_meta, _meta.Kind, text);
            }

            public override bool CanResetValue(object component) => component is MemberProxy m && m.Values.ContainsKey(Name);

            public override void ResetValue(object component)
            {
                if (component is MemberProxy m)
                {
                    m.Values.Remove(Name);
                }
            }

            public override bool ShouldSerializeValue(object component) => component is MemberProxy m && m.Values.ContainsKey(Name);
        }
    }
}
