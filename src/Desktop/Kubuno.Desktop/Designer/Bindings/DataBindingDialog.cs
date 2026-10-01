using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Kubuno.Desktop.Designer.UI;
using Microsoft.VisualStudio.PlatformUI;

namespace Kubuno.Desktop.Designer.Bindings
{
    /// <summary>
    /// « Liaison de données » (docs/DESIGNER.md, "Data bindings"): Windows Forms' "Formatting and Advanced Binding" and
    /// WPF's "Create Data Binding" in one themed dialog. On the left the source picker (<see cref="BindingPickerView"/>);
    /// on the right the path and source, the mode and the update trigger, the converter and its parameter, the format and
    /// culture, the value when null and the fallback value, the design-time value (<c>d:</c>), a live sample computed by
    /// the runtime's own code (<c>kubuno/bindingPreview</c>) and the resulting expression. OK yields the attribute edits
    /// (<see cref="BindingEditModel.Edits"/>); « Supprimer la liaison » the removal (<see cref="BindingEditModel.RemoveBinding"/>).
    /// </summary>
    internal sealed class DataBindingDialog : ThemedEditorDialog
    {
        /// <summary>Formats offered (any .NET-style format can be typed).</summary>
        private static readonly string[] Formats = { "N0", "N2", "C", "P0", "F1", "d", "D", "g", "dd/MM/yyyy", "HH:mm", "#,##0.00" };

        private static readonly string[] Cultures = { "fr-FR", "en-US", "de-DE", "invariant" };

        private readonly BindingEditModel _model;
        private readonly BindingSourceSchema _schema;
        private readonly BindingShape? _want;
        private readonly Func<string, string?, BindingShape, BindingShape?, BindingPreview>? _preview;
        private readonly TextBox _path;
        private readonly ComboBox _source;
        private readonly ComboBox _mode;
        private readonly ComboBox _trigger;
        private readonly ComboBox _converter;
        private readonly TextBox _parameter;
        private readonly ComboBox _format;
        private readonly ComboBox _culture;
        private readonly TextBox _nullValue;
        private readonly TextBox _fallback;
        private readonly TextBox _design;
        private readonly TextBox _sample;
        private readonly TextBlock _shown;
        private readonly TextBlock _modeHelp;
        private readonly TextBlock _triggerHelp;
        private readonly TextBox _expression;
        private readonly DispatcherTimer _previewTimer;
        private BindingShape _sampleShape = BindingShape.Text;
        private bool _loading;

