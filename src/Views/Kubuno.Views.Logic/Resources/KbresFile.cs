using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Kubuno.Views.Logic.Resources
{
    /// <summary>The kind of a resource entry: its element name in the <c>.kbres</c> file, and its editor category.</summary>
    public enum ResourceKind
    {
        String,
        Image,
        Icon,
        Audio,
        File,
        Color,
        Font,
    }

    /// <summary>Where an entry's value lives (docs/RESOURCES.md): text in the file, a linked project file, or embedded bytes.</summary>
    public enum ResourcePersistence
    {
        Text,
        Linked,
        Embedded,
    }

    /// <summary>
    /// One entry of a <c>.kbres</c> file. Immutable; the editor model replaces entries. Mirrors
    /// <c>kubuno_desktop_resources_model::format::Entry</c> (desktop: src/crates/kubuno-desktop-resources-model).
    /// </summary>
    public sealed class ResourceEntry
    {
        public ResourceEntry(string name, ResourceKind kind, ResourcePersistence persistence, string? text = null, string? path = null, string? format = null, byte[]? bytes = null, string? comment = null, bool textFile = false)
        {
            Name = name;
            Kind = kind;
            Persistence = persistence;
            Text = text ?? string.Empty;
            Path = path ?? string.Empty;
            Format = format ?? string.Empty;
            Bytes = bytes ?? Array.Empty<byte>();
            Comment = string.IsNullOrEmpty(comment) ? null : comment;
            TextFile = textFile;
        }

        public string Name { get; }

        public ResourceKind Kind { get; }

        public ResourcePersistence Persistence { get; }

        /// <summary>The text of a String (its content), Color or Font (its <c>Value</c>).</summary>
        public string Text { get; }

        /// <summary>The linked file, relative to the <c>.kbres</c> file, forward slashes.</summary>
        public string Path { get; }

        /// <summary>The format of an embedded entry (file extension, lower case).</summary>
        public string Format { get; }

        /// <summary>The embedded bytes.</summary>
        public byte[] Bytes { get; }

        public string? Comment { get; }

        /// <summary>A File entry whose bytes are UTF-8 text (<c>Text="true"</c>).</summary>
        public bool TextFile { get; }

        /// <summary>1-based line of the entry in the file it was read from (0 for an entry built in memory).</summary>
        public int Line { get; internal set; }

        /// <summary>The format of a binary entry: the embedded one, else the linked file's extension.</summary>
        public string EffectiveFormat => Persistence == ResourcePersistence.Embedded ? Format : System.IO.Path.GetExtension(Path).TrimStart('.').ToLowerInvariant();

        public static ResourceEntry String(string name, string value, string? comment = null) => new ResourceEntry(name, ResourceKind.String, ResourcePersistence.Text, text: value, comment: comment);

        public static ResourceEntry Linked(ResourceKind kind, string name, string path, string? comment = null) => new ResourceEntry(name, kind, ResourcePersistence.Linked, path: path.Replace('\\', '/'), comment: comment);

        public static ResourceEntry Embedded(ResourceKind kind, string name, string format, byte[] bytes, string? comment = null) => new ResourceEntry(name, kind, ResourcePersistence.Embedded, format: format.TrimStart('.').ToLowerInvariant(), bytes: bytes, comment: comment);

        public ResourceEntry WithName(string name) => new ResourceEntry(name, Kind, Persistence, Text, Path, Format, Bytes, Comment, TextFile) { Line = Line };

        public ResourceEntry WithText(string text) => new ResourceEntry(Name, Kind, Persistence, text, Path, Format, Bytes, Comment, TextFile) { Line = Line };

        public ResourceEntry WithComment(string? comment) => new ResourceEntry(Name, Kind, Persistence, Text, Path, Format, Bytes, comment, TextFile) { Line = Line };

        public ResourceEntry WithKind(ResourceKind kind) => new ResourceEntry(Name, kind, Persistence, Text, Path, Format, Bytes, Comment, TextFile) { Line = Line };
    }

    /// <summary>A problem found while reading a <c>.kbres</c> file.</summary>
    public sealed class ResourceDiagnostic
    {
        public ResourceDiagnostic(bool isError, string message, int line)
        {
            IsError = isError;
            Message = message;
            Line = line;
        }

        public bool IsError { get; }

        public string Message { get; }

        public int Line { get; }

        public override string ToString() => (IsError ? "error" : "warning") + " (line " + Line.ToString(CultureInfo.InvariantCulture) + "): " + Message;
    }

    /// <summary>
    /// A <c>.kbres</c> resource file (docs/RESOURCES.md): reading (lenient, with diagnostics) and the canonical writing,
    /// byte-for-byte the same as the Rust writer (<c>kubuno_desktop_resources_model::ResourceFile::to_text</c>) so that a file
    /// saved by Visual Studio and one written by the conversion tool never differ in form.
    /// </summary>
    public sealed class KbresFile
    {
        public const string Version = "1";

        /// <summary>Image formats offered for Image/Icon entries.</summary>
        public static readonly IReadOnlyList<string> ImageFormats = new[] { "svg", "png", "jpg", "jpeg", "bmp", "gif", "ico", "tif", "tiff", "webp" };

        /// <summary>Audio formats offered for Audio entries.</summary>
        public static readonly IReadOnlyList<string> AudioFormats = new[] { "wav", "mp3", "wma", "ogg", "flac", "m4a" };

        public List<ResourceEntry> Entries { get; } = new List<ResourceEntry>();

        /// <summary>The root's <c>Culture</c> attribute, when written (informative: the file name decides).</summary>
        public string? Culture { get; set; }

        public ResourceEntry? Get(string name) => Entries.FirstOrDefault(e => e.Name == name);

        public int IndexOf(string name) => Entries.FindIndex(e => e.Name == name);

        /// <summary>The kind an imported file gets from its extension.</summary>
        public static ResourceKind KindForExtension(string extension)
        {
            var ext = extension.TrimStart('.').ToLowerInvariant();
            if (ext == "ico")
            {
                return ResourceKind.Icon;
            }

            if (ImageFormats.Contains(ext))
            {
                return ResourceKind.Image;
            }

            return AudioFormats.Contains(ext) ? ResourceKind.Audio : ResourceKind.File;
        }

        /// <summary>Letters, digits, <c>_ . - +</c>, starting with a letter or <c>_</c>, no empty dotted part (same rule as the Rust model).</summary>
        public static bool IsValidName(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var first = name![0];
            if (!(IsAsciiLetter(first) || first == '_') || name.EndsWith(".", StringComparison.Ordinal) || name.Contains(".."))
            {
                return false;
            }

            return name.Skip(1).All(c => IsAsciiLetter(c) || (c >= '0' && c <= '9') || c == '_' || c == '.' || c == '-' || c == '+');
        }

        private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        /// <summary>Reads <paramref name="text"/>; <paramref name="diagnostics"/> receives every problem (the entries that could be read are kept).</summary>
        public static KbresFile Read(string text, out List<ResourceDiagnostic> diagnostics)
        {
            diagnostics = new List<ResourceDiagnostic>();
            var file = new KbresFile();
            XDocument doc;
            try
            {
                doc = XDocument.Parse(text, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            }
            catch (XmlException ex)
            {
                diagnostics.Add(new ResourceDiagnostic(true, ex.Message, ex.LineNumber));
                return file;
            }

            var root = doc.Root;
            if (root is null || root.Name.LocalName != "Resources")
            {
                diagnostics.Add(new ResourceDiagnostic(true, "the root element must be <Resources>", 1));
                return file;
            }

            file.Culture = (string?)root.Attribute("Culture") is { Length: > 0 } c ? c : null;
            foreach (var element in root.Elements())
            {
                var line = ((IXmlLineInfo)element).LineNumber;
                if (!Enum.TryParse<ResourceKind>(element.Name.LocalName, out var kind) || element.Name.LocalName != kind.ToString())
                {
                    diagnostics.Add(new ResourceDiagnostic(true, "unknown entry kind <" + element.Name.LocalName + ">", line));
                    continue;
                }

                var name = (string?)element.Attribute("Name");
                if (!IsValidName(name))
                {
                    diagnostics.Add(new ResourceDiagnostic(true, "`" + (name ?? string.Empty) + "` is not a valid resource name", line));
                    continue;
                }

                if (file.Get(name!) is not null)
                {
                    diagnostics.Add(new ResourceDiagnostic(true, "duplicate resource `" + name + "`", line));
                    continue;
                }

                var comment = (string?)element.Attribute("Comment");
                var textFile = (string?)element.Attribute("Text") == "true";
                ResourceEntry? entry = null;
                switch (kind)
                {
                    case ResourceKind.String:
                        entry = new ResourceEntry(name!, kind, ResourcePersistence.Text, text: string.Concat(element.Nodes().OfType<XText>().Select(t => t.Value)), comment: comment);
                        break;
                    case ResourceKind.Color:
                    case ResourceKind.Font:
                        if ((string?)element.Attribute("Value") is { } value)
                        {
                            entry = new ResourceEntry(name!, kind, ResourcePersistence.Text, text: value, comment: comment);
                        }
                        else
                        {
                            diagnostics.Add(new ResourceDiagnostic(true, "<" + kind + "> needs a Value", line));
                        }

                        break;
                    default:
                        if ((string?)element.Attribute("File") is { Length: > 0 } path)
                        {
                            entry = new ResourceEntry(name!, kind, ResourcePersistence.Linked, path: path.Replace('\\', '/'), comment: comment, textFile: textFile);
                        }
                        else if ((string?)element.Attribute("Format") is { Length: > 0 } format)
                        {
                            try
                            {
                                var compact = new string(element.Value.Where(ch => !char.IsWhiteSpace(ch)).ToArray());
                                entry = new ResourceEntry(name!, kind, ResourcePersistence.Embedded, format: format.TrimStart('.').ToLowerInvariant(), bytes: Convert.FromBase64String(compact), comment: comment, textFile: textFile);
                            }
                            catch (FormatException)
                            {
                                diagnostics.Add(new ResourceDiagnostic(true, "the embedded content is not valid base64", line));
                            }
                        }
                        else
                        {
                            diagnostics.Add(new ResourceDiagnostic(true, "<" + kind + "> needs a File (linked) or a Format and base64 content (embedded)", line));
                        }

                        break;
                }

                if (entry is not null)
                {
                    entry.Line = line;
                    file.Entries.Add(entry);
                }
            }

            return file;
        }

        /// <summary>Reads <paramref name="text"/>, ignoring diagnostics.</summary>
        public static KbresFile Parse(string text) => Read(text, out _);

        /// <summary>The canonical text (see the class remarks).</summary>
        public string ToText()
        {
            var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<Resources Version=\"1\"");
            if (!string.IsNullOrEmpty(Culture))
            {
                sb.Append(" Culture=\"").Append(EscapeAttr(Culture!)).Append('"');
            }

            if (Entries.Count == 0)
            {
                return sb.Append("/>\n").ToString();
            }

            sb.Append(">\n");
            foreach (var e in Entries)
            {
                WriteEntry(sb, e);
            }

            return sb.Append("</Resources>\n").ToString();
        }

        private static void WriteEntry(StringBuilder sb, ResourceEntry e)
        {
            var tag = e.Kind.ToString();
            sb.Append("  <").Append(tag).Append(" Name=\"").Append(EscapeAttr(e.Name)).Append('"');
            void Comment()
            {
                if (e.Comment is { } c)
                {
                    sb.Append(" Comment=\"").Append(EscapeAttr(c)).Append('"');
                }
            }

            switch (e.Persistence)
            {
                case ResourcePersistence.Text when e.Kind == ResourceKind.String:
                    Comment();
                    if (e.Text.Length == 0)
                    {
                        sb.Append("/>\n");
                    }
                    else
                    {
                        sb.Append('>').Append(EscapeText(e.Text)).Append("</").Append(tag).Append(">\n");
                    }

                    break;
                case ResourcePersistence.Text:
                    sb.Append(" Value=\"").Append(EscapeAttr(e.Text)).Append('"');
                    Comment();
                    sb.Append("/>\n");
                    break;
                case ResourcePersistence.Linked:
                    sb.Append(" File=\"").Append(EscapeAttr(e.Path)).Append('"');
                    if (e.TextFile)
                    {
                        sb.Append(" Text=\"true\"");
                    }

                    Comment();
                    sb.Append("/>\n");
                    break;
                default:
                    sb.Append(" Format=\"").Append(EscapeAttr(e.Format)).Append('"');
                    if (e.TextFile)
                    {
                        sb.Append(" Text=\"true\"");
                    }

                    Comment();
                    var b64 = Convert.ToBase64String(e.Bytes);
                    if (b64.Length <= 76)
                    {
                        sb.Append('>').Append(b64).Append("</").Append(tag).Append(">\n");
                    }
                    else
                    {
                        sb.Append(">\n");
                        for (var i = 0; i < b64.Length; i += 76)
                        {
                            sb.Append("    ").Append(b64, i, Math.Min(76, b64.Length - i)).Append('\n');
                        }

                        sb.Append("  </").Append(tag).Append(">\n");
                    }

                    break;
            }
        }

        public static string EscapeText(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '\r': sb.Append("&#13;"); break;
                    default: sb.Append(c); break;
                }
            }

            return sb.ToString();
        }

        public static string EscapeAttr(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\n': sb.Append("&#10;"); break;
                    case '\r': sb.Append("&#13;"); break;
                    case '\t': sb.Append("&#9;"); break;
                    default: sb.Append(c); break;
                }
            }

            return sb.ToString();
        }

        /// <summary>Whether <paramref name="value"/> is a colour value the runtime accepts (<c>#RGB</c>, <c>#RRGGBB</c>, <c>#AARRGGBB</c>, <c>r, g, b[, a]</c>).</summary>
        public static bool IsColor(string value)
        {
            var v = value.Trim();
            if (v.StartsWith("#", StringComparison.Ordinal))
            {
                var hex = v.Substring(1);
                return (hex.Length == 3 || hex.Length == 6 || hex.Length == 8) && hex.All(Uri.IsHexDigit);
            }

            var parts = v.Split(',');
            return (parts.Length == 3 || parts.Length == 4) && parts.All(p => byte.TryParse(p.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out _));
        }
    }
}
