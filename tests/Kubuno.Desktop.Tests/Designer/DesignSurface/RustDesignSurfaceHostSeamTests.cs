using Kubuno.Desktop.Designer.DesignSurface;
using Kubuno.Views.Designer.DesignSurface;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.Designer.DesignSurface
{
    /// <summary>
    /// docs/WEB-VIEWS.md WV-8: the views layer's designer only reaches the live features of a design surface (drops, edit
    /// batches, context menus, zoom, design language...) through <see cref="IProtocolDesignSurfaceHost"/>, so the Rust
    /// surface must implement it - without it the desktop designer silently loses all of them.
    /// </summary>
    [TestClass]
    public sealed class RustDesignSurfaceHostSeamTests
    {
        [TestMethod]
        public void The_rust_surface_is_a_protocol_design_surface_with_status_and_runtime()
        {
            Assert.IsTrue(typeof(IProtocolDesignSurfaceHost).IsAssignableFrom(typeof(RustDesignSurfaceHost)));
            Assert.IsTrue(typeof(IDesignSurfaceStatusAware).IsAssignableFrom(typeof(RustDesignSurfaceHost)));
            Assert.IsTrue(typeof(IDesignSurfaceRuntimeAware).IsAssignableFrom(typeof(RustDesignSurfaceHost)));
            Assert.IsTrue(typeof(IDesignSurfaceHostFactory).IsAssignableFrom(typeof(RustDesignSurfaceHostFactory)));
        }
    }
}
