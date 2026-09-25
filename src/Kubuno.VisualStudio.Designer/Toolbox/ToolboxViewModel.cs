using System;
using System.ComponentModel;
using System.Linq;
using System.Collections.Generic;
using Kubuno.VisualStudio.Designer.Registry;

namespace Kubuno.VisualStudio.Designer.Toolbox
{
    /// <summary>
    /// Toolbox state: the registry grouped by family, filtered by <see cref="SearchText"/>
    /// (docs/DESIGNER.md §1: "Toolbox ... grouped by family with search"). Kept free of any WPF type so
    /// it is plain-unit-testable, the same shape as <c>UI.DesignerSplitViewModel</c> in this library
    /// (see that class's own doc comment); <see cref="Toolbox.ToolboxView"/> is the only consumer.
    /// </summary>
    public sealed class ToolboxViewModel : INotifyPropertyChanged
    {
        private readonly IReadOnlyList<ToolboxFamilyViewModel> _allFamilies;
        private string _searchText = string.Empty;
        private IReadOnlyList<ToolboxFamilyViewModel> _families;

        public ToolboxViewModel(ComponentRegistry registry)
        {
            if (registry is null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            _allFamilies = registry.FamilyNames
                .Select(family => new ToolboxFamilyViewModel(
                    family,
                    registry.Families[family].Select(c => new ToolboxItemViewModel(c)).ToList()))
                .ToList();
            _families = _allFamilies;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Case-insensitive substring match against each item's <see cref="ToolboxItemViewModel.DisplayName"/>; a family with no surviving items is dropped from <see cref="Families"/> entirely.</summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                var normalized = value ?? string.Empty;
                if (_searchText == normalized)
                {
                    return;
                }

                _searchText = normalized;
                ApplyFilter();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SearchText)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Families)));
            }
        }

        public IReadOnlyList<ToolboxFamilyViewModel> Families
        {
            get => _families;
            private set => _families = value;
        }

        private void ApplyFilter()
        {
            if (string.IsNullOrWhiteSpace(_searchText))
            {
                Families = _allFamilies;
                return;
            }

            Families = _allFamilies
                .Select(family => new ToolboxFamilyViewModel(
                    family.Family,
                    family.Items.Where(MatchesSearch).ToList()))
                .Where(family => family.Items.Count > 0)
                .ToList();
        }

        private bool MatchesSearch(ToolboxItemViewModel item) =>
            item.DisplayName.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
