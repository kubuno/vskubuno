using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Kubuno.VisualStudio.Designer.DesignSurface;
using Microsoft.VisualStudio.TextManager.Interop;
using OleInterop = Microsoft.VisualStudio.OLE.Interop;

namespace Kubuno.VisualStudio.Designer.UI
{
    /// <summary>
    /// The whole content of <see cref="EditorFactory.DesignerWindowPane"/>: the Design/XML/Split
    /// orientation tab strip (docs/DESIGNER.md §1) on top, and a two-pane <see cref="Grid"/> below it -
    /// a <see cref="DesignSurface.IDesignSurfaceHost"/> (today, always
    /// <see cref="PlaceholderDesignSurfaceHost"/> - see <see cref="DesignSurfaceHostFactoryHost"/>) on
    /// the left, a <see cref="GridSplitter"/>, and a <see cref="CodeWindowHost"/> wrapping the real VS
    /// text editor on the right. No .xaml file: built entirely in code, matching the rest of this repo
    /// (see the csproj's own top comment).
    /// </summary>
    public sealed class DesignerSplitView : UserControl, IDisposable
    {
        private readonly DesignerSplitViewModel _viewModel = new();
        private readonly CodeWindowHost _codeWindowHost;
        private readonly IDesignSurfaceHost _designSurfaceHost;
        private readonly ColumnDefinition _designColumn;
        private readonly ColumnDefinition _splitterColumn;
        private readonly ColumnDefinition _xmlColumn;
        private readonly GridSplitter _splitter;
        private readonly ToggleButton _designTab;
        private readonly ToggleButton _xmlTab;
        private readonly ToggleButton _splitTab;
        private bool _disposed;

        public DesignerSplitView(IVsTextLines textBuffer, OleInterop.IServiceProvider oleServiceProvider)
            : this(textBuffer, oleServiceProvider, DesignSurfaceHostFactoryHost.Current)
        {
        }

        /// <summary>Overload used by tests to inject a fake <see cref="IDesignSurfaceHostFactory"/> instead of the static gateway's current value.</summary>
        internal DesignerSplitView(IVsTextLines textBuffer, OleInterop.IServiceProvider oleServiceProvider, IDesignSurfaceHostFactory designSurfaceHostFactory)
        {
            if (textBuffer is null)
            {
                throw new ArgumentNullException(nameof(textBuffer));
            }

            if (designSurfaceHostFactory is null)
            {
                throw new ArgumentNullException(nameof(designSurfaceHostFactory));
            }

            _codeWindowHost = new CodeWindowHost(textBuffer, oleServiceProvider);
            _designSurfaceHost = designSurfaceHostFactory.Create();

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            (_designTab, _xmlTab, _splitTab) = BuildTabStripButtons();
            var tabStrip = BuildTabStrip(_designTab, _xmlTab, _splitTab);
            Grid.SetRow(tabStrip, 0);
            root.Children.Add(tabStrip);

            var contentGrid = new Grid();
            _designColumn = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
            _splitterColumn = new ColumnDefinition { Width = GridLength.Auto };
            _xmlColumn = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
            contentGrid.ColumnDefinitions.Add(_designColumn);
            contentGrid.ColumnDefinitions.Add(_splitterColumn);
            contentGrid.ColumnDefinitions.Add(_xmlColumn);

            var designPane = new Border { Child = _designSurfaceHost.Content };
            Grid.SetColumn(designPane, 0);
            contentGrid.Children.Add(designPane);

            _splitter = new GridSplitter
            {
                Width = 4,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            };
            Grid.SetColumn(_splitter, 1);
            contentGrid.Children.Add(_splitter);

            Grid.SetColumn(_codeWindowHost, 2);
            contentGrid.Children.Add(_codeWindowHost);

            Grid.SetRow(contentGrid, 1);
            root.Children.Add(contentGrid);

            Content = root;

            _viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DesignerSplitViewModel.Mode))
                {
                    ApplyViewMode();
                }
            };
            ApplyViewMode();

            // One-shot initial push (docs/DESIGNER.md §2's "the compiled preview always re-renders
            // from buffer text ... pushed over IPC"). Continuous, debounced pushes on every
            // ITextBuffer.Changed are wired by DSG-6/DSG-8/DSG-9 once there is a real render surface to
            // push to - the placeholder ignores the text anyway.
            try
            {
                _designSurfaceHost.SetDocumentText(VsTextLinesText.ReadAll(textBuffer));
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // Best-effort: an empty/uninitialized buffer should not prevent the pane from opening.
            }
        }

        /// <summary>Current orientation - exposed for DSG-8's selection sync and for tests, not just the tab strip's own click handlers.</summary>
        public DesignerViewMode Mode
        {
            get => _viewModel.Mode;
            set => _viewModel.Mode = value;
        }

        private (ToggleButton design, ToggleButton xml, ToggleButton split) BuildTabStripButtons()
        {
            var design = new ToggleButton { Content = "Design", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(4, 4, 0, 4) };
            var xml = new ToggleButton { Content = "XML", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(2, 4, 0, 4) };
            var split = new ToggleButton { Content = "Split", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(2, 4, 4, 4) };

            design.Click += (_, _) => Mode = DesignerViewMode.Design;
            xml.Click += (_, _) => Mode = DesignerViewMode.Xml;
            split.Click += (_, _) => Mode = DesignerViewMode.Split;

            return (design, xml, split);
        }

        private static StackPanel BuildTabStrip(ToggleButton design, ToggleButton xml, ToggleButton split)
        {
            var strip = new StackPanel { Orientation = Orientation.Horizontal };
            strip.Children.Add(design);
            strip.Children.Add(xml);
            strip.Children.Add(split);
            return strip;
        }

        private void ApplyViewMode()
        {
            _designColumn.Width = _viewModel.IsDesignPaneVisible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            _xmlColumn.Width = _viewModel.IsXmlPaneVisible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            _splitterColumn.Width = _viewModel.IsSplitterVisible ? GridLength.Auto : new GridLength(0);
            _splitter.Visibility = _viewModel.IsSplitterVisible ? Visibility.Visible : Visibility.Collapsed;

            _designTab.IsChecked = _viewModel.Mode == DesignerViewMode.Design;
            _xmlTab.IsChecked = _viewModel.Mode == DesignerViewMode.Xml;
            _splitTab.IsChecked = _viewModel.Mode == DesignerViewMode.Split;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _designSurfaceHost.Dispose();
            _codeWindowHost.Dispose();
        }
    }
}
