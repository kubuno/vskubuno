using System;
using System.Collections.Generic;
using System.Linq;
using Kubuno.Views.Designer.PropertyBrowser;
using Kubuno.Views.Designer.Registry;

namespace Kubuno.Views.Tests.Designer.PropertyBrowser
{
    /// <summary>A designer pane fake for the rich editors: records the attribute edits (optionally batched like the designer) and the text edits.</summary>
    internal sealed class RichHost : IKbviewElementHost, IKbviewDesignServices
    {
        private readonly List<Action> _scheduled = new List<Action>();
        private readonly PropertyEditBatcher _batcher;
        private string _text;

        public RichHost(string text, ComponentRegistry? registry = null)
        {
            _text = text;
            Registry = registry ?? RichRegistry.Registry;
            _batcher = new PropertyEditBatcher(_scheduled.Add, Batches.Add);
        }

        public List<string> Calls { get; } = new List<string>();

        public List<IReadOnlyList<PropertyEdit>> Batches { get; } = new List<IReadOnlyList<PropertyEdit>>();

        public List<(int Version, IReadOnlyList<TextReplacement> Edits, string Description)> TextEdits { get; } = new List<(int, IReadOnlyList<TextReplacement>, string)>();

        public List<string> Paths { get; } = new List<string>();

        public string Text
        {
            get => _text;
            set
            {
                _text = value;
                CurrentVersion++;
            }
        }

        public ComponentRegistry Registry { get; }

        public int CurrentVersion { get; private set; }

        public string? ViewFilePath { get; set; } = @"C:\app\src\main_view.kbview";

        public string GetCurrentText() => _text;

        public void SetAttribute(string elementId, string name, string value)
        {
            Calls.Add($"set {elementId} {name}={value}");
            _batcher.Enqueue(new PropertyEdit(elementId, name, value));
        }

        public void RemoveAttribute(string elementId, string name)
        {
            Calls.Add($"remove {elementId} {name}");
            _batcher.Enqueue(new PropertyEdit(elementId, name, null));
        }

        public void CreateOrShowHandler(string elementId, string eventName, string? suggestedName)
        {
        }

        public bool IsHandlerRequestRecent(string elementId, string eventName) => false;

        public IReadOnlyList<string> GetCompatibleHandlers(string elementId, string eventName) => Array.Empty<string>();

        public void RenameHandler(string elementId, string eventName, string oldName, string newName)
        {
        }

        public void RemoveHandler(string elementId, string eventName)
        {
        }

        public IReadOnlyList<string> GetBindingPaths() => Paths;

        /// <summary>The schema <see cref="GetBindingSources"/> answers.</summary>
        public Kubuno.Views.Designer.Bindings.BindingSourceSchema Schema { get; set; } = Kubuno.Views.Designer.Bindings.BindingSourceSchema.Empty;

        public List<string> Definitions { get; } = new List<string>();

        public Kubuno.Views.Designer.Bindings.BindingSourceSchema GetBindingSources(string elementId) => Schema;

        public Kubuno.Views.Designer.Bindings.BindingPreview PreviewBinding(string expression, string? sample, Kubuno.Views.Designer.Bindings.BindingShape sampleShape, Kubuno.Views.Designer.Bindings.BindingShape? want) =>
            new Kubuno.Views.Designer.Bindings.BindingPreview(sample, null);

        public bool GoToBindingDefinition(string elementId, string attribute)
        {
            Definitions.Add(elementId + "." + attribute);
            return true;
        }

        public void ApplyTextEdits(int version, IReadOnlyList<TextReplacement> edits, string description) => TextEdits.Add((version, edits, description));

        public void RunScheduled()
        {
            var pending = _scheduled.ToList();
            _scheduled.Clear();
            pending.ForEach(a => a());
        }
    }
}
