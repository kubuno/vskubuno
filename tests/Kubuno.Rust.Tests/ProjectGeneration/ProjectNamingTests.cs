using Kubuno.Rust.Cargo.Naming;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Rust.Tests.ProjectGeneration
{
    /// <summary>Kubuno.&lt;Product&gt;.&lt;Component&gt; projects for kubuno-&lt;product&gt;-&lt;component&gt; crates (docs/RSPROJ.md).</summary>
    [TestClass]
    public sealed class ProjectNamingTests
    {
        [TestMethod]
        [DataRow("kubuno-desktop", "Kubuno.Desktop")]
        [DataRow("kubuno-desktop-ui", "Kubuno.Desktop.UI")]
        [DataRow("kubuno-desktop-controls", "Kubuno.Desktop.Controls")]
        [DataRow("kubuno-desktop-views-ls", "Kubuno.Desktop.Views.LS")]
        [DataRow("kubuno-desktop-views-macros", "Kubuno.Desktop.Views.Macros")]
        [DataRow("kubuno-desktop-shell", "Kubuno.Desktop.Shell")]
        [DataRow("kubuno-desktop-app-storage-components", "Kubuno.Desktop.App.Storage.Components")]
        [DataRow("kubuno-chat-desktop", "Kubuno.Chat.Desktop")]
        [DataRow("kubuno-office-desktop", "Kubuno.Office.Desktop")]
        [DataRow("kubuno-office-docs-core", "Kubuno.Office.Docs.Core")]
        [DataRow("kubuno-drive-desktop", "Kubuno.Drive.Desktop")]
        [DataRow("kubuno-drive-desktop-app-controls", "Kubuno.Drive.Desktop.App.Controls")]
        [DataRow("kubuno-web-views-compiler-core", "Kubuno.Web.Views.Compiler.Core")]
        [DataRow("hello-rust", "hello-rust")]
        [DataRow("kubuno", "kubuno")]
        [DataRow("suppaftp", "suppaftp")]
        public void A_kubuno_crate_becomes_a_dotted_project_name(string crate, string project)
        {
            Assert.AreEqual(project, ProjectNaming.ForCrate(crate));
        }

        [TestMethod]
        public void The_core_names_its_server_and_libraries_after_the_core()
        {
            Assert.AreEqual("Kubuno.Core.Server", ProjectNaming.ForCoreCrate("kubuno-core", isRun: true));
            Assert.AreEqual("Kubuno.Core.Server", ProjectNaming.ForCoreCrate("kubuno-core", isRun: false));
            Assert.AreEqual("Kubuno.Core.Db", ProjectNaming.ForCoreCrate("kubuno-db", isRun: false));
            Assert.AreEqual("Kubuno.Core.Modauth", ProjectNaming.ForCoreCrate("kubuno-modauth", isRun: false));
            Assert.AreEqual("Kubuno.Core.Frontend", ProjectNaming.CoreFrontend);
        }

        [TestMethod]
        public void A_module_names_its_server_libraries_and_frontend_after_its_id()
        {
            Assert.AreEqual("Kubuno.Drive.Server", ProjectNaming.ForModuleCrate("drive", "kubuno-drive", isRun: true));
            Assert.AreEqual("Kubuno.P2pnas.Server", ProjectNaming.ForModuleCrate("p2pnas", "kubuno-p2pnas", isRun: true));
            Assert.AreEqual("Kubuno.P2pnas.Core", ProjectNaming.ForModuleCrate("p2pnas", "p2pnas-core", isRun: false));
            Assert.AreEqual("Kubuno.Forms.Core", ProjectNaming.ForModuleCrate("forms", "kubuno-forms-core", isRun: false));
            Assert.AreEqual("Kubuno.Stt.Engine", ProjectNaming.ForModuleCrate("stt", "engine", isRun: false));
            Assert.AreEqual("Kubuno.Stt.Web", ProjectNaming.ForModuleFrontend("stt"));
            Assert.AreEqual("Kubuno.P2pNas.slnx", ProjectNaming.ModuleSolutionFileName("p2p-nas"));
        }

        [TestMethod]
        public void Npm_packages_keep_their_npm_names_and_get_web_project_names()
        {
            Assert.AreEqual("Kubuno.Web.UI", ProjectNaming.ForNpmPackage("@kubuno/ui", "ui", null));
            Assert.AreEqual("Kubuno.Web.Sdk", ProjectNaming.ForNpmPackage("@kubuno/sdk", "sdk", null));
            Assert.AreEqual("Kubuno.Web.Drive", ProjectNaming.ForNpmPackage("@kubuno/drive", "drive", null));
            Assert.AreEqual("Kubuno.Web.Views.Compiler", ProjectNaming.ForNpmPackage("@kubuno/views-compiler", "views-compiler", null));
            Assert.AreEqual("Kubuno.Calendar.Web.Widgets", ProjectNaming.ForNpmPackage("@kubuno-modules/calendar-widgets", "widgets", "calendar"));
        }
    }
}
