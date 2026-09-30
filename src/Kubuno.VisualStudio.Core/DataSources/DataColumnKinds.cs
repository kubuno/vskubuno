using System;
using System.Collections.Generic;

namespace Kubuno.VisualStudio.Core.DataSources
{
    /// <summary>What a column holds, as far as the designer cares (control, format, alignment).</summary>
    public enum DataColumnKind
    {
        Text,
        Integer,
        Decimal,
        Float,
        Boolean,
        Date,
        DateTime,
        Time,
        Binary,
    }

    /// <summary>How a table is dropped onto a view (the WinForms Data Sources drop-down of a table).</summary>
    public enum DataTableDropMode
    {
        /// <summary>A <c>&lt;DataTable&gt;</c> grid with one column per field (WinForms' DataGridView).</summary>
        Grid,

        /// <summary>A label and a bound control per field (WinForms' "Details").</summary>
        Details,
    }

    /// <summary>The control a column becomes in Details mode or when dropped alone (the WinForms drop-down of a column).</summary>
    public enum DataControlKind
    {
        TextField,
        NumericField,
        CheckBox,
        DatePicker,
        Label,

        /// <summary>The column is left out of Details drops.</summary>
        None,
    }

    /// <summary>Column classification and the per-kind defaults of a drop (control, grid width, format, alignment). Pure.</summary>
    public static class DataColumnKinds
    {
        /// <summary>The controls a column of <paramref name="kind"/> may become, in menu order.</summary>
        public static IReadOnlyList<DataControlKind> ControlsFor(DataColumnKind kind) => kind switch
        {
            DataColumnKind.Boolean => new[] { DataControlKind.CheckBox, DataControlKind.TextField, DataControlKind.Label, DataControlKind.None },
            DataColumnKind.Integer or DataColumnKind.Decimal or DataColumnKind.Float => new[] { DataControlKind.TextField, DataControlKind.NumericField, DataControlKind.Label, DataControlKind.None },
            DataColumnKind.Date or DataColumnKind.DateTime => new[] { DataControlKind.TextField, DataControlKind.DatePicker, DataControlKind.Label, DataControlKind.None },
            DataColumnKind.Binary => new[] { DataControlKind.Label, DataControlKind.None },
            _ => new[] { DataControlKind.TextField, DataControlKind.Label, DataControlKind.None },
        };

        /// <summary>
        /// Classifies a column from its Rust type first (what the typed rows use), then its database type (SQLite declares
        /// dates as text: <c>DATE</c> with a <c>String</c> Rust type is still a date for the designer).
        /// </summary>
        public static DataColumnKind Classify(string? dbType, string? rustType)
        {
            string rust = StripOption(rustType ?? string.Empty);
            string db = (dbType ?? string.Empty).ToUpperInvariant();

            // An exact number is a decimal for the designer (N2) even when the rows carry it as f64 (SQLite's NUMERIC).
            if ((db.Contains("NUMERIC") || db.Contains("DECIMAL") || db.Contains("MONEY")) && rust != "String" && !rust.StartsWith("i", StringComparison.Ordinal))
            {
                return DataColumnKind.Decimal;
            }

            switch (rust)
            {
                case "bool":
                    return DataColumnKind.Boolean;
                case "i8":
                case "i16":
                case "i32":
                case "i64":
                case "u8":
                case "u16":
                case "u32":
                case "u64":
                    return DataColumnKind.Integer;
                case "f32":
                case "f64":
                    return DataColumnKind.Float;
                case "Vec<u8>":
                    return DataColumnKind.Binary;
            }

            if (rust.EndsWith("NaiveDate", StringComparison.Ordinal) || rust.EndsWith("time::Date", StringComparison.Ordinal))
            {
                return DataColumnKind.Date;
            }

            if (rust.EndsWith("NaiveTime", StringComparison.Ordinal) || rust.EndsWith("time::Time", StringComparison.Ordinal))
            {
                return DataColumnKind.Time;
            }

            if (rust.Contains("DateTime") || rust.EndsWith("OffsetDateTime", StringComparison.Ordinal) || rust.EndsWith("PrimitiveDateTime", StringComparison.Ordinal))
            {
                return DataColumnKind.DateTime;
            }

            if (rust.EndsWith("Decimal", StringComparison.Ordinal) || rust.EndsWith("BigDecimal", StringComparison.Ordinal))
            {
                return DataColumnKind.Decimal;
            }

            // The database type (a String / text Rust type, or an unknown one).
            if (db.Contains("BOOL") || db == "BIT")
            {
                return DataColumnKind.Boolean;
            }

            if (db.Contains("NUMERIC") || db.Contains("DECIMAL") || db.Contains("MONEY"))
            {
                return DataColumnKind.Decimal;
            }

            if (db.Contains("TIMESTAMP") || db.Contains("DATETIME"))
            {
                return DataColumnKind.DateTime;
            }

            if (db.Contains("DATE"))
            {
                return DataColumnKind.Date;
            }

            if (db.StartsWith("TIME", StringComparison.Ordinal) || db == "INTERVAL")
            {
                return DataColumnKind.Time;
            }

            if (db.Contains("BLOB") || db.Contains("BYTEA") || db.Contains("BINARY") || db.Contains("IMAGE"))
            {
                return DataColumnKind.Binary;
            }

            if (rust == "String" || rust.Length > 0)
            {
                return DataColumnKind.Text;
            }

            if (db.Contains("INT") || db == "SERIAL" || db == "BIGSERIAL")
            {
                return DataColumnKind.Integer;
            }

            if (db.Contains("REAL") || db.Contains("FLOA") || db.Contains("DOUB"))
            {
                return DataColumnKind.Float;
            }

            return DataColumnKind.Text;
        }

