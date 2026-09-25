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

            viewModel.SearchText = "butt";

            var family = viewModel.Families.Single();
            Assert.AreEqual("core", family.Family);
            Assert.AreEqual("Button", family.Items.Single().DisplayName);
        }

        [TestMethod]
        public void SearchText_DropsFamiliesWithNoMatchingItems()
        {
            var viewModel = CreateViewModel();

            viewModel.SearchText = "grid";

            var family = viewModel.Families.Single();
            Assert.AreEqual("data", family.Family);
            Assert.AreEqual("DataGrid", family.Items.Single().DisplayName);
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
