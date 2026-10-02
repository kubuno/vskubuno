using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Kubuno.Views.Logic.Resources;

namespace Kubuno.Views.Resources.Editor
{
    /// <summary>
    /// One row of the editor's grid (Strings and Other categories): the neutral entry plus one cell per culture. Reads
    /// and writes go straight to the <see cref="ResourceSetModel"/> (no cached values), so the row is always the model.
    /// </summary>
    public sealed class ResourceRow : INotifyPropertyChanged
    {
        private readonly ResourceGridModel _owner;
        private string _name;

        internal ResourceRow(ResourceGridModel owner, string name)
        {
            _owner = owner;
            _name = name;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Name
        {
            get => _name;
            set
            {
                if (value == _name)
                {
                    return;
                }

                try
                {
                    _owner.Model.Rename(_name, value);
                    _name = value;
                }
                catch (ArgumentException ex)
                {
                    _owner.RaiseError(ex.Message);
                }

                Raise();
            }
        }

        /// <summary>The neutral value (a string's text, a colour or font value).</summary>
        public string Value
        {
            get => _owner.Model.Get(_name)?.Text ?? string.Empty;
            set => Write(null, value);
        }

        public string Comment
        {
            get => _owner.Model.Get(_name)?.Comment ?? string.Empty;
            set
            {
                if (value != Comment)
                {
                    _owner.Model.SetComment(_name, value.Length == 0 ? null : value);
                }
            }
        }

        public string Kind => _owner.Model.Get(_name) is { } e ? ResourceText.KindName(e.Kind) : string.Empty;

        /// <summary>The translation in <paramref name="culture"/>; empty = not translated (the neutral value applies).</summary>
        public string this[string culture]
        {
            get => _owner.Model.Translation(_name, culture)?.Text ?? string.Empty;
            set => Write(culture, value);
        }

        /// <summary>True when <paramref name="culture"/> has no value for this row.</summary>
        public bool IsMissing(string culture) => _owner.Model.Translation(_name, culture) is null;

        internal void Raise() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

        private void Write(string? culture, string value)
        {
            var current = culture is null ? Value : this[culture];
            if (value == current)
            {
                return;
            }

            try
            {
                _owner.Model.SetValue(_name, culture, value);
            }
            catch (ArgumentException ex)
            {
                _owner.RaiseError(ex.Message);
            }

            Raise();
        }
    }

    /// <summary>
    /// The rows of one category for the grid view, kept in step with the model: <see cref="Refresh"/> updates the
    /// existing rows in place when the set of names did not change (so an edit in progress survives) and rebuilds
    /// them otherwise. Pure (no Visual Studio, no WPF controls): unit-tested.
    /// </summary>
    public sealed class ResourceGridModel
    {
        public ResourceGridModel(ResourceSetModel model, ResourceCategory category)
        {
            Model = model;
            Category = category;
            Refresh();
        }

        public ResourceSetModel Model { get; }

        public ResourceCategory Category { get; }

        public ObservableCollection<ResourceRow> Rows { get; } = new ObservableCollection<ResourceRow>();

        public IReadOnlyList<string> Cultures => Model.Cultures;

        /// <summary>Raised when a write was refused by the model (invalid or duplicate name, bad colour).</summary>
        public event EventHandler<string>? Error;

        internal void RaiseError(string message) => Error?.Invoke(this, message);

        /// <summary>Brings the rows in line with the model.</summary>
        public void Refresh()
        {
            var names = Model.InCategory(Category).Select(e => e.Name).ToList();
            if (names.SequenceEqual(Rows.Select(r => r.Name)))
            {
                foreach (var row in Rows)
                {
                    row.Raise();
                }

                return;
            }

            Rows.Clear();
            foreach (var name in names)
            {
                Rows.Add(new ResourceRow(this, name));
            }
        }

        /// <summary>The number of String entries <paramref name="culture"/> does not translate.</summary>
        public int MissingCount(string culture) => Model.MissingTranslations(culture).Count;
    }
}