        /// <summary>The control a column becomes by default (WinForms: a text box for everything but booleans).</summary>
        public static DataControlKind DefaultControl(KbdataColumnInfo column)
        {
            if (column.Kind == DataColumnKind.Binary)
            {
                return DataControlKind.None;
            }

            if (column.Kind == DataColumnKind.Boolean)
            {
                return DataControlKind.CheckBox;
            }

            // A value the database generates or that cannot be written is shown, not edited.
            return column.AutoIncrement || column.ReadOnly ? DataControlKind.Label : DataControlKind.TextField;
        }

        /// <summary>The <c>FormatString</c> of a grid column or a detail field (dates <c>d</c>, date-times <c>g</c>, decimals <c>N2</c>), null for none.</summary>
        public static string? FormatString(DataColumnKind kind) => kind switch
        {
            DataColumnKind.Date => "d",
            DataColumnKind.DateTime => "g",
            DataColumnKind.Time => "t",
            DataColumnKind.Decimal => "N2",
            _ => null,
        };

        /// <summary>Numbers are right-aligned in a grid.</summary>
        public static bool IsRightAligned(DataColumnKind kind) => kind is DataColumnKind.Integer or DataColumnKind.Decimal or DataColumnKind.Float;

        /// <summary>A grid column's width, DIP.</summary>
        public static int GridWidth(KbdataColumnInfo column) => column.Kind switch
        {
            DataColumnKind.Boolean => 70,
            DataColumnKind.Integer => 80,
            DataColumnKind.Decimal or DataColumnKind.Float => 100,
            DataColumnKind.Date or DataColumnKind.Time => 100,
            DataColumnKind.DateTime => 140,
            DataColumnKind.Binary => 80,
            _ => column.MaxLength > 0 && column.MaxLength <= 12 ? 90 : column.MaxLength > 0 && column.MaxLength <= 40 ? 140 : 160,
        };

        /// <summary>The width of a detail control, DIP.</summary>
        public static int DetailWidth(KbdataColumnInfo column, DataControlKind control) => control switch
        {
            DataControlKind.CheckBox => 160,
            DataControlKind.NumericField => 140,
            DataControlKind.DatePicker => 180,
            _ => column.Kind switch
            {
                DataColumnKind.Integer or DataColumnKind.Decimal or DataColumnKind.Float or DataColumnKind.Date or DataColumnKind.Time => 160,
                DataColumnKind.DateTime => 200,
                _ => 240,
            },
        };

        private static string StripOption(string rust)
        {
            string trimmed = rust.Replace(" ", string.Empty);
            return trimmed.StartsWith("Option<", StringComparison.Ordinal) && trimmed.EndsWith(">", StringComparison.Ordinal)
                ? trimmed.Substring(7, trimmed.Length - 8)
                : trimmed;
        }
    }
}
