using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Kubuno.Views.Designer.DesignSurface
{
    /// <summary>
    /// What the design surface shows of the current text (docs/DESIGNER.md section 17) - the
    /// <c>state</c> of a <c>renderStatus</c> line, mirroring <c>kubuno_desktop_views::protocol::RenderState</c>.
    /// </summary>
    public enum DesignSurfaceRenderState
    {
        /// <summary>The view as written: no error.</summary>
        Clean,

        /// <summary>The view with errors: its valid part, placeholders for the elements the preview cannot show.</summary>
        Tolerant,

        /// <summary>A malformed file opened: the preview was rebuilt from what could be read.</summary>
        Recovered,

        /// <summary>The text does not parse: the last good preview stays on screen, dimmed.</summary>
        Stale,

        /// <summary>Nothing could be shown (no element at all).</summary>
        Empty,
    }

    /// <summary>
    /// One diagnostic of a <c>renderStatus</c> line (<c>kubuno_desktop_views::protocol::WireDiagnostic</c>): 1-based
    /// lines and 1-based columns counted in UTF-16 code units, as Visual Studio counts them.
    /// </summary>
    public sealed class DesignSurfaceDiagnostic
    {
        public DesignSurfaceDiagnostic(int line, int column, int endLine, int endColumn, string message, string? code = null, string? element = null, bool isSyntax = false)
        {
            Line = Math.Max(1, line);
            Column = Math.Max(1, column);
            EndLine = Math.Max(Line, endLine);
            EndColumn = EndLine == Line ? Math.Max(Column, endColumn) : Math.Max(1, endColumn);
            Message = message ?? string.Empty;
            Code = code;
            Element = element;
            IsSyntax = isSyntax;
        }

        public int Line { get; }

        public int Column { get; }

        public int EndLine { get; }

        public int EndColumn { get; }

        public string Message { get; }

        /// <summary><c>"unknownElement"</c> for an element the preview does not know (see <see cref="Element"/>), else <see langword="null"/>.</summary>
        public string? Code { get; }

        /// <summary>The element a <c>"unknownElement"</c> diagnostic is about.</summary>
        public string? Element { get; }

        /// <summary>A syntax error: the text is not well-formed XML (what keeps the last valid preview on screen).</summary>
        public bool IsSyntax { get; }

        /// <summary>Whether the preview does not know the element.</summary>
        public bool IsUnknownElement => string.Equals(Code, "unknownElement", StringComparison.Ordinal) && !string.IsNullOrEmpty(Element);
    }

    /// <summary>A whole <c>renderStatus</c> line.</summary>
    public sealed class DesignSurfaceRenderStatus
    {
        public DesignSurfaceRenderStatus(DesignSurfaceRenderState state, IReadOnlyList<DesignSurfaceDiagnostic> diagnostics)
        {
            State = state;
            Diagnostics = diagnostics ?? Array.Empty<DesignSurfaceDiagnostic>();
        }

        public DesignSurfaceRenderState State { get; }

        public IReadOnlyList<DesignSurfaceDiagnostic> Diagnostics { get; }
    }

    /// <summary>One line of the designer's error banner: where, and what.</summary>
    public sealed class DesignErrorBannerEntry
    {
        internal DesignErrorBannerEntry(DesignSurfaceDiagnostic diagnostic, string text, bool isRuntimeGap)
        {
            Diagnostic = diagnostic;
            Text = text;
            IsRuntimeGap = isRuntimeGap;
        }

        public DesignSurfaceDiagnostic Diagnostic { get; }

        /// <summary><c>line:column</c>, as the entry shows it before its message.</summary>
        public string Location => Diagnostic.Line.ToString(CultureInfo.InvariantCulture) + ":" + Diagnostic.Column.ToString(CultureInfo.InvariantCulture);

        public string Text { get; }

        /// <summary>An element Visual Studio's registry knows but the preview's runtime does not (a newer control, a project control not built yet): a warning, not an error of the view.</summary>
        public bool IsRuntimeGap { get; }

        /// <summary>A warning (else an error).</summary>
        public bool IsWarning => IsRuntimeGap;
    }

    /// <summary>
    /// What the designer's error banner shows for a <see cref="DesignSurfaceRenderStatus"/> (docs/DESIGNER.md
    /// section 17): pure, so it is unit-tested without WPF. The banner itself is <see cref="UI.DesignErrorBanner"/>.
    /// </summary>
    public sealed class DesignErrorBannerModel
    {
        private DesignErrorBannerModel(bool isVisible, bool isError, string headline, IReadOnlyList<DesignErrorBannerEntry> entries)
        {
            IsVisible = isVisible;
            IsError = isError;
            Headline = headline;
            Entries = entries;
        }

        /// <summary>The model of a hidden banner.</summary>
        public static DesignErrorBannerModel Hidden { get; } = new DesignErrorBannerModel(false, false, string.Empty, Array.Empty<DesignErrorBannerEntry>());

        public bool IsVisible { get; }

        /// <summary>The view has real errors (else only elements the preview's runtime does not know yet).</summary>
        public bool IsError { get; }

        public string Headline { get; }

        public IReadOnlyList<DesignErrorBannerEntry> Entries { get; }

        /// <summary>
        /// The banner of <paramref name="status"/>. <paramref name="knownElement"/> tells whether Visual
        /// Studio's own registry (the language server's) knows an element name: an unknown element it
        /// knows is a gap of the preview's runtime, not an error of the view.
        /// </summary>
        public static DesignErrorBannerModel Build(DesignSurfaceRenderStatus? status, Func<string, bool>? knownElement)
        {
            if (status is null || (status.Diagnostics.Count == 0 && status.State == DesignSurfaceRenderState.Clean))
            {
                return Hidden;
            }

            // The syntax error first (what keeps the last valid preview on screen), then in text order. Only the
            // first syntax error is listed: the parser's next ones are its consequences (an unterminated string
            // swallows what follows); the Error List keeps them all.
            var firstSyntax = status.Diagnostics.Where(d => d.IsSyntax).OrderBy(d => d.Line).ThenBy(d => d.Column).FirstOrDefault();
            var entries = status.Diagnostics
                .Where(d => !d.IsSyntax || ReferenceEquals(d, firstSyntax))
                .OrderBy(d => d.IsSyntax ? 0 : 1)
                .ThenBy(d => d.Line)
                .ThenBy(d => d.Column)
                .Select(d =>
                {
                    var gap = d.IsUnknownElement && knownElement != null && knownElement(d.Element!);
                    return new DesignErrorBannerEntry(d, gap ? DesignerText.ErrorBannerRuntimeGap(d.Element!) : d.Message, gap);
                })
                .ToList();
            // A control only the preview's runtime lacks is a warning; everything else an error.
            var errors = entries.Count(e => !e.IsRuntimeGap);
            var gaps = entries.Count - errors;
            string headline = status.State switch
            {
                DesignSurfaceRenderState.Stale => DesignerText.ErrorBannerStale,
                DesignSurfaceRenderState.Empty => DesignerText.ErrorBannerEmpty,
                DesignSurfaceRenderState.Recovered => DesignerText.ErrorBannerRecovered,
                _ when errors == 0 && gaps > 0 => DesignerText.ErrorBannerRuntimeGaps(gaps),
                _ => DesignerText.ErrorBannerTolerant(Math.Max(errors, 1), gaps),
            };
            var isError = errors > 0 || status.State is DesignSurfaceRenderState.Stale or DesignSurfaceRenderState.Empty or DesignSurfaceRenderState.Recovered;
            return new DesignErrorBannerModel(true, isError, headline, entries);
        }
    }
}
