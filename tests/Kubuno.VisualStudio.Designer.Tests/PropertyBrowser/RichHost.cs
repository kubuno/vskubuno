using System;
using System.Collections.Generic;
using System.Linq;
using Kubuno.VisualStudio.Designer.PropertyBrowser;
using Kubuno.VisualStudio.Designer.Registry;

namespace Kubuno.VisualStudio.Designer.Tests.PropertyBrowser
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

        public void ApplyTextEdits(int version, IReadOnlyList<TextReplacement> edits, string description) => TextEdits.Add((version, edits, description));

        public void RunScheduled()
        {
            var pending = _scheduled.ToList();
            _scheduled.Clear();
            pending.ForEach(a => a());
        }
    }
}
