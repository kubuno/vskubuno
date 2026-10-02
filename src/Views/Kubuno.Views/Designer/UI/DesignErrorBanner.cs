using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Kubuno.Views.Designer.DesignSurface;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Views.Designer.UI
{
    /// <summary>
    /// The design surface's error banner (docs/DESIGNER.md section 17): a compact, non-blocking strip above
    /// the surface - the WinForms designer's error page, minus the blocking - saying what the preview shows
    /// of a view with errors, and listing its diagnostics (<c>line:column message</c>), syntax errors first.
    /// It shows <see cref="CollapsedRows"/> entries, then « Afficher les N autres… » (remembered per document)
    /// with an inner scroll beyond that. An entry is a link: <see cref="NavigateRequested"/> takes the XML pane
    /// to it. In Visual Studio's info bar colours; the diagnostics themselves reach the Error List through the
    /// language server, not from here.
    /// </summary>
    internal sealed class DesignErrorBanner : Border
    {
        /// <summary>The entries shown before « Afficher les N autres… ».</summary>
        internal const int CollapsedRows = 3;

        /// <summary>The height of the expanded list before it scrolls.</summary>
        private const double ExpandedMaxHeight = 160;

        /// <summary>Whether a document's banner was expanded, by document path (the choice survives a reopen).</summary>
        private static readonly Dictionary<string, bool> s_expandedByDocument = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        private readonly CrispImage _icon = new CrispImage { Width = 16, Height = 16, Margin = new Thickness(0, 1, 6, 0), VerticalAlignment = VerticalAlignment.Top };
        private readonly TextBlock _headline = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        private readonly Hyperlink _toggle = new Hyperlink();
        private readonly TextBlock _toggleHost = new TextBlock { Margin = new Thickness(22, 2, 0, 0) };
        private readonly StackPanel _list = new StackPanel { Margin = new Thickness(22, 4, 0, 0) };
        private readonly ScrollViewer _scroll;
        private DesignErrorBannerModel _model = DesignErrorBannerModel.Hidden;
        private bool _expanded;

        public DesignErrorBanner()
        {
            Padding = new Thickness(8, 4, 8, 4);
            BorderThickness = new Thickness(0, 0, 0, 1);
            Visibility = Visibility.Collapsed;
            SetResourceReference(BackgroundProperty, VsBrushes.InfoBackgroundKey);
            SetResourceReference(BorderBrushProperty, EnvironmentColors.ToolWindowBorderBrushKey);
            _headline.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.InfoTextKey);
            _toggle.SetResourceReference(TextElement.ForegroundProperty, EnvironmentColors.ControlLinkTextBrushKey);
            _toggle.Click += (_, _) =>
            {
                _expanded = !_expanded;
                if (DocumentPath is { Length: > 0 } path)
                {
                    s_expandedByDocument[path] = _expanded;
                }

                Render();
            };
            _toggleHost.Inlines.Add(_toggle);

            var header = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(_icon, Dock.Left);
            header.Children.Add(_icon);
            header.Children.Add(_headline);

            _scroll = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var stack = new StackPanel();
            stack.Children.Add(header);
            stack.Children.Add(_scroll);
            stack.Children.Add(_toggleHost);
            Child = stack;
        }

        /// <summary>Raised when an entry is clicked: go to its place in the XML.</summary>
        public event EventHandler<DesignSurfaceDiagnostic>? NavigateRequested;

        /// <summary>The document the banner is about: its expanded state is remembered under this path.</summary>
        public string? DocumentPath
        {
            get => _documentPath;
            set
            {
                _documentPath = value;
                _expanded = value is { Length: > 0 } && s_expandedByDocument.TryGetValue(value, out var expanded) && expanded;
            }
        }

        private string? _documentPath;

        /// <summary>What the banner shows now (tests).</summary>
        public DesignErrorBannerModel Model => _model;

        /// <summary>Shows <paramref name="model"/> (hidden when it says so).</summary>
        public void Show(DesignErrorBannerModel model)
        {
            _model = model ?? DesignErrorBannerModel.Hidden;
            Render();
        }

        private void Render()
        {
            if (!_model.IsVisible)
            {
                Visibility = Visibility.Collapsed;
                _list.Children.Clear();
                return;
            }

            _icon.Moniker = _model.IsError ? KnownMonikers.StatusError : KnownMonikers.StatusWarning;
            _headline.Text = _model.Headline;
            var count = _model.Entries.Count;
            var hidden = Math.Max(0, count - CollapsedRows);
            var shown = _expanded ? _model.Entries : _model.Entries.Take(CollapsedRows);
            _list.Children.Clear();
            foreach (var entry in shown)
            {
                _list.Children.Add(BuildEntry(entry));
            }

            _scroll.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _scroll.MaxHeight = _expanded ? ExpandedMaxHeight : double.PositiveInfinity;
            _toggleHost.Visibility = hidden > 0 ? Visibility.Visible : Visibility.Collapsed;
            _toggle.Inlines.Clear();
            _toggle.Inlines.Add(new Run(_expanded ? DesignerText.ErrorBannerShowLess : DesignerText.ErrorBannerShowMore(hidden)));
            Visibility = Visibility.Visible;
        }

        private UIElement BuildEntry(DesignErrorBannerEntry entry)
        {
            var link = new Hyperlink(new Run(entry.Location)) { ToolTip = DesignerText.ErrorBannerGoTo };
            link.SetResourceReference(TextElement.ForegroundProperty, EnvironmentColors.ControlLinkTextBrushKey);
            link.Click += (_, _) => NavigateRequested?.Invoke(this, entry.Diagnostic);
            var icon = new CrispImage { Width = 12, Height = 12, Margin = new Thickness(0, 2, 5, 0), VerticalAlignment = VerticalAlignment.Top, Moniker = entry.IsWarning ? KnownMonikers.StatusWarning : KnownMonikers.StatusError };
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Cursor = Cursors.Arrow };
            text.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.InfoTextKey);
            text.Inlines.Add(link);
            text.Inlines.Add(new Run("  " + entry.Text));
            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            row.Children.Add(text);
            return row;
        }
    }
}
