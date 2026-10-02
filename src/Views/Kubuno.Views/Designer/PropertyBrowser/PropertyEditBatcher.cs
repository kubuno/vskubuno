using System;
using System.Collections.Generic;

namespace Kubuno.Views.Designer.PropertyBrowser
{
    /// <summary>One attribute edit made in the Properties window: set <see cref="Name"/> to <see cref="Value"/>, or remove it when <see cref="Value"/> is null.</summary>
    public sealed class PropertyEdit
    {
        public PropertyEdit(string elementId, string name, string? value)
        {
            ElementId = elementId ?? throw new ArgumentNullException(nameof(elementId));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Value = value;
        }

        public string ElementId { get; }

        public string Name { get; }

        /// <summary>The new value, or null to remove the attribute (the grid's "Reset", or a cleared cell).</summary>
        public string? Value { get; }

        public override string ToString() => Value is null ? $"remove {ElementId} {Name}" : $"set {ElementId} {Name}={Value}";
    }

    /// <summary>
    /// Gathers the attribute edits the Properties window makes in one go into ONE batch - one
    /// <c>kubuno/applyEdit</c> round trip per edit against the same buffer version, applied as one undo unit
    /// (docs/DESIGNER.md §13). With several elements selected, the grid sets the value on each object in turn,
    /// synchronously; without batching each would be its own undo unit, and all but the first would be computed
    /// against a buffer version the first one has already changed. The first edit schedules a flush through
    /// <c>schedule</c> (the UI dispatcher in Visual Studio, a manual queue in the tests); every edit made before
    /// it runs joins the batch.
    /// </summary>
    public sealed class PropertyEditBatcher
    {
        private readonly Action<Action> _schedule;
        private readonly Action<IReadOnlyList<PropertyEdit>> _apply;
        private List<PropertyEdit> _pending = new List<PropertyEdit>();
        private bool _scheduled;

        public PropertyEditBatcher(Action<Action> schedule, Action<IReadOnlyList<PropertyEdit>> apply)
        {
            _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
            _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        }

        /// <summary>Queues <paramref name="edit"/> for the next flush.</summary>
        public void Enqueue(PropertyEdit edit)
        {
            _pending.Add(edit ?? throw new ArgumentNullException(nameof(edit)));
            if (!_scheduled)
            {
                _scheduled = true;
                _schedule(Flush);
            }
        }

        /// <summary>Applies every queued edit as one batch (a no-op when nothing is queued).</summary>
        public void Flush()
        {
            _scheduled = false;
            if (_pending.Count == 0)
            {
                return;
            }

            var batch = _pending;
            _pending = new List<PropertyEdit>();
            _apply(batch);
        }
    }
}
