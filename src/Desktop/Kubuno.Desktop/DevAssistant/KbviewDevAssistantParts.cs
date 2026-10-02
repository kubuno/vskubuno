using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Shared.DevAssistant.Extensibility;
using Kubuno.Shared.DevAssistant.Logic.Changes;
using Kubuno.Shared.DevAssistant.Logic.Prompts;
using Kubuno.Shared.DevAssistant.Logic.Protocol;
using Kubuno.Shared.DevAssistant.Logic.Secrets;
using Kubuno.Views.Designer.PropertyBrowser;
using Kubuno.Views.Designer.Registry;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Newtonsoft.Json.Linq;

namespace Kubuno.Desktop.DevAssistant
{
    /// <summary>The element last selected in a <c>.kbview</c> designer (kept when the assistant window takes the focus).</summary>
    internal sealed class SelectedElement
    {
        public SelectedElement(string viewPath, string elementId, string tag, string? name)
        {
            ViewPath = viewPath;
            ElementId = elementId;
            Tag = tag;
            Name = name;
        }

        public string ViewPath { get; }

        public string ElementId { get; }

        public string Tag { get; }

        public string? Name { get; }

        public string Label => (Name is { Length: > 0 } ? $"{Tag} \"{Name}\"" : Tag) + $" ({Path.GetFileName(ViewPath)})";
    }

    /// <summary>
    /// Follows Visual Studio's selection (docs/AI-ASSISTANT.md section 5.2, <c>#élément</c>): the designer publishes its
    /// selected elements to the Properties window as <see cref="KbviewElementObject"/>s; the last one seen is kept, so the
    /// element stays known after the developer clicks into the assistant's window. Started at idle by the assistant layer.
    /// </summary>
    [Export(typeof(IDevAssistantStartup))]
    internal sealed class KbviewSelectionTracker : IDevAssistantStartup, IVsSelectionEvents
    {
        private uint _cookie;

        public static SelectedElement? Last { get; private set; }

        public void Start()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_cookie != 0 || Package.GetGlobalService(typeof(SVsShellMonitorSelection)) is not IVsMonitorSelection monitor)
            {
                return;
            }