        /// <param name="preview">The live sample (<c>kubuno/bindingPreview</c>), or null without a language server.</param>
        public DataBindingDialog(BindingEditModel model, BindingSourceSchema schema, BindingShape? want, string propertyType, IEnumerable<BindingIssue> issues, Func<string, string?, BindingShape, BindingShape?, BindingPreview>? preview)
            : base(BindingStrings.DialogTitle + " - " + model.Property, 900, 640)
        {
            _model = model;
            _schema = schema;
            _want = want;
            _preview = preview;

            var picker = new BindingPickerView(schema, want, model.Build(), Array.Empty<string>(), Array.Empty<BindingIssue>(), canEditValue: false, embedded: true) { Width = 330, Margin = new Thickness(0, 0, 12, 0) };
            picker.MemberPicked += Use;

            _path = MakeTextBox(model.Path ?? string.Empty);
            _source = MakeComboBox(editable: true);
            _source.Items.Add(string.Empty);
            foreach (var c in schema.Components)
            {
                _source.Items.Add(c.Path);
            }

            _source.Text = model.Source ?? string.Empty;
            _mode = Combo(BindingMarkup.Modes, model.Mode ?? "OneWay", editable: false);
            _trigger = Combo(BindingMarkup.Triggers, model.Trigger ?? "PropertyChanged", editable: false);
            _converter = MakeComboBox(editable: true);
            _converter.Items.Add(string.Empty);
            foreach (var c in schema.Converters)
            {
                _converter.Items.Add(new ComboBoxItem { Content = c.Name, ToolTip = BindingStrings.Combine(BindingStrings.Converted(c.Name, c.Project) + " → " + c.Output, c.Doc) });
            }

            _converter.Text = model.Converter ?? string.Empty;
            _parameter = MakeTextBox(model.ConverterParameter ?? string.Empty);
            _format = Combo(Formats, model.StringFormat ?? string.Empty, editable: true);
            _culture = Combo(Cultures, model.Culture ?? string.Empty, editable: true);
            _nullValue = MakeTextBox(model.TargetNullValue ?? string.Empty);
            _fallback = MakeTextBox(model.FallbackValue ?? string.Empty);
            _design = MakeTextBox(model.DesignValue ?? string.Empty);
            _sample = MakeTextBox(string.Empty);
            _shown = MakeText(string.Empty, wrap: true);
            _shown.FontWeight = FontWeights.SemiBold;
            _modeHelp = Help(BindingStrings.Mode(model.Mode ?? "OneWay"));
            _triggerHelp = Help(BindingStrings.Trigger(model.Trigger ?? "PropertyChanged"));
            _expression = MakeTextBox(string.Empty);
            _expression.IsReadOnly = true;
            _expression.TextWrapping = TextWrapping.Wrap;
            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _previewTimer.Tick += (_, _) =>
            {
                _previewTimer.Stop();
                RefreshPreview();
            };

            var fields = new Grid { VerticalAlignment = VerticalAlignment.Top };
            fields.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var row = 0;
            void Section(string title)
            {
                var header = MakeText(title);
                header.FontWeight = FontWeights.SemiBold;
                header.Margin = new Thickness(0, row == 0 ? 0 : 10, 0, 2);
                fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(header, row++);
                Grid.SetColumnSpan(header, 2);
                fields.Children.Add(header);
            }

            void Field(string label, UIElement editor)
            {
                System.Windows.Automation.AutomationProperties.SetName(editor, label.TrimEnd(' ', ':', ' '));
                UI.BindingDialog.AddRow(fields, row++, MakeLabel(label, editor), editor);
            }

            void Note(TextBlock note)
            {
                fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(note, row++);
                Grid.SetColumn(note, 1);
                fields.Children.Add(note);
            }

            var property = MakeText(model.Property + (propertyType.Length > 0 ? "  (" + propertyType + ")" : string.Empty));
            Field(BindingStrings.Property, property);
            foreach (var issue in issues)
            {
                Note(Help(BindingStrings.WithIssue(issue.Message)));
            }

            Section(BindingStrings.Source);
            Field(BindingStrings.PathLabel, _path);
            Field(BindingStrings.SourceLabel, _source);
            Section(BindingStrings.Behaviour);
            Field(BindingStrings.ModeLabel, _mode);
            Note(_modeHelp);
            Field(BindingStrings.TriggerLabel, _trigger);
            Note(_triggerHelp);
            Section(BindingStrings.Formatting);
            Field(BindingStrings.ConverterLabel, _converter);
            Field(BindingStrings.ConverterParameterLabel, _parameter);
            Field(BindingStrings.FormatLabel, _format);
            Field(BindingStrings.CultureLabel, _culture);
            Field(BindingStrings.NullValueLabel, _nullValue);
            Field(BindingStrings.FallbackLabel, _fallback);
            Field(BindingStrings.DesignValueLabel, _design);
            Field(BindingStrings.SampleLabel, _sample);
            Field(BindingStrings.PreviewLabel, _shown);
            Field(BindingStrings.ExpressionLabel, _expression);

            var remove = MakeButton(BindingStrings.RemoveBinding);
            remove.HorizontalAlignment = HorizontalAlignment.Left;
            remove.Margin = new Thickness(0, 10, 0, 0);
            remove.IsEnabled = model.WasBound;
            remove.Click += (_, _) =>
            {
                Edits = BindingEditModel.RemoveBinding(model.Property, model.DesignValue);
                Removed = true;
                DialogResult = true;
            };
            var right = new DockPanel();
            DockPanel.SetDock(remove, Dock.Bottom);
            right.Children.Add(remove);
            right.Children.Add(new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });

            var body = new DockPanel();
            DockPanel.SetDock(picker, Dock.Left);
            body.Children.Add(picker);
            body.Children.Add(right);
            SetBody(body);

            // Every change updates the expression and the sample.
            foreach (var box in new[] { _path, _parameter, _nullValue, _fallback, _design, _sample })
            {
                box.TextChanged += (_, _) => Changed();
            }

