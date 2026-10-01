using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.VisualStudio.PlatformUI;

namespace Kubuno.Desktop.Designer.Bindings
{
    /// <summary>
    /// The binding picker of the Properties window's value drop-down (docs/DESIGNER.md, "Data bindings"): the Windows Forms
    /// twin of <see cref="BindingPickerView"/> (the grid's drop-down holder only keeps Windows Forms content open under the
    /// mouse), on the same <see cref="BindingPickerModel"/>: the row's problems, its literal values, a search box, the tree of
    /// what it can be bound to (fitting members first, the others greyed), and « Avancé… », « Modifier la valeur… »,
    /// « Supprimer la liaison ». A click on a member picks it; Enter too. Colours from the Visual Studio theme.
    /// </summary>
    internal sealed class BindingPickerControl : UserControl
    {
        private readonly BindingSourceSchema _schema;
        private readonly BindingShape? _want;
        private readonly string? _current;
        private readonly TextBox _search;
        private readonly TreeView _tree;
        private readonly Color _text;
        private readonly Color _grey;

        public BindingPickerControl(BindingSourceSchema schema, BindingShape? want, string? current, IReadOnlyList<string> literals, IEnumerable<BindingIssue> issues, bool canEditValue)
        {
            _schema = schema;
            _want = want;
            _current = current;
            var back = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey);
            _text = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowTextColorKey);
            _grey = VSColorTheme.GetThemedColor(EnvironmentColors.SystemGrayTextColorKey);
            var link = VSColorTheme.GetThemedColor(EnvironmentColors.ControlLinkTextColorKey);
            BackColor = back;
            ForeColor = _text;
            Padding = new Padding(6);

            var scale = ScreenDpi() / 96f;
            Size = new Size((int)(400 * scale), (int)((literals.Count > 0 ? 440 : 400) * scale));

