using Kubuno.VisualStudio.Designer.PropertyBrowser;
using Kubuno.VisualStudio.Designer.Selection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Designer.Tests.Selection
{
    [TestClass]
    public class XmlCharacterReferencesTests
    {
        [TestMethod]
        public void AttributeValuesAreDecodedLikeTheRuntimeReadsThem()
        {
            Assert.AreEqual("&Save <b> \"x\" ' AB", XmlCharacterReferences.Decode("&amp;Save &lt;b&gt; &quot;x&quot; &apos; &#65;&#x42;"));
            Assert.AreEqual("&Save && R&D &unknown; &", XmlCharacterReferences.Decode("&Save && R&D &unknown; &"));
        }

        [TestMethod]
        public void TheReaderShowsTheMnemonicTextAndThePlannerWritesItBack()
        {
            var attributes = ElementAttributeReader.Read("<Panel><Button Text=\"&amp;Save\"/></Panel>", "0");
            Assert.IsNotNull(attributes);
            Assert.AreEqual("&Save", attributes!.Attributes["Text"]);
            Assert.AreEqual("&amp;Save &quot;now&quot;", ChildCollectionPlanner.Escape("&Save \"now\""));
            foreach (var value in new[] { "&Save", "a < b > c", "say \"hi\"", "it's", "two\nlines", "tab\there", "cr\r\nlf", "&amp; literally", "R&D; &#65;" })
            {
                Assert.AreEqual(value, XmlCharacterReferences.Decode(ChildCollectionPlanner.Escape(value)), value);
            }
        }
    }
}
