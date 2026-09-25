using System.Linq;
using Kubuno.VisualStudio.Designer.Registry;
using Kubuno.VisualStudio.Designer.Toolbox;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Designer.Tests.Toolbox
{
    [TestClass]
    public class ToolboxViewModelTests
    {
        private static ToolboxViewModel CreateViewModel() =>
            new ToolboxViewModel(ComponentRegistry.FromJson(TestFixtures.ReadAllText("registry.sample.json")));

        [TestMethod]
        public void InitialState_ListsEveryFamily_WithNoFilter()
        {
            var viewModel = CreateViewModel();

            Assert.AreEqual(6, viewModel.Families.Count);
            Assert.AreEqual(5, viewModel.Families.Single(f => f.Family == "core").Items.Count);
        }

        [TestMethod]
        public void SearchText_FiltersItemsCaseInsensitively_AcrossFamilies()
        {
            var viewModel = CreateViewModel();

            // "butt" now also matches `IconButton`/`RadioButton` (family "choice")
            // in the real registry export the fixture carries (DSG-1), so this
            // case-insensitivity check uses "SWiTch" instead, which - across all
            // 49 real components - matches only `Switch` (family "core").
            viewModel.SearchText = "SWiTch";

            var family = viewModel.Families.Single();
            Assert.AreEqual("core", family.Family);
            Assert.AreEqual("Switch", family.Items.Single().DisplayName);
        }

        [TestMethod]
        public void SearchText_DropsFamiliesWithNoMatchingItems()
        {
            var viewModel = CreateViewModel();

            // "tree" matches only `TreeView` (family "data") in the real
            // registry export the fixture now carries (DSG-1) - the old
            // fictional "DataGrid"/"grid" pairing no longer exists.
            viewModel.SearchText = "tree";

            var family = viewModel.Families.Single();
            Assert.AreEqual("data", family.Family);
            Assert.AreEqual("TreeView", family.Items.Single().DisplayName);
        }

        [TestMethod]
        public void SearchText_NoMatches_YieldsNoFamilies()
        {
            var viewModel = CreateViewModel();

            viewModel.SearchText = "zzz-nothing-matches";

            Assert.AreEqual(0, viewModel.Families.Count);
        }

        [TestMethod]
        public void ClearingSearchText_RestoresEveryFamily()
        {
            var viewModel = CreateViewModel();
            viewModel.SearchText = "butt";

            viewModel.SearchText = "";

            Assert.AreEqual(6, viewModel.Families.Count);
        }

        [TestMethod]
        public void SearchText_RaisesPropertyChanged_ForSearchTextAndFamilies()
        {
            var viewModel = CreateViewModel();
            var raised = new System.Collections.Generic.List<string?>();
            viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            viewModel.SearchText = "butt";

            CollectionAssert.AreEquivalent(
                new[] { nameof(ToolboxViewModel.SearchText), nameof(ToolboxViewModel.Families) },
                raised);
        }
    }
}
