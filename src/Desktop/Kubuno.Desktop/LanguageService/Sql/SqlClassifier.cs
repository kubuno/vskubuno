using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Windows.Media;
using Kubuno.Desktop.Logic.Sql;
using Kubuno.Rust.LanguageService.IntelliSense;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace Kubuno.Desktop.LanguageService.Sql
{
    /// <summary>
    /// The classifications of SQL inside Rust strings (docs/DATA.md DATA-8), listed in Tools &gt; Options &gt; Environment &gt;
    /// Fonts and Colors as "Kubuno SQL - ...". Their formats are ordered after <see cref="Priority.High"/>, so where they
    /// overlap the Rust string colour (TextMate's <c>string</c> and rust-analyzer's <c>string</c> semantic token, both of
    /// default priority) the SQL colour wins.
    /// </summary>
    internal static class SqlClassificationTypes
    {
        public const string Keyword = "Kubuno SQL - Keyword";
        public const string Name = "Kubuno SQL - Name";
        public const string Function = "Kubuno SQL - Function";
        public const string Number = "Kubuno SQL - Number";
        public const string String = "Kubuno SQL - String";
        public const string Comment = "Kubuno SQL - Comment";
        public const string Parameter = "Kubuno SQL - Parameter";
        public const string Operator = "Kubuno SQL - Operator";

        /// <summary>The escape-character format Roslyn defines above plain strings: SQL must also win over it.</summary>
        public const string StringEscape = "string - escape character";

        /// <summary>Every type with its default colour in the dark and the light theme.</summary>
        public static readonly IReadOnlyList<(string Name, Color Dark, Color Light)> Colors = new[]
        {
            (Keyword, Rgb(0x56, 0x9C, 0xD6), Rgb(0x00, 0x00, 0xFF)),
            (Name, Rgb(0xDC, 0xDC, 0xDC), Rgb(0x00, 0x00, 0x00)),
            (Function, Rgb(0xDC, 0xDC, 0xAA), Rgb(0x74, 0x53, 0x1F)),
            (Number, Rgb(0xB5, 0xCE, 0xA8), Rgb(0x09, 0x86, 0x58)),
            (String, Rgb(0xF4, 0x87, 0x71), Rgb(0xC8, 0x10, 0x2E)),
            (Comment, Rgb(0x57, 0xA6, 0x4A), Rgb(0x00, 0x80, 0x00)),
            (Parameter, Rgb(0x9C, 0xDC, 0xFE), Rgb(0x1F, 0x37, 0x7F)),
            (Operator, Rgb(0xB4, 0xB4, 0xB4), Rgb(0x80, 0x80, 0x80)),
        };

        [Export]
        [Name(Keyword)]
        internal static ClassificationTypeDefinition KeywordType = null!;

        [Export]
        [Name(Name)]
        internal static ClassificationTypeDefinition NameType = null!;

        [Export]
        [Name(Function)]
        internal static ClassificationTypeDefinition FunctionType = null!;

        [Export]
        [Name(Number)]
        internal static ClassificationTypeDefinition NumberType = null!;

        [Export]
        [Name(String)]
        internal static ClassificationTypeDefinition StringType = null!;

        [Export]
        [Name(Comment)]
        internal static ClassificationTypeDefinition CommentType = null!;

        [Export]
        [Name(Parameter)]
        internal static ClassificationTypeDefinition ParameterType = null!;

        [Export]
        [Name(Operator)]
        internal static ClassificationTypeDefinition OperatorType = null!;

        public static string ForToken(SqlTokenKind kind) => kind switch
        {
            SqlTokenKind.Keyword => Keyword,
            SqlTokenKind.Identifier => Name,
            SqlTokenKind.QuotedIdentifier => Name,
            SqlTokenKind.Function => Function,
            SqlTokenKind.Number => Number,
            SqlTokenKind.String => String,
            SqlTokenKind.Comment => Comment,
            SqlTokenKind.Parameter => Parameter,
            _ => Operator,
        };

        /// <summary>The default colour of <paramref name="name"/> for a theme.</summary>
        public static Color DefaultColor(string name, bool dark)
        {
            foreach (var entry in Colors)
            {
                if (entry.Name == name)
                {
                    return dark ? entry.Dark : entry.Light;
                }
            }

            return dark ? Rgb(0xDC, 0xDC, 0xDC) : Rgb(0, 0, 0);
        }

        private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
    }

    internal abstract class SqlFormat : ClassificationFormatDefinition
    {
        protected SqlFormat(string name, string english, string french)
        {
            DisplayName = Designer.DesignerText.IsFrench ? "Kubuno SQL - " + french : "Kubuno SQL - " + english;
            ForegroundColor = SqlClassificationTypes.DefaultColor(name, Kubuno.Core.UI.VsTheme.IsDark());
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = SqlClassificationTypes.Keyword)]
    [Name(SqlClassificationTypes.Keyword)]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    [Order(After = SqlClassificationTypes.StringEscape)]
    internal sealed class SqlKeywordFormat : SqlFormat
    {
        public SqlKeywordFormat()
            : base(SqlClassificationTypes.Keyword, "Keyword", "Mot clé")
        {
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = SqlClassificationTypes.Name)]
    [Name(SqlClassificationTypes.Name)]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    [Order(After = SqlClassificationTypes.StringEscape)]
    internal sealed class SqlNameFormat : SqlFormat
    {
        public SqlNameFormat()
            : base(SqlClassificationTypes.Name, "Name (table, column)", "Nom (table, colonne)")
        {
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = SqlClassificationTypes.Function)]
    [Name(SqlClassificationTypes.Function)]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    [Order(After = SqlClassificationTypes.StringEscape)]
    internal sealed class SqlFunctionFormat : SqlFormat
    {
        public SqlFunctionFormat()
            : base(SqlClassificationTypes.Function, "Function", "Fonction")
        {
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = SqlClassificationTypes.Number)]
    [Name(SqlClassificationTypes.Number)]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    [Order(After = SqlClassificationTypes.StringEscape)]
    internal sealed class SqlNumberFormat : SqlFormat
    {
        public SqlNumberFormat()
            : base(SqlClassificationTypes.Number, "Number", "Nombre")
        {
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = SqlClassificationTypes.String)]
    [Name(SqlClassificationTypes.String)]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    [Order(After = SqlClassificationTypes.StringEscape)]
    internal sealed class SqlStringFormat : SqlFormat
    {
        public SqlStringFormat()
            : base(SqlClassificationTypes.String, "String", "Chaîne")
        {
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = SqlClassificationTypes.Comment)]
    [Name(SqlClassificationTypes.Comment)]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    [Order(After = SqlClassificationTypes.StringEscape)]
    internal sealed class SqlCommentFormat : SqlFormat
    {
        public SqlCommentFormat()
            : base(SqlClassificationTypes.Comment, "Comment", "Commentaire")
        {
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = SqlClassificationTypes.Parameter)]
    [Name(SqlClassificationTypes.Parameter)]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    [Order(After = SqlClassificationTypes.StringEscape)]
    internal sealed class SqlParameterFormat : SqlFormat
    {
        public SqlParameterFormat()
            : base(SqlClassificationTypes.Parameter, "Parameter", "Paramètre")
        {
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = SqlClassificationTypes.Operator)]
    [Name(SqlClassificationTypes.Operator)]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    [Order(After = SqlClassificationTypes.StringEscape)]
    internal sealed class SqlOperatorFormat : SqlFormat
    {
        public SqlOperatorFormat()
            : base(SqlClassificationTypes.Operator, "Operator", "Opérateur")
        {
        }
    }

    /// <summary>Colours the SQL of <c>query!</c>/<c>query_as!</c>/<c>DbCommand::with_text</c>... strings.</summary>
    [Export(typeof(IClassifierProvider))]
    [ContentType(Kubuno.Rust.Constants.RustContentType)]
    [Name("Kubuno SQL in Rust strings")]
    internal sealed class SqlClassifierProvider : IClassifierProvider
    {
        [Import]
        internal IClassificationTypeRegistryService Registry { get; set; } = null!;

        public IClassifier GetClassifier(ITextBuffer textBuffer) =>
            textBuffer.Properties.GetOrCreateSingletonProperty(typeof(SqlClassifier), () => new SqlClassifier(textBuffer, Registry));
    }

    internal sealed class SqlClassifier : IClassifier
    {
        private static readonly IList<ClassificationSpan> Empty = new ClassificationSpan[0];

        private readonly SqlBufferState _state;
        private readonly Dictionary<SqlTokenKind, IClassificationType> _types = new Dictionary<SqlTokenKind, IClassificationType>();

        public SqlClassifier(ITextBuffer buffer, IClassificationTypeRegistryService registry)
        {
            _state = SqlBufferState.Get(buffer);
            foreach (SqlTokenKind kind in Enum.GetValues(typeof(SqlTokenKind)))
            {
                _types[kind] = registry.GetClassificationType(SqlClassificationTypes.ForToken(kind));
            }

            _state.LiteralsChanged += (_, e) => ClassificationChanged?.Invoke(this, new ClassificationChangedEventArgs(e.Span));
        }

        public event EventHandler<ClassificationChangedEventArgs>? ClassificationChanged;

        public IList<ClassificationSpan> GetClassificationSpans(SnapshotSpan span)
        {
            var literals = _state.GetLiterals(span.Snapshot);
            if (literals.Count == 0)
            {
                return Empty;
            }

            List<ClassificationSpan>? result = null;
            foreach (var literal in SqlBufferState.Intersecting(literals, span.Span))
            {
                int from = Math.Max(span.Start.Position, literal.ContentStart);
                int to = Math.Min(span.End.Position, literal.ContentEnd);
                if (to <= from)
                {
                    continue;
                }

                var tokens = literal.Sql.Tokens;
                int sqlFrom = literal.ToSql(from);
                int sqlTo = literal.ToSql(to);
                for (int i = FirstEndingAfter(tokens, sqlFrom); i < tokens.Count && tokens[i].Start <= sqlTo; i++)
                {
                    var token = tokens[i];
                    int start = Math.Max(from, literal.ToDocument(token.Start));
                    int end = Math.Min(to, literal.ToDocument(token.End));
                    if (end > start)
                    {
                        result ??= new List<ClassificationSpan>();
                        result.Add(new ClassificationSpan(new SnapshotSpan(span.Snapshot, start, end - start), _types[token.Kind]));
                    }
                }
            }

            return result ?? Empty;
        }

        private static int FirstEndingAfter(IReadOnlyList<SqlToken> tokens, int offset)
        {
            int low = 0;
            int high = tokens.Count;
            while (low < high)
            {
                int mid = (low + high) / 2;
                if (tokens[mid].End <= offset)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }

            return low;
        }
    }

    /// <summary>
    /// Keeps the SQL colours right for the theme (a format's colour is read once): on a dark ⇄ light switch, each SQL
    /// colour still at the other theme's default moves to this theme's; a colour the user chose is left alone.
    /// </summary>
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType(Kubuno.Rust.Constants.RustContentType)]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class SqlClassificationThemeListener : IWpfTextViewCreationListener
    {
        private static bool _subscribed;

        [Import]
        internal IClassificationFormatMapService FormatMapService { get; set; } = null!;

        [Import]
        internal IClassificationTypeRegistryService Registry { get; set; } = null!;

        public void TextViewCreated(IWpfTextView textView)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_subscribed)
            {
                return;
            }

            _subscribed = true;
            VSColorTheme.ThemeChanged += _ => Apply();
        }

        private void Apply()
        {
            var map = FormatMapService.GetClassificationFormatMap("text");
            var dark = Kubuno.Core.UI.VsTheme.IsDark();
            foreach (var (name, darkColor, lightColor) in SqlClassificationTypes.Colors)
            {
                var type = Registry.GetClassificationType(name);
                if (type is null)
                {
                    continue;
                }

                var current = map.GetExplicitTextProperties(type);
                var brush = current.ForegroundBrush as SolidColorBrush;
                if (brush is null || brush.Color == (dark ? lightColor : darkColor))
                {
                    map.SetExplicitTextProperties(type, current.SetForeground(dark ? darkColor : lightColor));
                }
            }
        }
    }
}
