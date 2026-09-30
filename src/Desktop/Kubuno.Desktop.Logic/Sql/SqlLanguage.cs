using System;
using System.Collections.Generic;
using System.Linq;

namespace Kubuno.VisualStudio.Core.Sql
{
    /// <summary>The SQL words the colouring and the completion know (common to PostgreSQL, SQLite, MySQL and SQL Server).</summary>
    public static class SqlLanguage
    {
        /// <summary>
        /// Keywords coloured as such. Words that are common column names (<c>name</c>, <c>type</c>, <c>date</c>, <c>text</c>,
        /// <c>status</c>, <c>value</c>...) are deliberately left out: they are coloured as names.
        /// </summary>
        private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ADD", "ALL", "ALTER", "ANALYZE", "AND", "ANY", "AS", "ASC", "AUTOINCREMENT", "AUTO_INCREMENT", "BEGIN", "BETWEEN",
            "BY", "CASCADE", "CASE", "CAST", "CHECK", "COLLATE", "COLUMN", "COMMIT", "CONFLICT", "CONSTRAINT", "CREATE", "CROSS",
            "CURRENT", "DEFAULT", "DEFERRABLE", "DELETE", "DESC", "DISTINCT", "DO", "DROP", "ELSE", "END", "ESCAPE", "EXCEPT",
            "EXISTS", "EXPLAIN", "FALSE", "FETCH", "FILTER", "FIRST", "FOLLOWING", "FOR", "FOREIGN", "FROM", "FULL", "GLOB",
            "GRANT", "GROUP", "HAVING", "IF", "IGNORE", "ILIKE", "IN", "INDEX", "INNER", "INSERT", "INTERSECT", "INTO", "IS",
            "ISNULL", "JOIN", "KEY", "LAST", "LATERAL", "LEFT", "LIKE", "LIMIT", "LOCKED", "MATCHED", "MATERIALIZED", "MERGE",
            "NATURAL", "NEXT", "NO", "NOT", "NOTHING", "NOTNULL", "NOWAIT", "NULL", "NULLS", "OF", "OFFSET", "ON", "ONLY", "OR",
            "ORDER", "OUTER", "OVER", "PARTITION", "PRAGMA", "PRECEDING", "PRIMARY", "RECURSIVE", "REFERENCES", "REGEXP",
            "RENAME", "REPLACE", "RESTRICT", "RETURNING", "REVOKE", "RIGHT", "ROLLBACK", "ROW", "ROWS", "SAVEPOINT", "SCHEMA",
            "SELECT", "SET", "SHARE", "SIMILAR", "SKIP", "SOME", "TABLE", "TEMP", "TEMPORARY", "THEN", "TIES", "TO", "TOP",
            "TRANSACTION", "TRUE", "TRUNCATE", "UNBOUNDED", "UNION", "UNIQUE", "UPDATE", "USING", "VACUUM", "VALUES", "VIEW",
            "WHEN", "WHERE", "WINDOW", "WITH", "WITHOUT",

            // Type names that are not usual column names.
            "BIGINT", "BIGSERIAL", "BOOLEAN", "BYTEA", "CHAR", "DECIMAL", "DOUBLE", "FLOAT", "INT", "INTEGER", "INTERVAL",
            "JSONB", "NUMERIC", "NVARCHAR", "PRECISION", "REAL", "SERIAL", "SMALLINT", "TIMESTAMP", "TIMESTAMPTZ", "UUID",
            "VARCHAR", "VARYING", "BLOB", "DATETIME2", "TINYINT",
        };