            monitor.AdviseSelectionEvents(this, out _cookie);
            if (ErrorHandler.Succeeded(monitor.GetCurrentSelection(out var hierarchy, out _, out _, out var container)))
            {
                Capture(container);
                ReleaseIfSet(hierarchy);
                ReleaseIfSet(container);
            }
        }

        private static void ReleaseIfSet(IntPtr pointer)
        {
            if (pointer != IntPtr.Zero)
            {
                System.Runtime.InteropServices.Marshal.Release(pointer);
            }
        }

        private static void Capture(IntPtr containerPointer)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (containerPointer == IntPtr.Zero)
            {
                return;
            }

            Capture(System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(containerPointer) as ISelectionContainer);
        }

        private static void Capture(ISelectionContainer? container)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (container is null || ErrorHandler.Failed(container.CountObjects((uint)Microsoft.VisualStudio.Shell.Interop.Constants.GETOBJS_SELECTED, out var count)) || count == 0)
            {
                return;
            }

            var objects = new object[count];
            if (ErrorHandler.Failed(container.GetObjects((uint)Microsoft.VisualStudio.Shell.Interop.Constants.GETOBJS_SELECTED, count, objects)))
            {
                return;
            }

            if (objects.FirstOrDefault() is KbviewElementObject element && element.Host is IKbviewDesignServices services && services.ViewFilePath is { } path)
            {
                Last = new SelectedElement(path, element.ElementId, element.Component.Name, element.XName);
            }
        }

        public int OnSelectionChanged(IVsHierarchy pHierOld, uint itemidOld, IVsMultiItemSelect pMISOld, ISelectionContainer pSCOld, IVsHierarchy pHierNew, uint itemidNew, IVsMultiItemSelect pMISNew, ISelectionContainer pSCNew)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                Capture(pSCNew);
            }
            catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or InvalidCastException)
            {
            }

            return VSConstants.S_OK;
        }

        public int OnElementValueChanged(uint elementid, object varValueOld, object varValueNew) => VSConstants.S_OK;

        public int OnCmdUIContextChanged(uint dwCmdUICookie, int fActive) => VSConstants.S_OK;
    }

    /// <summary>
    /// <c>#élément</c> (section 5.2): the element selected in the designer - view path, stable element id, its XML (range
    /// resolved by <c>kubuno-views-ls</c>), the ancestor chain, its handlers, and its registry entry.
    /// </summary>
    [Export(typeof(IDevAssistantReferenceProvider))]
    internal sealed class KbviewReferenceProvider : IDevAssistantReferenceProvider
    {
        public IEnumerable<string> Kinds => new[] { ReferenceKinds.Element };

        public async Task<ResolvedReference?> ResolveAsync(PromptReference reference, DevAssistantToolContext context, CancellationToken cancellationToken)
        {
            if (KbviewSelectionTracker.Last is not { } selected)
            {
                return null;
            }

            var description = await KbviewElementDescriber.DescribeAsync(selected.ViewPath, selected.ElementId, context, cancellationToken).ConfigureAwait(false);
            return new ResolvedReference(reference.Kind, selected.Label, description);
        }
    }

    /// <summary>Builds the text describing one element of a view (shared by <c>#élément</c> and <c>kbview_element</c>).</summary>
    internal static class KbviewElementDescriber
    {
        private static readonly Regex StartTag = new Regex(@"^<(?<tag>[\w.:]+)", RegexOptions.CultureInvariant);
        private static readonly Regex Attribute = new Regex(@"(?<name>[\w.:]+)\s*=\s*""(?<value>[^""]*)""", RegexOptions.CultureInvariant);

        public static async Task<string> DescribeAsync(string viewPath, string elementId, DevAssistantToolContext context, CancellationToken cancellationToken)
        {
            var text = context.ReadDocument(viewPath);
            var builder = new StringBuilder();
            builder.Append("view: ").Append(viewPath).Append('\n');
            builder.Append("element id: \"").Append(elementId).Append("\" (child-ordinal path from the root; \"\" is the root)\n");
            if (text is null)
            {
                builder.Append("(the view cannot be read)\n");
                return builder.ToString();
            }

            var range = await KbviewLanguageServerBridge.RangeOfElementAsync(viewPath, text, elementId, cancellationToken).ConfigureAwait(false);
            if (range is not { } r)
            {
                builder.Append("(the language server could not resolve this element: the view may have changed)\n");
                return builder.ToString();
            }

            var xml = text.Substring(r.Start, r.End - r.Start);
            var startTag = xml.Contains('>') ? xml.Substring(0, xml.IndexOf('>') + 1) : xml;
            var tag = StartTag.Match(xml).Groups["tag"].Value;
            var attributes = Attribute.Matches(startTag).Cast<System.Text.RegularExpressions.Match>().ToDictionary(m => m.Groups["name"].Value, m => m.Groups["value"].Value);

            // Ancestors, from the parent up to the root (each resolved by the server too).
            var ancestors = new List<string>();
            var id = elementId;
            while (id.Length > 0)
            {
                id = id.Contains('.') ? id.Substring(0, id.LastIndexOf('.')) : string.Empty;
                if (await KbviewLanguageServerBridge.RangeOfElementAsync(viewPath, text, id, cancellationToken).ConfigureAwait(false) is { } parent)
                {
                    var parentXml = text.Substring(parent.Start, Math.Min(400, parent.End - parent.Start));
                    var parentTag = StartTag.Match(parentXml).Groups["tag"].Value;
                    var parentName = Attribute.Matches(parentXml.Substring(0, parentXml.Contains('>') ? parentXml.IndexOf('>') : parentXml.Length)).Cast<System.Text.RegularExpressions.Match>().FirstOrDefault(m => m.Groups["name"].Value == "x:Name")?.Groups["value"].Value;
                    ancestors.Add(parentTag + (parentName is null ? string.Empty : $" \"{parentName}\"") + $" [id \"{id}\"]");
                }
            }

            builder.Append("tag: ").Append(tag);
            if (attributes.TryGetValue("x:Name", out var name))
            {
                builder.Append("   x:Name: ").Append(name);
            }

            builder.Append('\n');
            builder.Append("ancestors: ").Append(ancestors.Count == 0 ? "(root)" : string.Join(" < ", ancestors)).Append('\n');
            var handlers = attributes.Where(a => a.Key.StartsWith("On", StringComparison.Ordinal)).Select(a => $"{a.Key}=\"{a.Value}\"").ToList();
            builder.Append("code-behind handlers: ").Append(handlers.Count == 0 ? "none" : string.Join(", ", handlers)).Append('\n');
            if (await KbviewLanguageServerBridge.GetRegistryAsync(cancellationToken).ConfigureAwait(false) is { } registry && registry.Find(tag) is { } component)
            {
                builder.Append(KbviewRegistryText.Describe(component));
            }

            builder.Append("xml:\n").Append(xml).Append('\n');
            return builder.ToString();
        }
    }

    /// <summary>Registry entries as compact text for the model.</summary>
    internal static class KbviewRegistryText
    {
        public static string Describe(ComponentMeta component)
        {
            var builder = new StringBuilder();
            builder.Append("registry: ").Append(component.Name).Append(" (family ").Append(component.Family).Append(", children ").Append(component.Children);
            if (component.AllowedChildren.Count > 0)
            {
                builder.Append(": ").Append(string.Join("/", component.AllowedChildren));
            }

            builder.Append(component.IsProject ? ", project control" : string.Empty).Append(")\n");
            if (!string.IsNullOrEmpty(component.Doc))
            {
                builder.Append("  doc: ").Append(component.Doc).Append('\n');
            }

            builder.Append("  properties: ").Append(string.Join(", ", component.Properties.Where(p => p.Browsable || p.Bindable).Select(Property))).Append('\n');
            builder.Append("  events: ").Append(string.Join(", ", component.Events.Select(e => e.Name))).Append('\n');
            return builder.ToString();
        }

        private static string Property(PropertyMeta property)
        {
            var kind = property.Kind.Tag == PropKindTag.Enum ? "{" + string.Join("|", property.Kind.EnumVariants) + "}" : property.Kind.Tag.ToString();
            return property.Name + ":" + kind + (string.IsNullOrEmpty(property.Default) ? string.Empty : "=" + property.Default);
        }
    }

    /// <summary>
    /// The Desktop layer's tools (section 6.3): the live registry, the selected element, an element by id, validation,
    /// and <c>kbview_apply_ops</c> - structured <c>kubuno/applyEdit</c> ops computed and validated by the language server on
    /// a scratch copy, then PROPOSED as a change set (nothing is written before the developer's review).
    /// </summary>
    [Export(typeof(IDevAssistantToolProvider))]
    internal sealed class KbviewToolProvider : IDevAssistantToolProvider
    {
        private const string OpsSchema =
            @"{""type"":""object"",""properties"":{" +
            @"""path"":{""type"":""string"",""description"":""The .kbview file (absolute or relative to the solution root).""}," +
            @"""ops"":{""type"":""array"",""items"":{""type"":""object"",""properties"":{" +
            @"""kind"":{""type"":""string"",""enum"":[""setAttribute"",""removeAttribute"",""insertChild"",""removeElement"",""moveElement"",""renameElement""]}," +
            @"""elementId"":{""type"":""string""},""parentId"":{""type"":""string""},""index"":{""type"":""integer""},""xml"":{""type"":""string""}," +
            @"""name"":{""type"":""string""},""value"":{""type"":""string""},""newParentId"":{""type"":""string""},""newName"":{""type"":""string""}}," +
            @"""required"":[""kind""],""additionalProperties"":false}}," +
            @"""summary"":{""type"":""string""}},""required"":[""path"",""ops""],""additionalProperties"":false}";

        public IEnumerable<IDevAssistantTool> GetTools() => new IDevAssistantTool[]
        {
            new Tool(
                "kbview_registry",
                "The live element registry of kubuno-views-ls (the truth for element, property and enum names). Without 'components': every element with its family. With 'components': their properties (kind, enum values, default), events and allowed children.",
                @"{""type"":""object"",""properties"":{""components"":{""type"":""array"",""items"":{""type"":""string""}}},""additionalProperties"":false}",
                ApprovalClass.Read,
                RegistryAsync),
            new Tool(
                "kbview_selected_element",
                "The element selected in the .kbview designer: view path, element id, XML, ancestors, handlers and registry entry.",
                @"{""type"":""object"",""properties"":{},""additionalProperties"":false}",
                ApprovalClass.Read,
                async (_, context, token) => KbviewSelectionTracker.Last is { } selected
                    ? DevAssistantToolResult.Text(await KbviewElementDescriber.DescribeAsync(selected.ViewPath, selected.ElementId, context, token).ConfigureAwait(false))
                    : DevAssistantToolResult.Error("No element is selected in a view designer.")),
            new Tool(
                "kbview_element",
                "One element of a view by its stable id (child-ordinal path, \"\" = root, \"0.2\" = third child of the first child): XML, ancestors, handlers, registry entry.",
                @"{""type"":""object"",""properties"":{""path"":{""type"":""string""},""elementId"":{""type"":""string""}},""required"":[""path"",""elementId""],""additionalProperties"":false}",
                ApprovalClass.Read,
                async (input, context, token) =>
                {
                    var path = Resolve(Str(input, "path"), context);
                    return context.WhyDenied(path) is { } why
                        ? DevAssistantToolResult.Error($"Access denied ({why}).")
                        : DevAssistantToolResult.Text(await KbviewElementDescriber.DescribeAsync(path, Str(input, "elementId") ?? string.Empty, context, token).ConfigureAwait(false));
                }),
            new Tool(
                "kbview_validate",
                "Validates a view with the language server: the file's current text, or 'text' (a new view) for 'path'. Returns the errors.",
                @"{""type"":""object"",""properties"":{""path"":{""type"":""string""},""text"":{""type"":""string""}},""required"":[""path""],""additionalProperties"":false}",
                ApprovalClass.Read,
                async (input, context, token) =>
                {
                    var path = Resolve(Str(input, "path"), context);
                    var text = Str(input, "text") is { } given ? context.Masker.Restore(given).Text : context.ReadDocument(path);
                    if (text is null)
                    {
                        return DevAssistantToolResult.Error(path + " does not exist; pass its text.");
                    }

                    var result = await KbviewLanguageServerBridge.ValidateAsync(path, text, token).ConfigureAwait(false);
                    return result.Succeeded ? DevAssistantToolResult.Text("Valid (" + result.Note + ").") : DevAssistantToolResult.Error(result.Error!);
                }),
            new Tool(
                "kbview_apply_ops",
                "Modifies a .kbview surgically with kubuno/applyEdit ops, applied IN ORDER (each op sees the result of the previous ones, so ids after an insert/remove at the same level shift): " +
                "setAttribute{elementId,name,value}, removeAttribute{elementId,name}, insertChild{parentId,index,xml} (xml = one well-formed element), removeElement{elementId}, moveElement{elementId,newParentId,index}, renameElement{elementId,newName}. " +
                "The language server computes and validates the result on a scratch copy; when valid it is PROPOSED to the developer for review (nothing is written yet). New user-facing strings belong in .kbres resources ({Res key}).",
                OpsSchema,
                ApprovalClass.Write,
                ApplyOpsAsync),
        };

        private static async Task<DevAssistantToolResult> RegistryAsync(JsonElement input, DevAssistantToolContext context, CancellationToken token)
        {
            var registry = await KbviewLanguageServerBridge.GetRegistryAsync(token).ConfigureAwait(false);
            if (registry is null || registry.Components.Count == 0)
            {
                return DevAssistantToolResult.Error("The .kbview language server is not running (open a view first) or returned an empty registry.");
            }

            var builder = new StringBuilder();
            if (input.TryGetProperty("components", out var components) && components.ValueKind == JsonValueKind.Array && components.GetArrayLength() > 0)
            {
                foreach (var name in components.EnumerateArray().Select(c => c.GetString() ?? string.Empty))
                {
                    builder.Append(registry.Find(name) is { } component ? KbviewRegistryText.Describe(component) : $"{name}: not in the registry\n");
                }
            }
            else
            {
                builder.Append("registry version ").Append(registry.Version ?? "?").Append(", ").Append(registry.Components.Count).Append(" elements:\n");
                foreach (var family in registry.FamilyNames)
                {
                    builder.Append(family).Append(": ").Append(string.Join(", ", registry.Families[family].Select(c => c.Name))).Append('\n');
                }
            }

            return DevAssistantToolResult.Text(builder.ToString());
        }

        private static async Task<DevAssistantToolResult> ApplyOpsAsync(JsonElement input, DevAssistantToolContext context, CancellationToken token)
        {
            var path = Resolve(Str(input, "path"), context);
            if (!path.EndsWith(".kbview", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".kbcontrol", StringComparison.OrdinalIgnoreCase))
            {
                return DevAssistantToolResult.Error("kbview_apply_ops edits .kbview/.kbcontrol files only.");
            }

            if (context.WhyDenied(path) is { } why)
            {
                return DevAssistantToolResult.Error($"{path}: writes are not allowed there ({why}).");
            }

            var current = context.ReadDocument(path);
            if (current is null)
            {
                return DevAssistantToolResult.Error($"{path} does not exist: create a new view with edit_propose (new_file_content).");
            }

            // The model saw masked text: restore the values of the placeholders it kept, refuse invented ones.
            var ops = new List<JObject>();
            foreach (var op in input.GetProperty("ops").EnumerateArray())
            {
                var restored = context.Masker.Restore(op.GetRawText());
                if (!restored.IsComplete)
                {
                    return DevAssistantToolResult.Error("Unknown secret placeholder(s) " + string.Join(", ", restored.UnknownPlaceholders) + ": never invent «secret:…» markers.");
                }

                ops.Add(JObject.Parse(restored.Text));
            }

            var result = await KbviewLanguageServerBridge.ApplyOpsAsync(path, current, ops, token).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                return DevAssistantToolResult.Error(result.Error!);
            }

            context.ProposeChange(path, current, result.Text!);
            var hunks = LineDiff.Compute(current, result.Text!);
            return DevAssistantToolResult.Text(
                $"OK: {ops.Count} op(s) validated by the language server ({result.Note}). Proposed to the developer: {Path.GetFileName(path)}, {hunks.Count} hunk(s), +{hunks.Sum(h => h.Added)} -{hunks.Sum(h => h.Removed)}. Nothing is written until they accept.");
        }

        private static string? Str(JsonElement input, string name) =>
            input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        private static string Resolve(string? path, DevAssistantToolContext context)
        {
            var trimmed = (path ?? string.Empty).Trim().Trim('"');
            if (!Path.IsPathRooted(trimmed) && context.SolutionRoots.Count > 0)
            {
                trimmed = Path.Combine(context.SolutionRoots[0], trimmed);
            }

            return trimmed.Length == 0 ? trimmed : Path.GetFullPath(trimmed);
        }

        private sealed class Tool : IDevAssistantTool
        {
            private readonly Func<JsonElement, DevAssistantToolContext, CancellationToken, Task<DevAssistantToolResult>> _invoke;

            public Tool(string name, string description, string schema, ApprovalClass approvalClass, Func<JsonElement, DevAssistantToolContext, CancellationToken, Task<DevAssistantToolResult>> invoke)
            {
                Descriptor = new ToolDescriptor { Name = name, Description = description, InputSchema = RpcCodec.ParseElement(schema), ApprovalClass = approvalClass };
                _invoke = invoke;
            }

            public ToolDescriptor Descriptor { get; }

            public Task<DevAssistantToolResult> InvokeAsync(JsonElement input, DevAssistantToolContext context, CancellationToken cancellationToken) => _invoke(input, context, cancellationToken);
        }
    }

    /// <summary><c>/vue</c> (section 6.2): create or modify a view from a description, through LS-validated structured edits.</summary>
    [Export(typeof(IDevAssistantCommandProvider))]
    internal sealed class KbviewCommandProvider : IDevAssistantCommandProvider
    {
        public IEnumerable<DevAssistantCommand> GetCommands() => new[]
        {
            new DevAssistantCommand
            {
                Name = "vue",
                Aliases = new[] { "view" },
                DescriptionFr = "Créer ou modifier une vue .kbview à partir d'une description",
                DescriptionEn = "Create or change a .kbview view from a description",
                Effort = "high",
                DefaultReferences = new[] { ReferenceKinds.File, ReferenceKinds.Element },
                Digest = VueDigest,
            },
        };

        private const string VueDigest =
            "Command /vue: create or modify a Kubuno .kbview view (desktop) from the developer's description.\n" +
            "Grammar (VIEWS-SPEC): elements are components and attributes are properties or events, PascalCase and case-sensitive. " +
            "x:Name (unique identifier, becomes a code-behind field), d:Property (design-time only), DesignWidth/DesignHeight on the root. " +
            "Attached properties Owner.Property (Stack.Fill, TableLayoutPanel.Row/Column). Children follow the registry's children model " +
            "(None, SingleWidget, List with allowed children). Values: Bool true/false, F32 invariant numbers, enums as listed by the registry; " +
            "Padding/Margin \"l, t, r, b\", Anchor \"Top, Left\"; colours are theme tokens (TextSecondary...), never literals. " +
            "{Binding Path[, Mode=TwoWay]} binds to the code-behind; {Res key} reads a .kbres resource. Events are On* attributes naming code-behind handlers.\n" +
            "Layout: Panel/UserControl roots dock children (Dock) or place them absolutely (X, Y, Width, Height, Anchor) like WinForms; " +
            "Stack flows children (Direction, Gap, Padding); TableLayoutPanel is a grid.\n" +
            "Procedure: 1) read the target view (attached, or fs_read/vs_active_document) and the selected element if any; " +
            "2) check every element and property you use with kbview_registry (the registry wins over any document); " +
            "3) modify an EXISTING view only with kbview_apply_ops (surgical ops; never rewrite the file; keep comments, order and formatting; " +
            "match the style of the neighbouring elements, e.g. their X/Y/Width/Height grid); a NEW view goes through edit_propose with new_file_content, then kbview_validate; " +
            "4) if the language server reports errors, fix the ops and try again (at most 3 attempts); " +
            "5) end with a short summary of what you proposed. User-facing text: French, preferably as {Res key} resources.";
    }
}
