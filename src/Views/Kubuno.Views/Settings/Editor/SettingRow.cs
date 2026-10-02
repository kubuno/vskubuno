using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Kubuno.Views.Logic.Settings;

namespace Kubuno.Views.Settings.Editor
{
    /// <summary>
    /// One row of the settings grid: a <see cref="SettingEntry"/> as editable text (lists as <c>a|b</c>), with the
    /// problem the validation found on it (the row's tooltip).
    /// </summary>
    internal sealed class SettingRow : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _type = "String";
        private string _scope = "User";
        private bool _roaming = true;
        private string _value = string.Empty;
        private string _values = string.Empty;
        private string _previousNames = string.Empty;
        private string _description = string.Empty;
        private string? _problem;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Name { get => _name; set => Set(ref _name, value ?? string.Empty); }

        public string Type
        {
            get => _type;
            set
            {
                if (Set(ref _type, value ?? "String"))
                {
                    // A value that does not fit the new type is replaced by the type's zero (the user sees it at once).
                    if (_type != "StringList" && !KbsettingsFile.IsValidDefault(_type, _value))
                    {
                        Value = KbsettingsFile.ZeroDefault(_type);
                    }
                }
            }
        }

        public string Scope
        {
            get => _scope;
            set
            {
                if (Set(ref _scope, value ?? "User"))
                {
                    OnPropertyChanged(nameof(RoamingEnabled));
                }
            }
        }

        public bool Roaming { get => _roaming; set => Set(ref _roaming, value); }

        /// <summary>Roaming means something for a user setting only.</summary>
        public bool RoamingEnabled => _scope != "Application";

        /// <summary>The default value; a list's items separated by <c>|</c>.</summary>
        public string Value { get => _value; set => Set(ref _value, value ?? string.Empty); }

        /// <summary>The accepted values of a String, separated by <c>|</c>.</summary>
        public string Values { get => _values; set => Set(ref _values, value ?? string.Empty); }

        public string PreviousNames { get => _previousNames; set => Set(ref _previousNames, value ?? string.Empty); }

        public string Description { get => _description; set => Set(ref _description, value ?? string.Empty); }

        /// <summary>What the validation found on this row (null when nothing).</summary>
        public string? Problem { get => _problem; set => Set(ref _problem, value, notifyChange: false); }

        /// <summary>Raised when a property the file holds changed (not <see cref="Problem"/>).</summary>
        public event EventHandler? Edited;

        public static SettingRow From(SettingEntry e) => new SettingRow
        {
            _name = e.Name,
            _type = e.Type,
            _scope = e.Scope,
            _roaming = e.Roaming,
            _value = e.Type == "StringList" ? string.Join("|", e.Default.Split('\n').Where(s => s.Length > 0)) : e.Default,
            _values = string.Join("|", e.Values),
            _previousNames = string.Join("|", e.PreviousNames),
            _description = e.Description,
        };

        public SettingEntry ToEntry()
        {
            static System.Collections.Generic.List<string> Split(string s) => s.Split('|').Select(v => v.Trim()).Where(v => v.Length > 0).ToList();
            return new SettingEntry
            {
                Name = Name.Trim(),
                Type = Type,
                Scope = Scope,
                Roaming = Scope != "Application" && Roaming,
                Default = Type == "StringList" ? string.Join("\n", Split(Value)) : Value,
                Values = Split(Values),
                PreviousNames = Split(PreviousNames),
                Description = Description,
            };
        }

        private bool Set<T>(ref T field, T value, bool notifyChange = true, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(name);
            if (notifyChange)
            {
                Edited?.Invoke(this, EventArgs.Empty);
            }

            return true;
        }

        private void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