        /// <summary>Built-in functions (lower case).</summary>
        private static readonly HashSet<string> Functions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "abs", "array_agg", "avg", "bool_and", "bool_or", "ceil", "ceiling", "char_length", "coalesce", "concat", "concat_ws",
            "count", "cume_dist", "current_date", "current_time", "current_timestamp", "date_part", "date_trunc", "datetime",
            "dense_rank", "extract", "first_value", "floor", "format", "gen_random_uuid", "generate_series", "greatest",
            "group_concat", "ifnull", "iif", "instr", "json_agg", "json_build_object", "json_extract", "json_object",
            "jsonb_agg", "jsonb_build_object", "julianday", "lag", "last_insert_rowid", "last_value", "lead", "least", "left",
            "length", "localtimestamp", "lower", "ltrim", "max", "md5", "min", "mod", "now", "nth_value", "ntile", "nullif",
            "percent_rank", "position", "power", "printf", "random", "rank", "replace", "right", "round", "row_number",
            "rtrim", "sqrt", "strftime", "string_agg", "substr", "substring", "sum", "to_char", "to_date", "to_timestamp",
            "total", "trim", "trunc", "unixepoch", "upper", "uuid_generate_v4", "getdate", "sysdatetime", "isnull", "len",
            "newid", "date", "time", "char",
        };

        /// <summary>Functions called without parentheses (<c>CURRENT_TIMESTAMP</c>).</summary>
        private static readonly HashSet<string> Niladic = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "current_date", "current_time", "current_timestamp", "localtimestamp",
        };

        /// <summary>The keywords the completion offers (the statement and clause words people type).</summary>
        public static readonly IReadOnlyList<string> CompletionKeywords = new[]
        {
            "SELECT", "FROM", "WHERE", "AND", "OR", "NOT", "NULL", "IS", "IN", "EXISTS", "BETWEEN", "LIKE", "ILIKE", "AS",
            "JOIN", "INNER", "LEFT", "RIGHT", "FULL", "OUTER", "CROSS", "ON", "USING", "GROUP", "ORDER", "BY", "HAVING",
            "ASC", "DESC", "NULLS", "LIMIT", "OFFSET", "FETCH", "DISTINCT", "UNION", "ALL", "INTERSECT", "EXCEPT", "INSERT",
            "INTO", "VALUES", "DEFAULT", "UPDATE", "SET", "DELETE", "RETURNING", "WITH", "RECURSIVE", "CASE", "WHEN", "THEN",
            "ELSE", "END", "CAST", "CONFLICT", "DO", "NOTHING", "TRUE", "FALSE", "OVER", "PARTITION", "CREATE", "TABLE",
            "INDEX", "VIEW", "ALTER", "DROP", "PRIMARY", "KEY", "FOREIGN", "REFERENCES", "UNIQUE", "CHECK", "CONSTRAINT",
            "IF", "BEGIN", "COMMIT", "ROLLBACK",
        };

        /// <summary>The built-in functions the completion offers, with their signature.</summary>
        public static readonly IReadOnlyList<(string Name, string Signature)> CompletionFunctions = new[]
        {
            ("COUNT", "COUNT(*) / COUNT(expr)"), ("SUM", "SUM(expr)"), ("AVG", "AVG(expr)"), ("MIN", "MIN(expr)"),
            ("MAX", "MAX(expr)"), ("COALESCE", "COALESCE(value, …)"), ("NULLIF", "NULLIF(a, b)"), ("LOWER", "LOWER(text)"),
            ("UPPER", "UPPER(text)"), ("LENGTH", "LENGTH(text)"), ("TRIM", "TRIM(text)"), ("SUBSTR", "SUBSTR(text, start, length)"),
            ("REPLACE", "REPLACE(text, from, to)"), ("ROUND", "ROUND(number, digits)"), ("ABS", "ABS(number)"),
            ("NOW", "NOW()"), ("CURRENT_TIMESTAMP", "CURRENT_TIMESTAMP"), ("ROW_NUMBER", "ROW_NUMBER() OVER (…)"),
            ("STRING_AGG", "STRING_AGG(text, separator)"), ("GROUP_CONCAT", "GROUP_CONCAT(text, separator)"),
            ("ARRAY_AGG", "ARRAY_AGG(expr)"), ("DATE_TRUNC", "DATE_TRUNC(field, timestamp)"), ("EXTRACT", "EXTRACT(field FROM value)"),
        };

        /// <summary>The words after which a space opens the table list.</summary>
        public static readonly IReadOnlyCollection<string> TableTriggerKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "FROM", "JOIN", "INTO", "UPDATE", "TABLE",
        };

        /// <summary>The words after which a space opens the column list (when the statement names tables).</summary>
        public static readonly IReadOnlyCollection<string> ColumnTriggerKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "WHERE", "ON", "AND", "OR", "BY", "SET", "HAVING", "RETURNING", "DISTINCT",
        };

        public static bool IsKeyword(string word) => Keywords.Contains(word);

        public static bool IsFunction(string word) => Functions.Contains(word);

        public static bool IsNiladicFunction(string word) => Niladic.Contains(word);

        /// <summary>Names of schemas, tables and prefixes that belong to the database engine itself (never "unknown").</summary>
        public static bool IsSystemName(string name) =>
            name.StartsWith("pg_", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("sys", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("_sqlx", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "information_schema", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "mysql", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "performance_schema", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "dual", StringComparison.OrdinalIgnoreCase);

        internal static IEnumerable<string> AllKeywords => Keywords.OrderBy(k => k, StringComparer.Ordinal);
    }
}
