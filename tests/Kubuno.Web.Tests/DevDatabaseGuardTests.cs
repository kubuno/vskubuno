using System;
using System.Linq;
using Kubuno.Web.Logic.DevCore;
using Kubuno.Web.Logic.DevDatabase;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Web.Tests
{
    [TestClass]
    public sealed class DevDatabaseGuardTests
    {
        [TestMethod]
        public void A_missing_url_is_refused_with_the_variable_and_an_example()
        {
            var check = DevDatabaseGuard.Check(null, null);
            Assert.IsFalse(check.IsAccepted);
            Assert.AreEqual(DevDatabaseVerdict.Missing, check.Verdict);
            StringAssert.Contains(check.Message, "KUBUNO_DEV_DATABASE_URL is not set");
            StringAssert.Contains(check.Message, "kubuno_dev");
        }

        [TestMethod]
        public void The_tunnelled_kubuno_dev_database_is_accepted()
        {
            var check = DevDatabaseGuard.Check("postgres://kubuno:s3cret@localhost:55432/kubuno_dev", null);
            Assert.IsTrue(check.IsAccepted);
            Assert.AreEqual(DevDatabaseVerdict.DevName, check.Verdict);
            Assert.AreEqual("localhost", check.Url!.Host);
            Assert.AreEqual("55432", check.Url.Port);
            Assert.AreEqual("postgres://kubuno:***@localhost:55432/kubuno_dev", check.Url.Redacted);
            StringAssert.Contains(DevDatabaseGuard.ExampleUrl, "localhost:55432/kubuno_dev");
        }

        [TestMethod]
        public void A_dev_database_is_accepted()
        {
            var check = DevDatabaseGuard.Check("postgres://kubuno:p%40ss@192.168.1.220:5432/kubuno_dev?sslmode=prefer", null);
            Assert.IsTrue(check.IsAccepted);
            Assert.AreEqual("kubuno_dev", check.Url!.Database);
            Assert.AreEqual("192.168.1.220", check.Url.Host);
            Assert.AreEqual("5432", check.Url.Port);
            Assert.AreEqual("postgres://kubuno:***@192.168.1.220:5432/kubuno_dev", check.Url.Redacted);
        }

        [TestMethod]
        public void The_live_database_name_is_refused_without_the_override_and_never_shows_the_password()
        {
            var check = DevDatabaseGuard.Check("postgres://kubuno:s3cret@192.168.1.220:5432/kubuno", null);
            Assert.IsFalse(check.IsAccepted);
            Assert.AreEqual(DevDatabaseVerdict.NotDevName, check.Verdict);
            Assert.IsFalse(check.Message!.Contains("s3cret"));
            StringAssert.Contains(check.Message, "KUBUNO_DEV_ALLOW_ANY_DATABASE");

            var overridden = DevDatabaseGuard.Check("postgres://kubuno:s3cret@192.168.1.220:5432/kubuno", "1");
            Assert.IsTrue(overridden.IsAccepted);
            Assert.AreEqual(DevDatabaseVerdict.Overridden, overridden.Verdict);
        }

        [TestMethod]
        [DataRow("kubuno_dev", true)]
        [DataRow("kubuno-test", true)]
        [DataRow("dev_kubuno2", true)]
        [DataRow("kubuno_dev3", true)]
        [DataRow("KUBUNO_DEVELOPMENT", true)]
        [DataRow("sandbox", true)]
        [DataRow("kubuno", false)]
        [DataRow("kubunodev", false)]
        [DataRow("device_registry", false)]
        [DataRow("prod", false)]
        public void Dev_names_are_recognized_by_whole_tokens(string name, bool expected)
        {
            Assert.AreEqual(expected, DevDatabaseGuard.LooksLikeDevDatabase(name));
        }

        [TestMethod]
        public void Passwords_with_at_signs_and_urls_without_database_are_handled()
        {
            var url = DatabaseUrl.TryParse("postgresql://user:a@b/c@db.local/kubuno_test")!;
            Assert.AreEqual("db.local", url.Host);
            Assert.AreEqual("kubuno_test", url.Database);
            Assert.AreEqual("user", url.User);

            var noDatabase = DevDatabaseGuard.Check("postgres://user:pw@host:5432", null);
            Assert.AreEqual(DevDatabaseVerdict.Invalid, noDatabase.Verdict);
            Assert.AreEqual(DevDatabaseVerdict.Invalid, DevDatabaseGuard.Check("not a url", null).Verdict);
        }

        [TestMethod]
        public void The_dev_core_environment_replaces_every_linux_path()
        {
            var layout = new DevCoreLayout(@"C:\dev-core");
            var environment = DevCoreEnvironment.Build(layout, "postgres://u:p@h/kubuno_dev", new DevCoreSecrets("kubunodev_a", "kubunodev_b"), @"Z:\core\frontend\dist", 8080);
            Assert.AreEqual("postgres://u:p@h/kubuno_dev", environment["KV__DATABASE__URL"]);
            Assert.AreEqual(@"C:\dev-core\modules", environment["KV__SERVER__MODULES_DIR"]);
            Assert.AreEqual(@"C:\dev-core\modules-store", environment["KV__SERVER__MODULES_INSTALL_DIR"]);
            Assert.AreEqual(@"Z:\core\frontend\dist", environment["KV__SERVER__FRONTEND_DIST"]);
            Assert.AreEqual("8080", environment["KV__SERVER__PORT"]);
            Assert.AreEqual(@"C:\dev-core\data.key", environment["KUBUNO_DATA_KEY_FILE"]);
            Assert.IsTrue(environment.Values.All(value => !value.StartsWith("/")), "no Linux default left");
            CollectionAssert.IsSubsetOf(new[] { "KV__DATABASE__URL", "KV__SERVER__INTERNAL_SECRET", "KV__AUTH__JWT_SECRET" }, DevCoreEnvironment.SecretNames.ToList());
        }

        [TestMethod]
        public void The_dev_core_platform_layout_stays_in_the_dev_core_folder()
        {
            // Without KUBUNO_PATHS_*, a Windows core runs in system mode and uses %ProgramData%\Kubuno: an installed
            // Kubuno's config.toml would be read, and state, backups and module data written next to it.
            var layout = new DevCoreLayout(@"C:\dev-core");
            var environment = DevCoreEnvironment.Build(layout, "postgres://u:p@h/kubuno_dev", new DevCoreSecrets("kubunodev_a", "kubunodev_b"), null, 8080);
            Assert.AreEqual("user", environment["KUBUNO_PATHS_MODE"]);
            var directories = new[]
            {
                "KUBUNO_PATHS_CONFIG_DIR", "KUBUNO_PATHS_STATE_DIR", "KUBUNO_PATHS_DATA_DIR", "KUBUNO_PATHS_LOG_DIR", "KUBUNO_PATHS_CACHE_DIR",
                "KUBUNO_PATHS_RUNTIME_DIR", "KUBUNO_PATHS_BACKUP_DIR", "KUBUNO_PATHS_MODULES_STORE", "KUBUNO_PATHS_MODULES_CONFIG_DIR",
                "KUBUNO_PATHS_MODULES_DATA_DIR",
            };
            foreach (var name in directories)
            {
                Assert.IsTrue(environment[name].StartsWith(@"C:\dev-core",StringComparison.OrdinalIgnoreCase), name + " = " + environment[name]);
            }

            Assert.AreEqual(environment["KV__SERVER__MODULES_INSTALL_DIR"], environment["KUBUNO_PATHS_MODULES_STORE"]);
            Assert.AreEqual(environment["KV__SERVER__MODULES_CONFIG_DIR"], environment["KUBUNO_PATHS_MODULES_CONFIG_DIR"]);
            Assert.AreEqual(environment["KV__SERVER__MODULES_DATA_DIR"], environment["KUBUNO_PATHS_MODULES_DATA_DIR"]);
            Assert.AreEqual(@"C:\dev-core\config", environment["KUBUNO_PATHS_CONFIG_DIR"]);
        }

        [TestMethod]
        public void F5_opens_chrome_by_default_and_edge_only_as_a_fallback()
        {
            var environment = new System.Collections.Generic.Dictionary<string, string>
            {
                ["ProgramFiles"] = @"C:\Program Files",
                ["ProgramFiles(x86)"] = @"C:\Program Files (x86)",
                ["LOCALAPPDATA"] = @"C:\Users\dev\AppData\Local",
            };
            string? Env(string name) => environment.TryGetValue(name, out var value) ? value : null;
            const string Chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
            const string UserChrome = @"C:\Users\dev\AppData\Local\Google\Chrome\Application\chrome.exe";
            const string Edge = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";

            var both = new[] { Chrome, Edge };
            Assert.AreEqual(Chrome, DevBrowser.Resolve(null, Env, both.Contains), "Chrome is the default");
            Assert.AreEqual(Chrome, DevBrowser.Resolve("", Env, both.Contains));
            Assert.AreEqual(UserChrome, DevBrowser.Resolve(null, Env, new[] { UserChrome, Edge }.Contains), "a per-user Chrome install counts");
            Assert.AreEqual(Edge, DevBrowser.Resolve(null, Env, new[] { Edge }.Contains), "Edge only when Chrome is missing");
            Assert.IsNull(DevBrowser.Resolve(null, Env, _ => false), "then the system's default browser");
            Assert.AreEqual(Edge, DevBrowser.Resolve("edge", Env, both.Contains), "Edge on purpose (KubunoBrowser=edge)");
            Assert.IsNull(DevBrowser.Resolve("default", Env, both.Contains));
        }

        [TestMethod]
        public void Every_module_folder_the_core_starts_from_is_cleaned_up_with_it()
        {
            // Stop Debugging ends the core but not the modules it started: each folder the core runs module
            // executables from - the F5 deployments and the .kbpkg store - is swept, not only the debugged module's.
            var layout = new DevCoreLayout(@"C:\dev-core");
            CollectionAssert.AreEquivalent(
                new[] { @"C:\dev-core\modules", @"C:\dev-core\modules-store" },
                layout.ModuleProcessDirectories.ToList());
            var environment = DevCoreEnvironment.Build(layout, "postgres://u:p@h/kubuno_dev", new DevCoreSecrets("kubunodev_a", "kubunodev_b"), null, 8080);
            CollectionAssert.AreEquivalent(
                new[] { environment["KV__SERVER__MODULES_DIR"], environment["KV__SERVER__MODULES_INSTALL_DIR"] },
                layout.ModuleProcessDirectories.ToList());
        }
    }
}
