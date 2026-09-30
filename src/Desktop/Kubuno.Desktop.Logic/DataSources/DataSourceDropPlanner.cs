using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Kubuno.Desktop.Logic.DataSources
{
    /// <summary>What is dropped from the Data Sources window onto a view, and where.</summary>
    public sealed class DataDropRequest
    {
        public DataDropRequest(KbdataSourceInfo source, KbdataTableInfo table, DataTableDropMode mode, IReadOnlyDictionary<string, DataControlKind>? controls, string? column = null)
        {
            Source = source;
            Table = table;
            Mode = mode;
            Controls = controls ?? new Dictionary<string, DataControlKind>();
            Column = column;
        }

        public KbdataSourceInfo Source { get; }

        public KbdataTableInfo Table { get; }

        public DataTableDropMode Mode { get; }

        /// <summary>The control chosen per column (Details mode and single columns); a missing column takes its default.</summary>
        public IReadOnlyDictionary<string, DataControlKind> Controls { get; }

        /// <summary>A single column dropped alone (a label and its control), or null for the whole table.</summary>
        public string? Column { get; }

        public DataControlKind ControlOf(KbdataColumnInfo column) =>
            Controls.TryGetValue(column.Name, out var kind) && DataColumnKinds.ControlsFor(column.Kind).Contains(kind) ? kind : DataColumnKinds.DefaultControl(column);
    }

    /// <summary>One <c>insertFragment</c> of a drop: <see cref="Xml"/> (one or more elements) as children of <see cref="ParentId"/> at <see cref="Index"/>.</summary>
    public sealed class DataDropInsertion
    {
        public DataDropInsertion(string parentId, int index, string xml)
        {
            ParentId = parentId;
            Index = index;
            Xml = xml;
        }

        public string ParentId { get; }

        public int Index { get; }

        public string Xml { get; }
    }

    /// <summary>The text edits of a drop (computed against one version of the view) and what to select afterwards.</summary>
    public sealed class DataDropPlan
    {
        public DataDropPlan(IReadOnlyList<DataDropInsertion> insertions, string description, string? selectElementId, IReadOnlyList<string> createdNames)
        {
            Insertions = insertions;
            Description = description;
            SelectElementId = selectElementId;
            CreatedNames = createdNames;
        }

        public IReadOnlyList<DataDropInsertion> Insertions { get; }

        /// <summary>The undo unit's name.</summary>
        public string Description { get; }

        /// <summary>The main new control's stable id once inserted (the grid, or the first field).</summary>
        public string? SelectElementId { get; }

        /// <summary>The <c>x:Name</c>s the drop creates, in document order.</summary>
        public IReadOnlyList<string> CreatedNames { get; }
    }

    /// <summary>
    /// Plans a drop from the Data Sources window onto a <c>.kbview</c> (docs/DATA.md §9, DATA-6; the WinForms behaviour): the data
    /// components the view lacks (<c>&lt;DbConnection&gt;</c> for the source's connection name, <c>&lt;TableAdapter&gt;</c>,
    /// <c>&lt;BindingSource AutoFill="true"&gt;</c>, <c>&lt;ErrorProvider&gt;</c>), inserted at the root after the view's existing data
    /// components (they live in the component tray), and the visual part at the drop point - a <c>&lt;BindingNavigator&gt;</c>
    /// above a <c>&lt;DataTable&gt;</c> (grid mode) or above a column of labels and bound controls (details mode), or a label and
    /// its control for a single column. Components already in the view for the same connection / table are reused, so a
    /// table dropped twice (grid then details) shares one binding source, like WinForms. Every new <c>x:Name</c> is unique
    /// in the view (suffix 1, 2...), so <c>kubuno-views-ls</c>'s own renaming never breaks a reference. Pure.
    /// </summary>
    public static class DataSourceDropPlanner
    {
        /// <summary>The data component elements (the component tray).</summary>
        public static readonly IReadOnlyCollection<string> DataComponents = new HashSet<string>(StringComparer.Ordinal)
        {
            "DbConnection", "DbCommand", "TableAdapter", "BindingSource", "ErrorProvider",
        };

        public const int NavigatorWidth = 360;
        public const int NavigatorHeight = 34;
        public const int GridHeight = 220;
        public const int RowHeight = 40;
        public const int LabelWidth = 120;
        public const int FieldHeight = 34;
        public const int Gap = 8;

        /// <summary>
        /// The plan of <paramref name="request"/> dropped into element <paramref name="parentId"/> at <paramref name="index"/> (its
        /// children count: an append) at <paramref name="x"/>, <paramref name="y"/> (DIP, local to that element), or at a free place
        /// below the view's content when they are null. Null (with <paramref name="error"/>) when the view cannot be read.
        /// </summary>
        public static DataDropPlan? Plan(string documentText, DataDropRequest request, string parentId, int? index, double? x, double? y, out string? error)
        {
            error = null;
            var outline = KbviewOutline.TryParse(documentText);
            if (outline is null)
            {
                error = DataSourcesText.ViewNotReadable;
                return null;
            }

            var parent = outline.Find(parentId) ?? outline.Root;
            parentId = parent.Id;
            int parentIndex = index is { } i && i >= 0 && i <= parent.Children.Count ? i : parent.Children.Count;
            var (left, top) = x is { } px && y is { } py ? ((int)Math.Round(px), (int)Math.Round(py)) : DefaultDropPoint(outline, parent);

            var taken = outline.Names();
            var created = new List<string>();
            var components = new List<string>();
            var source = request.Source;
            var table = request.Table;

            // The connection.
            string connection = FindConnection(outline, source.Connection) ?? CreateName(DataSourceNames.ToFieldName(source.Connection) + "_connection");
            if (FindConnection(outline, source.Connection) is null)
            {
                var attributes = new List<KeyValuePair<string, string>>
                {
                    Attr("x:Name", connection),
                    Attr("Provider", ProviderValue(source.Provider)),
                    Attr("ConnectionStringName", source.Connection),
                };
                if (source.Schema.Length > 0)
                {
                    attributes.Add(Attr("Schema", source.Schema));
                }

                components.Add(Element("DbConnection", attributes));
            }

            // The table adapter, the binding source and the error provider of the table.
            string? adapter = FindAdapter(outline, connection, table.Name);
            string? bindingSource = adapter is null ? null : FindBindingSource(outline, adapter);
            if (adapter is null)
            {
                adapter = CreateName(DataSourceNames.ToFieldName(table.BareName) + "_table_adapter");
                var attributes = new List<KeyValuePair<string, string>>
                {
                    Attr("x:Name", adapter),
                    Attr("Connection", connection),
                    Attr("SelectCommand", SelectCommand(source.Provider, table)),
                };
                if (!table.IsView)
                {
                    attributes.Add(Attr("UpdateTable", table.Name));
                    if (table.Key.Count > 0)
                    {
                        attributes.Add(Attr("PrimaryKey", string.Join(", ", table.Key)));
                        bool generated = table.Key.Count == 1 && table.Column(table.Key[0]) is { AutoIncrement: true };
                        if (!generated)
                        {
                            attributes.Add(Attr("AutoIncrementKey", "false"));
                        }
                    }
                }

                components.Add(Element("TableAdapter", attributes));
            }

            if (bindingSource is null)
            {
                bindingSource = CreateName(DataSourceNames.ToFieldName(table.BareName) + "_binding_source");
                components.Add(Element("BindingSource", new[] { Attr("x:Name", bindingSource), Attr("DataSource", adapter), Attr("AutoFill", "true") }));
            }

            if (!outline.All().Any(e => e.Name == "ErrorProvider" && e.Attribute("DataSource") == bindingSource))
            {
                string errors = CreateName(DataSourceNames.ToFieldName(table.BareName) + "_error_provider");
                components.Add(Element("ErrorProvider", new[] { Attr("x:Name", errors), Attr("DataSource", bindingSource) }));
            }

            // The visual part.
            var visuals = new List<string>();
            string? mainControl = null;
            bool hasNavigator = outline.All().Any(e => e.Name == "BindingNavigator" && e.Attribute("BindingSource") == bindingSource);
            int gridWidth = Math.Max(NavigatorWidth, Math.Min(900, table.Columns.Sum(DataColumnKinds.GridWidth) + 20));
            if ((parent.Number("Width") ?? parent.Number("DesignWidth")) is { } availableWidth)
            {
                // Within the container (its columns scroll), like a WinForms grid dropped near the form's edge.
                gridWidth = Math.Min(gridWidth, Math.Max(NavigatorWidth, (int)availableWidth - left - 16));
            }

            // A column dropped where the same list already has detail rows joins them: same label/field columns, the
            // next row (a consistent label/field grid rather than a field at another offset).
            if (request.Column is not null)
            {
                string bound = "Source=" + bindingSource + ", Path=";
                var rows = parent.Children
                    .Where(c => c.Name != "Label" && c.Number("X") is not null && c.Number("Y") is not null
                        && c.Attributes.Values.Any(v => v.IndexOf(bound, StringComparison.Ordinal) >= 0))
                    .ToList();
                if (rows.Count > 0)
                {
                    left = (int)Math.Round(rows.Min(c => c.Number("X")!.Value)) - LabelWidth;
                    top = (int)Math.Round(rows.Max(c => c.Number("Y")!.Value)) + RowHeight;
                    if (left < 0)
                    {
                        left = 0;
                    }
                }
            }

            // The block the drop adds never lies on existing controls: it moves down, below what it would cover.
            var (blockWidth, blockHeight) = BlockSize(request, table, hasNavigator, gridWidth);
            top = FreeTop(parent, left, top, blockWidth, blockHeight);
            int cursorY = top;
            if (request.Column is null && !hasNavigator)
            {
                string navigator = CreateName(DataSourceNames.ToFieldName(table.BareName) + "_binding_navigator");
                var attributes = new List<KeyValuePair<string, string>> { Attr("x:Name", navigator), Attr("BindingSource", bindingSource) };
                if (table.IsView)
                {
                    attributes.Add(Attr("ShowAddItem", "false"));
                    attributes.Add(Attr("ShowDeleteItem", "false"));
                    attributes.Add(Attr("ShowSaveItem", "false"));
                }

                attributes.AddRange(Bounds(left, cursorY, NavigatorWidth, NavigatorHeight));
                visuals.Add(Element("BindingNavigator", attributes));
                cursorY += NavigatorHeight + Gap;
            }

            if (request.Column is { } columnName)
            {
                if (table.Column(columnName) is not { } column)
                {
                    error = DataSourcesText.UnknownColumn(columnName, table.Name);
                    return null;
                }

                var control = request.ControlOf(column);
                if (control == DataControlKind.None)
                {
                    control = DataColumnKinds.DefaultControl(column) is var d && d != DataControlKind.None ? d : DataControlKind.Label;
                }

                mainControl = AddDetail(visuals, column, control, bindingSource, left, cursorY, table.IsView);
            }
            else if (request.Mode == DataTableDropMode.Grid)
            {
                mainControl = CreateName(DataSourceNames.ToFieldName(table.BareName) + "_data_table");
                int width = gridWidth;
                var attributes = new List<KeyValuePair<string, string>>
                {
                    Attr("x:Name", mainControl),
                    Attr("ItemsSource", "{Binding Source=" + bindingSource + "}"),
                    Attr("SelectedIndex", "{Binding Source=" + bindingSource + ", Path=Position, Mode=TwoWay}"),
                };
                attributes.AddRange(Bounds(left, cursorY, width, GridHeight));
                var columns = table.Columns.Select(c => Element("Column", GridColumnAttributes(c, table.IsView))).ToList();
                visuals.Add(Element("DataTable", attributes, columns));
            }
            else
            {
                foreach (var column in table.Columns)
                {
                    var control = request.ControlOf(column);
                    if (control == DataControlKind.None)
                    {
                        continue;
                    }

                    string name = AddDetail(visuals, column, control, bindingSource, left, cursorY, table.IsView);
                    mainControl ??= name;
                    cursorY += RowHeight;
                }
            }

            if (visuals.Count == 0 && components.Count == 0)
            {
                error = DataSourcesText.NothingToInsert;
                return null;
            }

            // Where the components go: at the root, after its last data component (else first).
            var root = outline.Root;
            int componentIndex = 0;
            for (int c = 0; c < root.Children.Count; c++)
            {
                if (DataComponents.Contains(root.Children[c].Name))
                {
                    componentIndex = c + 1;
                }
            }

            var insertions = new List<DataDropInsertion>();
            string? selectId = null;
            int mainIndexInVisuals = mainControl is null ? -1 : visuals.FindIndex(v => v.Contains("x:Name=\"" + mainControl + "\""));
            if (parentId.Length == 0 && componentIndex >= parentIndex)
            {
                // One insertion at the end of the root: the components, then the controls.
                var all = components.Concat(visuals).ToList();
                if (all.Count > 0)
                {
                    insertions.Add(new DataDropInsertion(string.Empty, parentIndex, Join(all)));
                }

                if (mainIndexInVisuals >= 0)
                {
                    selectId = (parentIndex + components.Count + mainIndexInVisuals).ToString(CultureInfo.InvariantCulture);
                }
            }
            else
            {
                if (components.Count > 0)
                {
                    insertions.Add(new DataDropInsertion(string.Empty, componentIndex, Join(components)));
                }

                if (visuals.Count > 0)
                {
                    insertions.Add(new DataDropInsertion(parentId, parentIndex, Join(visuals)));
                }

                // The inserted components shift the root's later children (the drop parent among them).
                string shiftedParent = ShiftRootChild(parentId, componentIndex, components.Count);
                if (mainIndexInVisuals >= 0)
                {
                    int local = (shiftedParent.Length == 0 ? parentIndex + (componentIndex <= parentIndex ? components.Count : 0) : parentIndex) + mainIndexInVisuals;
                    selectId = shiftedParent.Length == 0 ? local.ToString(CultureInfo.InvariantCulture) : shiftedParent + "." + local.ToString(CultureInfo.InvariantCulture);
                }
            }

            string description = request.Column is { } col ? DataSourcesText.DropColumnUndo(col) : DataSourcesText.DropTableUndo(table.Name);
            return new DataDropPlan(insertions, description, selectId, created);

            string CreateName(string baseName)
            {
                string name = DataSourceNames.Unique(baseName, taken);
                created.Add(name);
                return name;
            }

            string AddDetail(List<string> into, KbdataColumnInfo column, DataControlKind control, string bs, int x0, int y0, bool readOnlyTable)
            {
                string field = DataSourceNames.ToFieldName(column.Name);
                string label = DataSourceNames.Humanize(column.Name);
                string binding = "{Binding Source=" + bs + ", Path=" + column.Name;
                string? format = DataColumnKinds.FormatString(column.Kind);
                bool writable = !readOnlyTable && !column.ReadOnly && !column.AutoIncrement;
                string mode = writable ? ", Mode=TwoWay}" : "}";
                int width = DataColumnKinds.DetailWidth(column, control);
                if (control == DataControlKind.CheckBox)
                {
                    string name = CreateName(field + "_check_box");
                    var attributes = new List<KeyValuePair<string, string>> { Attr("x:Name", name), Attr("Text", label), Attr("Checked", binding + mode) };
                    attributes.AddRange(Bounds(x0 + LabelWidth, y0 + 4, width, 24));
                    into.Add(Element("CheckBox", attributes));
                    return name;
                }

                string labelName = CreateName(field + "_label");
                into.Add(Element("Label", new[] { Attr("x:Name", labelName), Attr("Text", label) }.Concat(Bounds(x0, y0 + 4, LabelWidth - Gap, 26))));
                string controlName;
                var controlAttributes = new List<KeyValuePair<string, string>>();
                switch (control)
                {
                    case DataControlKind.NumericField:
                        controlName = CreateName(field + "_numeric_field");
                        controlAttributes.Add(Attr("x:Name", controlName));
                        controlAttributes.Add(Attr("Value", binding + mode));
                        controlAttributes.Add(Attr("Minimum", "-1000000000"));
                        controlAttributes.Add(Attr("Maximum", "1000000000"));
                        if (column.Kind != DataColumnKind.Integer)
                        {
                            controlAttributes.Add(Attr("DecimalPlaces", "2"));
                        }

                        break;
                    case DataControlKind.DatePicker:
                        controlName = CreateName(field + "_date_picker");
                        controlAttributes.Add(Attr("x:Name", controlName));
                        controlAttributes.Add(Attr("Date", binding + mode));
                        break;
                    case DataControlKind.Label:
                        controlName = CreateName(field + "_value_label");
                        controlAttributes.Add(Attr("x:Name", controlName));
                        controlAttributes.Add(Attr("Text", binding + (format != null ? ", FormatString=" + format : string.Empty) + "}"));
                        break;
                    default:
                        controlName = CreateName(field + "_text_field");
                        controlAttributes.Add(Attr("x:Name", controlName));
                        var text = new StringBuilder(binding);
                        if (format != null)
                        {
                            text.Append(", FormatString=").Append(format);
                        }

                        if (column.Nullable)
                        {
                            text.Append(", NullValue=''");
                        }

                        text.Append(mode);
                        controlAttributes.Add(Attr("Text", text.ToString()));
                        if (!writable)
                        {
                            // WinForms' ReadOnly TextBox for a generated key: shown, not editable.
                            controlAttributes.Add(Attr("Enabled", "false"));
                        }

                        break;
                }

                controlAttributes.AddRange(Bounds(x0 + LabelWidth, y0, width, FieldHeight));
                into.Add(Element(control == DataControlKind.NumericField ? "NumericField" : control == DataControlKind.DatePicker ? "DatePicker" : control == DataControlKind.Label ? "Label" : "TextField", controlAttributes));
                return controlName;
            }
        }

        /// <summary>The attributes of a grid <c>&lt;Column&gt;</c>: header, binding, width, format and alignment by type, read-only when generated.</summary>
        public static List<KeyValuePair<string, string>> GridColumnAttributes(KbdataColumnInfo column, bool readOnlyTable)
        {
            var attributes = new List<KeyValuePair<string, string>>
            {
                Attr("Header", DataSourceNames.Humanize(column.Name)),
                Attr("Binding", "{Binding " + column.Name + "}"),
                Attr("Width", DataColumnKinds.GridWidth(column).ToString(CultureInfo.InvariantCulture)),
            };
            if (DataColumnKinds.FormatString(column.Kind) is { } format)
            {
                attributes.Add(Attr("FormatString", format));
            }

            if (DataColumnKinds.IsRightAligned(column.Kind))
            {
                attributes.Add(Attr("Alignment", "Right"));
            }

            if (readOnlyTable || column.AutoIncrement || column.ReadOnly || column.Kind == DataColumnKind.Binary)
            {
                attributes.Add(Attr("ReadOnly", "true"));
            }

            return attributes;
        }

        /// <summary><c>SELECT id, name FROM customers</c>: every column, identifiers quoted only when they need it.</summary>
        public static string SelectCommand(string provider, KbdataTableInfo table)
        {
            string columns = string.Join(", ", table.Columns.Select(c => QuoteIdentifier(provider, c.Name)));
            string from = string.Join(".", table.Name.Split('.').Select(p => QuoteIdentifier(provider, p)));
            return "SELECT " + (columns.Length == 0 ? "*" : columns) + " FROM " + from;
        }

        /// <summary>An identifier as SQL text: bare when it is a plain lower-case name that is no reserved word, else quoted for <paramref name="provider"/>.</summary>
        public static string QuoteIdentifier(string provider, string name)
        {
            if (Regex.IsMatch(name, "^[a-z_][a-z0-9_]*$") && !SqlReserved.Contains(name.ToUpperInvariant()))
            {
                return name;
            }

            return provider.ToLowerInvariant() switch
            {
                "mysql" => "`" + name.Replace("`", "``") + "`",
                "sqlserver" => "[" + name.Replace("]", "]]") + "]",
                _ => "\"" + name.Replace("\"", "\"\"") + "\"",
            };
        }

        /// <summary>The <c>Provider</c> attribute of a <c>&lt;DbConnection&gt;</c> (kubuno-data's <c>Provider</c> enum).</summary>
        public static string ProviderValue(string provider) => provider.ToLowerInvariant() switch
        {
            "sqlite" => "Sqlite",
            "mysql" => "MySql",
            "sqlserver" => "SqlServer",
            _ => "Postgres",
        };

        /// <summary>The size of the controls a drop adds (navigator, grid or detail rows), for <see cref="FreeTop"/>.</summary>
        public static (int Width, int Height) BlockSize(DataDropRequest request, KbdataTableInfo table, bool hasNavigator, int gridWidth)
        {
            int width = 0;
            int height = 0;
            if (request.Column is null && !hasNavigator)
            {
                width = NavigatorWidth;
                height = NavigatorHeight + Gap;
            }

            if (request.Column is { } name)
            {
                if (table.Column(name) is { } column)
                {
                    var control = request.ControlOf(column);
                    if (control == DataControlKind.None)
                    {
                        control = DataColumnKinds.DefaultControl(column) is var d && d != DataControlKind.None ? d : DataControlKind.Label;
                    }

                    width = Math.Max(width, LabelWidth + DataColumnKinds.DetailWidth(column, control));
                    height += RowHeight;
                }
            }
            else if (request.Mode == DataTableDropMode.Grid)
            {
                width = Math.Max(width, gridWidth);
                height += GridHeight;
            }
            else
            {
                foreach (var column in table.Columns)
                {
                    var control = request.ControlOf(column);
                    if (control != DataControlKind.None)
                    {
                        width = Math.Max(width, LabelWidth + DataColumnKinds.DetailWidth(column, control));
                        height += RowHeight;
                    }
                }
            }

            return (width, height);
        }

        /// <summary>
        /// The first top, from <paramref name="top"/> down, where a block of <paramref name="width"/> x <paramref name="height"/> at
        /// <paramref name="left"/> covers none of <paramref name="parent"/>'s positioned children (like the Windows Forms designer, a
        /// drop never lands on existing controls): each time the block would cover a child, it moves below it.
        /// </summary>
        public static int FreeTop(KbviewElement parent, int left, int top, int width, int height)
        {
            var occupied = parent.Children
                .Where(c => !DataComponents.Contains(c.Name) && c.Number("X") is not null && c.Number("Y") is not null)
                .Select(c => (X: c.Number("X")!.Value, Y: c.Number("Y")!.Value, W: c.Number("Width") ?? 100, H: c.Number("Height") ?? 30))
                .ToList();
            for (int guard = 0; guard <= occupied.Count; guard++)
            {
                double bottom = -1;
                foreach (var r in occupied)
                {
                    bool overlaps = left < r.X + r.W && r.X < left + width && top < r.Y + r.H && r.Y < top + height;
                    if (overlaps)
                    {
                        bottom = Math.Max(bottom, r.Y + r.H);
                    }
                }

                if (bottom < 0)
                {
                    break;
                }

                top = (int)Math.Ceiling(bottom) + Gap;
            }

            return top;
        }

        /// <summary>A free place for a drop without a point: below the lowest positioned child of <paramref name="parent"/>.</summary>
        public static (int X, int Y) DefaultDropPoint(KbviewOutline outline, KbviewElement parent)
        {
            double bottom = 0;
            foreach (var child in parent.Children)
            {
                if (child.Number("Y") is { } y)
                {
                    bottom = Math.Max(bottom, y + (child.Number("Height") ?? 30));
                }
            }

            return (16, (int)Math.Round(bottom) + 16);
        }

        /// <summary>The x:Name of the view's <c>&lt;DbConnection&gt;</c> for connection string <paramref name="connectionName"/>, or null.</summary>
        public static string? FindConnection(KbviewOutline outline, string connectionName) =>
            outline.All().FirstOrDefault(e => e.Name == "DbConnection" && string.Equals(e.Attribute("ConnectionStringName"), connectionName, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(e.XName))?.XName;

        /// <summary>The view's <c>&lt;TableAdapter&gt;</c> of <paramref name="table"/> on <paramref name="connection"/> (by <c>UpdateTable</c>, else by its <c>FROM</c>), or null.</summary>
        public static string? FindAdapter(KbviewOutline outline, string connection, string table)
        {
            var pattern = new Regex(@"\bFROM\s+[""`\[]?" + Regex.Escape(table) + @"[""`\]]?(\s|$|;)", RegexOptions.IgnoreCase);
            return outline.All().FirstOrDefault(e =>
                e.Name == "TableAdapter" && !string.IsNullOrEmpty(e.XName) && e.Attribute("Connection") == connection &&
                (string.Equals(e.Attribute("UpdateTable"), table, StringComparison.OrdinalIgnoreCase) || (e.Attribute("SelectCommand") is { } select && pattern.IsMatch(select))))?.XName;
        }

        /// <summary>The view's <c>&lt;BindingSource&gt;</c> over <paramref name="adapter"/> (a list, not a detail of a relation), or null.</summary>
        public static string? FindBindingSource(KbviewOutline outline, string adapter) =>
            outline.All().FirstOrDefault(e => e.Name == "BindingSource" && e.Attribute("DataSource") == adapter && !string.IsNullOrEmpty(e.XName))?.XName;

        /// <summary>Escapes an attribute value for a double-quoted XML attribute.</summary>
        public static string EscapeAttribute(string value) =>
            value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("\r", "&#13;").Replace("\n", "&#10;");

        private static string ShiftRootChild(string id, int insertedAt, int count)
        {
            if (id.Length == 0 || count == 0)
            {
                return id;
            }

            int dot = id.IndexOf('.');
            string first = dot < 0 ? id : id.Substring(0, dot);
            if (!int.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index < insertedAt)
            {
                return id;
            }

            return (index + count).ToString(CultureInfo.InvariantCulture) + (dot < 0 ? string.Empty : id.Substring(dot));
        }

        private static IEnumerable<KeyValuePair<string, string>> Bounds(int x, int y, int width, int height) => new[]
        {
            Attr("X", x.ToString(CultureInfo.InvariantCulture)),
            Attr("Y", y.ToString(CultureInfo.InvariantCulture)),
            Attr("Width", width.ToString(CultureInfo.InvariantCulture)),
            Attr("Height", height.ToString(CultureInfo.InvariantCulture)),
        };

        private static KeyValuePair<string, string> Attr(string name, string value) => new KeyValuePair<string, string>(name, value);

        private static string Element(string name, IEnumerable<KeyValuePair<string, string>> attributes, IReadOnlyList<string>? children = null)
        {
            var builder = new StringBuilder("<").Append(name);
            foreach (var attribute in attributes)
            {
                builder.Append(' ').Append(attribute.Key).Append("=\"").Append(EscapeAttribute(attribute.Value)).Append('"');
            }

            if (children is null || children.Count == 0)
            {
                return builder.Append("/>").ToString();
            }

            builder.Append('>');
            foreach (var child in children)
            {
                builder.Append("\n  ").Append(child);
            }

            return builder.Append('\n').Append("</").Append(name).Append('>').ToString();
        }

        private static string Join(IEnumerable<string> elements) => string.Join("\n", elements);

        private static readonly HashSet<string> SqlReserved = new HashSet<string>(StringComparer.Ordinal)
        {
            "ALL", "AND", "AS", "ASC", "BETWEEN", "BY", "CASE", "CHECK", "COLUMN", "CONSTRAINT", "CREATE", "CROSS", "DEFAULT", "DELETE",
            "DESC", "DISTINCT", "DROP", "ELSE", "END", "EXISTS", "FOR", "FOREIGN", "FROM", "FULL", "GROUP", "HAVING", "IN", "INDEX",
            "INNER", "INSERT", "INTO", "IS", "JOIN", "KEY", "LEFT", "LIKE", "LIMIT", "NOT", "NULL", "OFFSET", "ON", "OR", "ORDER",
            "OUTER", "PRIMARY", "REFERENCES", "RIGHT", "SELECT", "SET", "TABLE", "THEN", "TO", "UNION", "UNIQUE", "UPDATE", "USER",
            "USING", "VALUES", "WHEN", "WHERE", "WITH", "GRANT", "ANALYZE", "WINDOW", "RANGE", "ROWS", "ROW",
        };
    }
}