            foreach (var combo in new[] { _source, _converter, _format, _culture })
            {
                combo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => Changed()));
#pragma warning disable VSTHRD001, VSTHRD110 // a plain "after the combo box shows the picked text", on this UI thread.
                combo.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(new Action(Changed), DispatcherPriority.Background);
#pragma warning restore VSTHRD001, VSTHRD110
            }

            _mode.SelectionChanged += (_, _) =>
            {
                _modeHelp.Text = BindingStrings.Mode(_mode.SelectedItem as string ?? "OneWay");
                Changed();
            };
            _trigger.SelectionChanged += (_, _) =>
            {
                _triggerHelp.Text = BindingStrings.Trigger(_trigger.SelectedItem as string ?? "PropertyChanged");
                Changed();
            };

            _loading = true;
            SetSample(schema.Find(model.Source is { } s ? s + "." + model.Path : model.Path));
            _loading = false;
            Changed();
            Loaded += (_, _) => _path.Focus();
        }

        /// <summary>The attribute edits OK makes (or the removal), empty when nothing changed.</summary>
        public IReadOnlyList<BindingAttributeEdit> Edits { get; private set; } = Array.Empty<BindingAttributeEdit>();

        /// <summary>Whether « Supprimer la liaison » closed the dialog.</summary>
        public bool Removed { get; private set; }

        /// <summary>The fields as typed (what OK writes).</summary>
        public BindingEditModel Model
        {
            get
            {
                Read();
                return _model;
            }
        }

        protected override bool Accept()
        {
            Read();
            Edits = _model.Edits();
            return true;
        }

        /// <summary>A member picked in the tree fills the path and the source.</summary>
        private void Use(BindingMember member)
        {
            _loading = true;
            switch (member.Kind)
            {
                case "component":
                    _source.Text = member.Path;
                    _path.Text = string.Empty;
                    break;
                case "column":
                case "state":
                    var dot = member.Path.IndexOf('.');
                    _source.Text = dot > 0 ? member.Path.Substring(0, dot) : string.Empty;
                    _path.Text = dot > 0 ? member.Path.Substring(dot + 1) : member.Path;
                    break;
                default:
                    _source.Text = string.Empty;
                    _path.Text = member.Path;
                    break;
            }

            // A two-way binding of a read-only member writes nothing back.
            if (!member.Writable && (_mode.SelectedItem as string) is "TwoWay" or "OneWayToSource")
            {
                _mode.SelectedItem = "OneWay";
            }

            SetSample(member);
            _loading = false;
            Changed();
        }

        /// <summary>A sample value of the member's shape (its design-time value first).</summary>
        private void SetSample(BindingMember? member)
        {
            _sampleShape = member?.Shape is BindingShape.Bool or BindingShape.Number ? member.Shape : BindingShape.Text;
            _sample.Text = _model.DesignValue is { Length: > 0 } d && _sampleShape == BindingShape.Text ? d : _sampleShape switch
            {
                BindingShape.Number => "1234.5",
                BindingShape.Bool => "true",
                _ => DesignerText.IsFrench ? "Exemple" : "Sample",
            };
        }

        private void Read()
        {
            _model.Path = _path.Text;
            _model.Source = _source.Text;
            _model.Mode = _mode.SelectedItem as string;
            _model.Trigger = _trigger.SelectedItem as string;
            _model.Converter = _converter.Text;
            _model.ConverterParameter = _parameter.Text;
            _model.StringFormat = _format.Text;
            _model.Culture = _culture.Text;
            _model.TargetNullValue = _nullValue.Text;
            _model.FallbackValue = _fallback.Text;
            _model.DesignValue = _design.Text;
        }

        private void Changed()
        {
            if (_loading)
            {
                return;
            }

            Read();
            _expression.Text = _model.Build();
            _previewTimer.Stop();
            _previewTimer.Start();
        }

        private void RefreshPreview()
        {
            var expression = _model.Build();
            if (expression.Length == 0 || _preview is null)
            {
                _shown.Text = string.Empty;
                return;
            }

            var result = _preview(expression, _sample.Text, _sampleShape, _want);
            _shown.Text = BindingStrings.Combine(result.Text is null ? "∅" : "« " + result.Text + " »", result.Note);
        }

        private ComboBox Combo(IEnumerable<string> items, string current, bool editable)
        {
            var combo = MakeComboBox(editable);
            foreach (var i in items)
            {
                combo.Items.Add(i);
            }

            if (editable)
            {
                combo.Text = current;
            }
            else
            {
                combo.SelectedItem = items.Contains(current) ? current : items.First();
            }

            return combo;
        }

        private static TextBlock Help(string text)
        {
            var block = MakeText(text, wrap: true);
            block.Opacity = 0.8;
            block.Margin = new Thickness(0, 0, 0, 4);
            return block;
        }
    }
}
