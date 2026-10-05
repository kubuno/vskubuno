using Kubuno.Views.Designer.DesignSurface;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Views.Tests.Designer.DesignSurface
{
    /// <summary>docs/WEB-VIEWS.md section 4.1 (WV-9b): a target factory claims its documents, the default factory renders the rest.</summary>
    [TestClass]
    public class DesignSurfaceHostFactoryHostTests
    {
        private sealed class WebLike : ITargetDesignSurfaceHostFactory
        {
            public bool Handles(DesignSurfaceDocument document) => document.Path.Contains("frontend");

            public IDesignSurfaceHost Create(DesignSurfaceDocument? document) => new PlaceholderDesignSurfaceHost();
        }

        private sealed class Throwing : ITargetDesignSurfaceHostFactory
        {
            public bool Handles(DesignSurfaceDocument document) => throw new System.IO.IOException("share offline");

            public IDesignSurfaceHost Create(DesignSurfaceDocument? document) => throw new System.InvalidOperationException();
        }

        [TestMethod]
        public void A_target_factory_takes_its_documents_and_only_them()
        {
            var web = new WebLike();
            var throwing = new Throwing();
            DesignSurfaceHostFactoryHost.AddTargetFactory(throwing);
            DesignSurfaceHostFactoryHost.AddTargetFactory(web);
            DesignSurfaceHostFactoryHost.AddTargetFactory(web);
            try
            {
                Assert.AreSame(web, DesignSurfaceHostFactoryHost.For(new DesignSurfaceDocument(@"Z:\src\core\frontend\src\W.kbcontrol", null, 0)));
                Assert.AreSame(DesignSurfaceHostFactoryHost.Current, DesignSurfaceHostFactoryHost.For(new DesignSurfaceDocument(@"Z:\src\desktop\app\main_view.kbview", null, 0)));
                Assert.AreSame(DesignSurfaceHostFactoryHost.Current, DesignSurfaceHostFactoryHost.For(null), "no document: the default");
            }
            finally
            {
                DesignSurfaceHostFactoryHost.RemoveTargetFactory(web);
                DesignSurfaceHostFactoryHost.RemoveTargetFactory(throwing);
            }

            Assert.AreSame(DesignSurfaceHostFactoryHost.Current, DesignSurfaceHostFactoryHost.For(new DesignSurfaceDocument(@"Z:\src\core\frontend\src\W.kbcontrol", null, 0)));
        }
    }
}
