using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Kubuno.Shared.UI;
using Kubuno.Views.Designer.PropertyBrowser;
using Kubuno.Views.Designer.UI;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Views.Designer.Icons
{
    /// <summary>
    /// The icon picker (docs/ICONS.md), opened by every icon property of the Properties window (<c>Icon</c>,
    /// <c>SmallIcon</c>/<c>LargeIcon</c>, a window's <c>Icon</c>...) - Visual Studio's Select Resource dialog, for icons:
    /// <list type="bullet">
    /// <item><b>Kubuno icons</b>: the whole icon set as a searchable gallery (name and keywords), by set (Lucide, the Kubuno
    /// themed icons, the module logos) or the recently used ones;</item>
    /// <item><b>Project images</b>: the SVG, PNG, ICO... files of the project, Import... (a file outside the view's folder is
    /// copied into its <c>resources</c> folder, like WinForms' Import), and the project's resource files when the resources
    /// editor is there;</item>
    /// <item>the chosen icon previewed at 16, 20, 24 and 32 pixels on the light and the dark theme - an image file rendered by
    /// the runtime's own code - and No icon.</item>
    /// </list>
    /// The value is what the attribute gets: a glyph name (<c>Save</c>), a path relative to the view, or <c>{Res key}</c>.
    /// </summary>
    internal sealed class IconPickerDialog : ThemedEditorDialog
    {
        private const int IdYes = 6;
        private const int IdCancel = 2;
        private static readonly Color LightInk = Color.FromRgb(0x20, 0x21, 0x24);
        private static readonly Color DarkInk = Color.FromRgb(0xe8, 0xea, 0xed);
        private static readonly Color LightAccent = Color.FromRgb(0x1a, 0x73, 0xe8);
        private static readonly Color DarkAccent = Color.FromRgb(0x8a, 0xb4, 0xf8);
        private static readonly int[] PreviewSizes = { 16, 20, 24, 32 };

        private readonly string? _viewFile;
        private readonly IKbviewIconServices? _services;
        private readonly IconCatalog _catalog;
        private readonly RecentIcons _recent;
        private readonly TextBox _search;
        private readonly ComboBox _set;
        private readonly ListBox _gallery;
        private readonly TextBlock _count;
        private readonly ListBox _files;
        private readonly TextBox _value;
        private readonly StackPanel _lightRow;
        private readonly StackPanel _darkRow;
        private readonly TextBlock _caption;
        private readonly TabControl _tabs;
        private readonly Color _ink;
        private bool _syncing;

        public IconPickerDialog(string? viewFile, string? current, IKbviewIconServices? services, RecentIcons? recent = null)
            : base(IconText.PickerTitle, 820, 580)
        {
            _viewFile = viewFile;
            _services = services;
            _recent = recent ?? RecentIcons.Default;
            ThreadHelper.ThrowIfNotOnUIThread();
            _catalog = services?.GetIconCatalog() ?? IconCatalog.Empty;
            _ink = ThemedInk();

            // ── Kubuno icons ──
            _search = MakeTextBox(string.Empty);
            _set = MakeComboBox(editable: false);
            _set.Items.Add(new SetChoice(null, IconText.SetAll));
            _set.Items.Add(new SetChoice("*recent", IconText.SetRecent));
            foreach (var set in IconCatalog.SetOrder)
            {
                _set.Items.Add(new SetChoice(set, IconText.SetName(set)));
            }

            _set.SelectedIndex = 0;
            _set.Width = 170;
            _set.Margin = new Thickness(8, 0, 0, 0);
            _count = ThemedControls.SecondaryText(string.Empty);
            _count.Margin = new Thickness(0, 4, 0, 4);
            _gallery = MakeGallery();
            var searchRow = new DockPanel();
            DockPanel.SetDock(_set, Dock.Right);
            searchRow.Children.Add(_set);
            searchRow.Children.Add(ThemedControls.WithPlaceholder(_search, IconText.Search));
            var kubunoTab = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(searchRow, Dock.Top);
            DockPanel.SetDock(_count, Dock.Top);
            kubunoTab.Children.Add(searchRow);
            kubunoTab.Children.Add(_count);
            if (_catalog.IsEmpty)
            {
                var missing = ThemedControls.SecondaryText(IconText.CatalogUnavailable);
                missing.Margin = new Thickness(0, 4, 0, 8);
                DockPanel.SetDock(missing, Dock.Top);
                kubunoTab.Children.Add(missing);
            }

            kubunoTab.Children.Add(_gallery);

            // ── Project images ──
            _files = MakeGallery();
            var import = MakeButton(IconText.Import);
            import.Click += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                ImportFile();
            };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            buttons.Children.Add(import);
            if (ProjectResourceBridge.IsAvailable && viewFile is not null)
            {
                var resource = MakeButton(IconText.ProjectResource);
                resource.Margin = new Thickness(8, 0, 0, 0);
                resource.Click += (_, _) =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    if (ProjectResourceBridge.PickIcon(viewFile, _value!.Text) is { } picked)
                    {
                        SetValue(picked);
                    }
                };
                buttons.Children.Add(resource);
            }

            var projectTab = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(buttons, Dock.Bottom);
            projectTab.Children.Add(buttons);
            projectTab.Children.Add(_files);

            _tabs = IconDialogTheme.TabControl();
            _tabs.Items.Add(new TabItem { Header = IconText.TabKubuno, Content = kubunoTab });
            _tabs.Items.Add(new TabItem { Header = IconText.TabProject, Content = projectTab });

            // ── Preview ──
            _caption = MakeText(string.Empty);
            _caption.FontWeight = FontWeights.SemiBold;
            _caption.TextTrimming = TextTrimming.CharacterEllipsis;
            _lightRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _darkRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var none = MakeButton(IconText.NoneButton);
            none.HorizontalAlignment = HorizontalAlignment.Left;
            none.Margin = new Thickness(0, 12, 0, 0);
            none.Click += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                SetValue(string.Empty);
            };
            var preview = new StackPanel { Width = 230, Margin = new Thickness(12, 0, 0, 0) };
            preview.Children.Add(MakeLabel(IconText.Preview));
            preview.Children.Add(_caption);
            preview.Children.Add(Swatch(IconText.Light, _lightRow, Colors.White));
            preview.Children.Add(Swatch(IconText.Dark, _darkRow, Color.FromRgb(0x1f, 0x1f, 0x1f)));
            preview.Children.Add(none);

            // ── Value ──
            _value = MakeTextBox(current ?? string.Empty);
            var valueRow = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var valueLabel = MakeLabel(IconText.ValueLabel, _value);
            DockPanel.SetDock(valueLabel, Dock.Left);
            valueRow.Children.Add(valueLabel);
            valueRow.Children.Add(_value);

            var body = new DockPanel();
            DockPanel.SetDock(valueRow, Dock.Bottom);
            DockPanel.SetDock(preview, Dock.Right);
            body.Children.Add(valueRow);
            body.Children.Add(preview);
            body.Children.Add(_tabs);
            SetBody(body);
            IconDialogTheme.ThemeScrollBars(this);

            _search.TextChanged += (_, _) => FillGallery();
            _set.SelectionChanged += (_, _) => FillGallery();
            _gallery.SelectionChanged += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                PickTile(_gallery);
            };
            _files.SelectionChanged += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                PickTile(_files);
            };
            _gallery.MouseDoubleClick += (_, _) => AcceptByDoubleClick(_gallery);
            _files.MouseDoubleClick += (_, _) => AcceptByDoubleClick(_files);
            _value.TextChanged += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (!_syncing)
                {
                    ShowPreview(_value.Text);
                }
            };

            Result = current;
            FillGallery();
            FillFiles();
            var kind = IconValue.Classify(current);
            _tabs.SelectedIndex = kind is IconValueKind.File or IconValueKind.Resource ? 1 : 0;
            SelectCurrent(current);
            ShowPreview(current);
            Loaded += (_, _) => (_tabs.SelectedIndex == 0 ? (UIElement)_search : _files).Focus();
        }

        public string? Result { get; private set; }

        /// <summary>Shows the picker for <paramref name="current"/>; the value chosen, or null when cancelled.</summary>
        public static string? Pick(string? viewFile, string? current, IKbviewIconServices? services)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dialog = new IconPickerDialog(viewFile, current, services);
            return dialog.Ask() ? dialog.Result : null;
        }

        protected override bool Accept()
        {
            Result = IconValue.Normalize(_value.Text);
            _recent.Add(Result);
            return true;
        }

        private sealed class SetChoice
        {
            public SetChoice(string? set, string label)
            {
                Set = set;
                Label = label;
            }

            public string? Set { get; }

            public string Label { get; }

            public override string ToString() => Label;
        }

        /// <summary>A tile of a gallery: the value it writes, its picture and its name.</summary>
        private sealed class Tile
        {
            public Tile(string value, string name, ImageSource? image, string tip)
            {
                Value = value;
                Name = name;
                Image = image;
                Tip = tip;
            }

            public string Value { get; }

            public string Name { get; }

            public ImageSource? Image { get; }

            public string Tip { get; }
        }

        private static Color ThemedInk()
        {
            var c = VSColorTheme.GetThemedColor(ThemedDialogColors.WindowPanelTextColorKey);
            return Color.FromArgb(c.A, c.R, c.G, c.B);
        }

        private static ListBox MakeGallery()
        {
            var list = new ListBox { SelectionMode = SelectionMode.Single };
            list.SetResourceReference(StyleProperty, VsResourceKeys.ThemedDialogListBoxStyleKey);
            list.ItemContainerStyle = IconDialogTheme.GalleryItemStyle();
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            var panel = new FrameworkElementFactory(typeof(WrapPanel));
            list.ItemsPanel = new ItemsPanelTemplate(panel);

            var tile = new FrameworkElementFactory(typeof(StackPanel));
            tile.SetValue(WidthProperty, 84.0);
            tile.SetValue(HeightProperty, 62.0);
            tile.SetBinding(ToolTipProperty, new System.Windows.Data.Binding(nameof(Tile.Tip)));
            var image = new FrameworkElementFactory(typeof(Image));
            image.SetValue(WidthProperty, 24.0);
            image.SetValue(HeightProperty, 24.0);
            image.SetValue(MarginProperty, new Thickness(0, 6, 0, 4));
            image.SetValue(RenderOptions.BitmapScalingModeProperty, BitmapScalingMode.HighQuality);
            image.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding(nameof(Tile.Image)));
            var name = new FrameworkElementFactory(typeof(TextBlock));
            name.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
            name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            name.SetValue(TextBlock.FontSizeProperty, 11.0);
            name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Tile.Name)));
            tile.AppendChild(image);
            tile.AppendChild(name);
            list.ItemTemplate = new DataTemplate { VisualTree = tile };
            return list;
        }

        private static Border Swatch(string label, StackPanel row, Color background)
        {
            var text = new TextBlock { Text = label, FontSize = 11, Margin = new Thickness(0, 0, 0, 6), Foreground = new SolidColorBrush(background.R > 128 ? LightInk : DarkInk) };
            var stack = new StackPanel();
            stack.Children.Add(text);
            stack.Children.Add(row);
            var border = new Border
            {
                Background = new SolidColorBrush(background),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 8, 0, 0),
                BorderThickness = new Thickness(1),
                Child = stack,
                MinHeight = 76,
            };
            border.SetResourceReference(Border.BorderBrushProperty, ThemedDialogColors.ListBoxBorderBrushKey);
            return border;
        }

        private void FillGallery()
        {
            var choice = _set.SelectedItem as SetChoice;
            IEnumerable<IconGlyph> glyphs;
            if (choice?.Set == "*recent")
            {
                glyphs = _recent.Items.Select(_catalog.Find).Where(g => g is not null).Select(g => g!);
                var q = _search.Text.Trim();
                if (q.Length > 0)
                {
                    var found = new HashSet<string>(_catalog.Search(q).Select(g => g.Name), StringComparer.Ordinal);
                    glyphs = glyphs.Where(g => found.Contains(g.Name));
                }
            }
            else
            {
                glyphs = _catalog.Search(_search.Text, choice?.Set);
            }

            var tiles = glyphs.Select(g => new Tile(g.Name, g.Name, IconDrawing.ToImage(g, _ink, LightAccent, 24), $"{g.Name} — {IconText.SetName(g.Set)}\n{string.Join(", ", g.Keywords)}")).ToList();
            _gallery.ItemsSource = tiles;
            _count.Text = IconText.Count(tiles.Count, _catalog.Icons.Count(g => g.Layers.Count > 0));
        }

        private void FillFiles()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var tiles = new List<Tile>();
            foreach (var path in ImageResources.ProjectImages(_viewFile, 400, IconValue.Extensions))
            {
                tiles.Add(new Tile(path, Path.GetFileName(path), FileImage(path, 24, _ink), path));
            }

            _files.ItemsSource = tiles;
            if (tiles.Count == 0)
            {
                _files.ToolTip = IconText.NoProjectImages;
            }
        }

        /// <summary>An image file's picture: the runtime's own rendering (SVG and anything else), else WIC.</summary>
        private ImageSource? FileImage(string value, int size, Color ink)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            // A common raster file is decoded here (quick); an SVG, a WebP (WPF loses its transparency) or a
            // resource is rendered by the runtime's own code.
            var ext = System.IO.Path.GetExtension(value).ToLowerInvariant();
            if (ext is not (".svg" or ".webp") && ImageResources.Resolve(_viewFile, value) is { } full && IconDrawing.LoadFile(full, size) is { } local)
            {
                return local;
            }

            return _services?.RenderIcon(value, size, ink);
        }

        private void SelectCurrent(string? current)
        {
            var v = (current ?? string.Empty).Trim();
            if (v.Length == 0)
            {
                return;
            }

            _syncing = true;
            try
            {
                var name = IconValue.GlyphName(v, _catalog);
                var list = IconValue.Classify(v) == IconValueKind.Glyph ? _gallery : _files;
                var tile = list.Items.OfType<Tile>().FirstOrDefault(t => string.Equals(t.Value, name, StringComparison.Ordinal));
                if (tile is not null)
                {
                    list.SelectedItem = tile;
                    list.ScrollIntoView(tile);
                }
            }
            finally
            {
                _syncing = false;
            }
        }

        private void PickTile(ListBox list)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_syncing || list.SelectedItem is not Tile tile)
            {
                return;
            }

            SetValue(tile.Value);
        }

        private void AcceptByDoubleClick(ListBox list)
        {
            if (list.SelectedItem is Tile && Accept())
            {
                DialogResult = true;
            }
        }

        private void SetValue(string value)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _syncing = true;
            try
            {
                _value.Text = value;
            }
            finally
            {
                _syncing = false;
            }

            ShowPreview(value);
        }

        private void ShowPreview(string? value)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var v = (value ?? string.Empty).Trim();
            _caption.Text = v.Length == 0 ? IconText.None : v;
            Fill(_lightRow, v, LightInk, LightAccent);
            Fill(_darkRow, v, DarkInk, DarkAccent);
        }

        private void Fill(StackPanel row, string value, Color ink, Color accent)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            row.Children.Clear();
            foreach (var size in PreviewSizes)
            {
                var image = Picture(value, size, ink, accent);
                var cell = new StackPanel { Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Bottom };
                cell.Children.Add(new Image
                {
                    Source = image,
                    Width = size,
                    Height = size,
                    SnapsToDevicePixels = true,
                    HorizontalAlignment = HorizontalAlignment.Center,
                });
                cell.Children.Add(new TextBlock { Text = size.ToString(System.Globalization.CultureInfo.InvariantCulture), FontSize = 9, Foreground = new SolidColorBrush(ink), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) });
                row.Children.Add(cell);
            }
        }

        private ImageSource? Picture(string value, int size, Color ink, Color accent)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            switch (IconValue.Classify(value))
            {
                case IconValueKind.Glyph:
                    return _catalog.Find(value) is { } glyph ? IconDrawing.ToImage(glyph, ink, accent, size) : _services?.RenderIcon(value, PixelSize(size), ink);
                case IconValueKind.File:
                case IconValueKind.Resource:
                    return _services?.RenderIcon(value, PixelSize(size), ink) ?? FileImage(value, size, ink);
                default:
                    return null;
            }
        }

        /// <summary>The pixel size of <paramref name="dip"/> on this monitor (the runtime renders at the pixels it covers).</summary>
        private int PixelSize(int dip)
        {
            var scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            return Math.Max(1, (int)Math.Round(dip * scale));
        }

        private void ImportFile()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = IconText.ImportFilter, CheckFileExists = true };
            if (_viewFile is not null)
            {
                dialog.InitialDirectory = Path.GetDirectoryName(_viewFile);
            }

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            var file = dialog.FileName;
            string value;
            if (_viewFile is null)
            {
                value = file.Replace('\\', '/');
            }
            else
            {
                if (!ImageResources.IsBesideView(_viewFile, file))
                {
                    var target = ImageResources.CopyTarget(_viewFile, file);
                    var answer = AskYesNoCancel(IconText.ImportPrompt(Path.GetFileName(file), Path.GetDirectoryName(target) ?? string.Empty));
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

                value = ImageResources.RelativePath(_viewFile, file);
            }

            FillFiles();
            SetValue(value);
            var tile = _files.Items.OfType<Tile>().FirstOrDefault(t => t.Value == value);
            if (tile is not null)
            {
                _syncing = true;
                _files.SelectedItem = tile;
                _files.ScrollIntoView(tile);
                _syncing = false;
            }
        }
    }
}
