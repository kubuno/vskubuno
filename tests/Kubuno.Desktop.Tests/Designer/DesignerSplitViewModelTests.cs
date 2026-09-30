using System.Collections.Generic;
using Kubuno.VisualStudio.Designer.UI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Designer.Tests
{
    [TestClass]
    public class DesignerSplitViewModelTests
    {
        [TestMethod]
        public void InitialMode_IsSplit_WithBothPanesAndSplitterVisible()
        {
            var model = new DesignerSplitViewModel();

            Assert.AreEqual(DesignerViewMode.Split, model.Mode);
            Assert.IsTrue(model.IsDesignPaneVisible);
            Assert.IsTrue(model.IsXmlPaneVisible);
            Assert.IsTrue(model.IsSplitterVisible);
        }

        [TestMethod]
        public void Design_HidesXmlPane_AndSplitter()
        {
            var model = new DesignerSplitViewModel { Mode = DesignerViewMode.Design };

            Assert.IsTrue(model.IsDesignPaneVisible);
            Assert.IsFalse(model.IsXmlPaneVisible);
            Assert.IsFalse(model.IsSplitterVisible);
        }

        [TestMethod]
        public void Xml_HidesDesignPane_AndSplitter()
        {
            var model = new DesignerSplitViewModel { Mode = DesignerViewMode.Xml };

            Assert.IsFalse(model.IsDesignPaneVisible);
            Assert.IsTrue(model.IsXmlPaneVisible);
            Assert.IsFalse(model.IsSplitterVisible);
        }

        [TestMethod]
        public void Split_ShowsBothPanes_AndSplitter()
        {
            var model = new DesignerSplitViewModel { Mode = DesignerViewMode.Design };

            model.Mode = DesignerViewMode.Split;

            Assert.IsTrue(model.IsDesignPaneVisible);
            Assert.IsTrue(model.IsXmlPaneVisible);
            Assert.IsTrue(model.IsSplitterVisible);
        }

        [TestMethod]
        public void SettingSameMode_DoesNotRaisePropertyChanged()
        {
            var model = new DesignerSplitViewModel();
            var raisedProperties = new List<string?>();
            model.PropertyChanged += (_, e) => raisedProperties.Add(e.PropertyName);

            model.Mode = DesignerViewMode.Split; // already Split - a no-op change

            Assert.AreEqual(0, raisedProperties.Count);
        }

        [TestMethod]
        public void ChangingMode_RaisesPropertyChanged_ForModeAndAllDerivedVisibilities()
        {
            var model = new DesignerSplitViewModel();
            var raisedProperties = new List<string?>();
            model.PropertyChanged += (_, e) => raisedProperties.Add(e.PropertyName);

            model.Mode = DesignerViewMode.Design;

            CollectionAssert.AreEquivalent(
                new[] { nameof(DesignerSplitViewModel.Mode), nameof(DesignerSplitViewModel.IsDesignPaneVisible), nameof(DesignerSplitViewModel.IsXmlPaneVisible), nameof(DesignerSplitViewModel.IsSplitterVisible) },
                raisedProperties);
        }
    }
}