            // Bottom to top (Dock order): links, tree (fill), search, values, problems.
            var links = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = back, Padding = new Padding(0, 4, 0, 0) };
            links.Controls.Add(Link(BindingStrings.Advanced, link, () => AdvancedRequested?.Invoke()));
            if (canEditValue)
            {
                links.Controls.Add(Link(BindingStrings.EditValue, link, () => EditValueRequested?.Invoke()));
            }

            if (BindingMarkup.IsMarkupExtension(current))
            {
                links.Controls.Add(Link(BindingStrings.RemoveBinding, link, () => RemoveRequested?.Invoke()));
                links.Controls.Add(Link(BindingStrings.GoToDefinition, link, () => GoToDefinitionRequested?.Invoke()));
            }

            _tree = new TreeView
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = back,
                ForeColor = _text,
                HideSelection = false,
                ShowLines = false,
                FullRowSelect = true,
                ItemHeight = (int)(20 * scale),
                AccessibleName = BindingStrings.Source,
            };
            _tree.NodeMouseClick += (_, e) =>
            {
                // A click on a member picks it (the expander of a component only expands it).
                if (e.Button == MouseButtons.Left && e.Node.Tag is BindingPickerNode node && (node.Children.Count == 0 || e.X >= e.Node.Bounds.Left))
                {
                    Pick(node);
                }
            };
            _tree.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter && _tree.SelectedNode?.Tag is BindingPickerNode node)
                {
                    Pick(node);
                    e.Handled = true;
                }
            };

            _search = new TextBox { Dock = DockStyle.Top, BackColor = back, ForeColor = _text, BorderStyle = BorderStyle.FixedSingle, AccessibleName = BindingStrings.Search };
            _search.TextChanged += (_, _) => Fill();
            _search.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Down && _tree.Nodes.Count > 0)
                {
                    _tree.Focus();
                    _tree.SelectedNode = FirstMember(_tree.Nodes) ?? _tree.Nodes[0];
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Enter && FirstMember(_tree.Nodes)?.Tag is BindingPickerNode first)
                {
                    Pick(first);
                    e.Handled = true;
                }
            };

            Controls.Add(_tree);
            Controls.Add(Spacer(4));
            Controls.Add(_search);
            if (literals.Count > 0)
            {
                var values = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = back, Padding = new Padding(0, 0, 0, 4) };
                foreach (var v in literals)
                {
                    var button = new Button { Text = v, AutoSize = true, FlatStyle = FlatStyle.Flat, ForeColor = _text, BackColor = back, Margin = new Padding(0, 0, 4, 4) };
                    button.FlatAppearance.BorderColor = _grey;
                    button.Click += (_, _) => LiteralPicked?.Invoke(v);
                    values.Controls.Add(button);
                }

                Controls.Add(values);
                Controls.Add(new Label { Dock = DockStyle.Top, Text = BindingStrings.Values, AutoSize = false, Height = (int)(20 * scale), ForeColor = _text, Font = new Font(Font, FontStyle.Bold) });
            }

            foreach (var issue in issues)
            {
                Controls.Add(new Label { Dock = DockStyle.Top, Text = BindingStrings.WithIssue(issue.Message), AutoSize = false, Height = (int)(36 * scale), ForeColor = _text, Font = new Font(Font, FontStyle.Bold) });
            }

            Controls.Add(links);
            Fill();
        }

        public event Action<string>? Picked;

        public event Action<string>? LiteralPicked;

        public event Action? AdvancedRequested;

        public event Action? EditValueRequested;

        public event Action? RemoveRequested;

        public event Action? GoToDefinitionRequested;

        public event Action? Cancelled;

        /// <summary>The groups shown (what UI automation and the tests of the live picker read).</summary>
        public IReadOnlyList<BindingPickerGroup> Groups { get; private set; } = Array.Empty<BindingPickerGroup>();

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _search.Focus();
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Cancelled?.Invoke();
                return true;
            }

            return base.ProcessDialogKey(keyData);
        }

        private void Fill()
        {
            _tree.BeginUpdate();
            _tree.Nodes.Clear();
            Groups = BindingPickerModel.Build(_schema, _want, _current, _search.Text);
            foreach (var group in Groups)
            {
                var g = new TreeNode(BindingStrings.Group(group.Id, group.Label)) { NodeFont = new Font(_tree.Font, FontStyle.Bold), ForeColor = _text };
                foreach (var node in group.Nodes)
                {
                    g.Nodes.Add(NodeOf(node, expand: _search.Text.Trim().Length > 0 || node.Children.Any(c => c.IsCurrent)));
                }

                g.Expand();
                _tree.Nodes.Add(g);
            }

            if (Groups.Count == 0)
            {
                _tree.Nodes.Add(new TreeNode(BindingStrings.NoSources) { ForeColor = _grey });
            }
            else if (_schema.Context.Open && _search.Text.Trim().Length == 0)
            {
                _tree.Nodes.Add(new TreeNode(BindingStrings.OpenContext) { ForeColor = _grey });
            }

            _tree.EndUpdate();
            if (_tree.Nodes.Count > 0)
            {
                _tree.Nodes[0].EnsureVisible();
            }
        }

        private TreeNode NodeOf(BindingPickerNode node, bool expand)
        {
            var t = new TreeNode(node.Label + "   " + BindingStrings.TypeOf(node.Member))
            {
                Tag = node,
                ForeColor = node.Compatible ? _text : _grey,
                ToolTipText = BindingStrings.Combine(node.Member.Path, node.Member.Doc, node.Compatible ? null : BindingStrings.Incompatible(node.Member.Shape, _want)),
            };
            if (node.IsCurrent)
            {
                t.NodeFont = new Font(_tree.Font, FontStyle.Bold);
            }

            foreach (var child in node.Children)
            {
                t.Nodes.Add(NodeOf(child, expand: false));
            }

            if (expand)
            {
                t.Expand();
            }

            return t;
        }

        private static TreeNode? FirstMember(TreeNodeCollection nodes)
        {
            foreach (TreeNode n in nodes)
            {
                if (n.Tag is BindingPickerNode)
                {
                    return n;
                }

                if (FirstMember(n.Nodes) is { } inner)
                {
                    return inner;
                }
            }

            return null;
        }

        /// <summary>Picks <paramref name="node"/> (a click, Enter, UI automation).</summary>
        public void Pick(BindingPickerNode node) => Picked?.Invoke(BindingPickerModel.Apply(_current, node.Member));

        /// <summary>Picks the member at <paramref name="path"/>, when the tree shows it (UI automation of the live check).</summary>
        public bool Pick(string path)
        {
            var node = Groups.SelectMany(g => g.Nodes).SelectMany(n => new[] { n }.Concat(n.Children)).FirstOrDefault(n => n.Member.Path == path);
            if (node is null)
            {
                return false;
            }

            Pick(node);
            return true;
        }

        private static LinkLabel Link(string text, Color color, Action click)
        {
            var label = new LinkLabel { Text = text, AutoSize = true, LinkColor = color, ActiveLinkColor = color, VisitedLinkColor = color, Margin = new Padding(0, 0, 12, 0) };
            label.LinkClicked += (_, _) => click();
            return label;
        }

        private static Control Spacer(int height) => new Panel { Dock = DockStyle.Top, Height = height };

        private static float ScreenDpi()
        {
            using var g = Graphics.FromHwnd(IntPtr.Zero);
            return g.DpiX;
        }
    }
}
