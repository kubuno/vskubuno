using System.Linq;
using Kubuno.Views.Logic.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Views.Tests.Settings
{
    /// <summary>
    /// docs/STORAGE-COMPONENTS.md, lot ST-2: the per-account settings attribute (same canonical text as the Rust model's
    /// <c>account_scoped_files_round_trip</c>), the settings editor's undo/redo history and the Registry key picker's
    /// path model.
    /// </summary>
    [TestClass]
    public class SettingsStorageTests
    {
        private const string AccountScopedSample =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<Settings Version=\"1\" App=\"mail\" AccountScoped=\"true\">\n" +
            "  <Setting Name=\"Signature\" Type=\"String\"/>\n</Settings>\n";

        [TestMethod]
        public void Account_scoped_files_round_trip_like_the_rust_model()
        {
            var file = KbsettingsFile.Parse(AccountScopedSample, out var errors);
            Assert.AreEqual(0, errors.Count, string.Join("; ", errors));
            Assert.IsTrue(file.AccountScoped);
            Assert.AreEqual(AccountScopedSample, file.ToText());

            file.AccountScoped = false;
            StringAssert.DoesNotMatch(file.ToText(), new System.Text.RegularExpressions.Regex("AccountScoped"), "false is the default and is omitted");

            KbsettingsFile.Parse("<Settings AccountScoped=\"maybe\"/>", out var bad);
            Assert.AreEqual(1, bad.Count);
        }

        [TestMethod]
        public void The_history_undoes_and_redoes_the_grid_changes()
        {
            var h = new SettingsHistory();
            Assert.IsFalse(h.CanUndo);
            h.Record("a", "b");
            h.Record("b", "c");
            h.Record("c", "c");
            Assert.AreEqual("b", h.Undo("c"));
            Assert.AreEqual("a", h.Undo("b"));
            Assert.IsNull(h.Undo("a"));
            Assert.AreEqual("b", h.Redo("a"));
            Assert.IsTrue(h.CanRedo);
            h.Record("b", "x");
            Assert.IsFalse(h.CanRedo, "a new change drops the redo steps");
            h.Clear();
            Assert.IsFalse(h.CanUndo);

            for (var i = 0; i < SettingsHistory.Capacity + 10; i++)
            {
                h.Record("s" + i, "s" + (i + 1));
            }

            var steps = 0;
            var current = "s" + (SettingsHistory.Capacity + 10);
            while (h.Undo(current) is { } previous)
            {
                current = previous;
                steps++;
            }

            Assert.AreEqual(SettingsHistory.Capacity, steps);
        }

        [TestMethod]
        public void Registry_paths_split_and_join()
        {
            Assert.AreEqual(("CurrentUser", @"Software\Kubuno"), RegistryKeyPath.Parse(@"HKEY_CURRENT_USER\Software\Kubuno"));
            Assert.AreEqual(("LocalMachine", @"SOFTWARE\Microsoft"), RegistryKeyPath.Parse("HKLM/SOFTWARE//Microsoft/"));
            Assert.AreEqual(((string?)null, @"Software\Kubuno"), RegistryKeyPath.Parse(@"Software\Kubuno"));
            Assert.AreEqual("ClassesRoot", RegistryKeyPath.HiveName("hkcr"));
            Assert.IsNull(RegistryKeyPath.HiveName("HKXX"));
            Assert.AreEqual(@"HKCU\Software\Kubuno", RegistryKeyPath.Display("CurrentUser", @"Software\Kubuno"));
            Assert.AreEqual("HKLM", RegistryKeyPath.Display("LocalMachine", string.Empty));
            Assert.AreEqual(@"a\b", RegistryKeyPath.Join("a", "b"));
            Assert.AreEqual("b", RegistryKeyPath.Join(string.Empty, "b"));
            CollectionAssert.AreEqual(new[] { "Software", "Kubuno" }, RegistryKeyPath.Segments(@"HKCU\Software\Kubuno").ToArray());
        }
    }
}
