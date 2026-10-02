using System.Linq;
using Kubuno.Views.Logic.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Views.Tests.Settings
{
    /// <summary>
    /// The <c>.kbsettings</c> model of the settings editor (docs/STORAGE-COMPONENTS.md §5.3). <see cref="Sample"/> is
    /// the SAME text as the Rust model's test (<c>kubuno-resources-model/src/settings.rs</c>, <c>SAMPLE</c>): both
    /// writers must produce it byte for byte, so the editor and the macro never fight over the file.
    /// </summary>
    [TestClass]
    public class KbsettingsFileTests
    {
        private const string Sample =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<Settings Version=\"2\" App=\"kubuno-notes\">\n" +
            "  <Setting Name=\"Theme\" Type=\"String\" Default=\"System\" Values=\"System|Light|Dark\" Description=\"The colour &amp; theme.\"/>\n" +
            "  <Setting Name=\"SyncIntervalMinutes\" Type=\"Int\" Default=\"5\" PreviousNames=\"SyncInterval\"/>\n" +
            "  <Setting Name=\"WindowBounds\" Type=\"String\" Roaming=\"false\"/>\n" +
            "  <Setting Name=\"ShowHidden\" Type=\"Bool\" Default=\"false\"/>\n" +
            "  <Setting Name=\"UpdateChannel\" Type=\"String\" Scope=\"Application\" Default=\"stable\"/>\n" +
            "  <Setting Name=\"RecentFiles\" Type=\"StringList\" Roaming=\"false\">\n    <Item>welcome.kbdoc</Item>\n    <Item>a &lt; b</Item>\n  </Setting>\n" +
            "</Settings>\n";

        [TestMethod]
        public void Reads_and_writes_the_canonical_form_of_the_rust_model()
        {
            var file = KbsettingsFile.Parse(Sample, out var errors);
            Assert.AreEqual(0, errors.Count, string.Join("; ", errors));
            Assert.AreEqual(2, file.Version);
            Assert.AreEqual("kubuno-notes", file.App);
            Assert.AreEqual(6, file.Entries.Count);
            Assert.AreEqual("The colour & theme.", file.Entries[0].Description);
            CollectionAssert.AreEqual(new[] { "System", "Light", "Dark" }, file.Entries[0].Values);
            Assert.IsFalse(file.Entries[2].Roaming);
            Assert.IsTrue(file.Entries[4].IsApplication);
            Assert.AreEqual("welcome.kbdoc\na < b", file.Entries[5].Default);
            Assert.AreEqual(4, file.Entries[1].Line);
            Assert.AreEqual(Sample, file.ToText(), "the canonical text, identical to the Rust writer's");
        }

        [TestMethod]
        public void Validation_follows_the_rust_rules()
        {
            var file = new KbsettingsFile();
            file.Entries.Add(new SettingEntry { Name = "1x" });
            file.Entries.Add(new SettingEntry { Name = "Count", Type = "Int", Default = "1.5" });
            file.Entries.Add(new SettingEntry { Name = "Mode", Default = "Blue", Values = { "Red", "Green" } });
            file.Entries.Add(new SettingEntry { Name = "Flag", Type = "Bool", Default = "true", Values = { "x" } });
            file.Entries.Add(new SettingEntry { Name = "a" });
            file.Entries.Add(new SettingEntry { Name = "A" });
            file.Entries.Add(new SettingEntry { Name = "SmtpPassword" });
            var problems = file.Validate().ToList();
            Assert.AreEqual(5, problems.Count(p => p.IsError), string.Join("\n", problems.Select(p => p.Message)));
            Assert.IsTrue(problems.Any(p => !p.IsError && p.Message.Contains("looks like a secret")));
        }

        [TestMethod]
        public void Migrated_type_names_defaults_and_blank_files()
        {
            var file = KbsettingsFile.Parse("<Settings><Setting Name=\"N\" Type=\"System.Int32\"/><Setting Name=\"M\" Values=\"Fast|Safe\"/><Setting Name=\"B\" Type=\"Boolean\" Default=\"True\"/></Settings>", out var errors);
            Assert.AreEqual(0, errors.Count, string.Join("; ", errors));
            Assert.AreEqual("Int", file.Entries[0].Type);
            Assert.AreEqual("0", file.Entries[0].Default);
            Assert.AreEqual("Fast", file.Entries[1].Default, "the first accepted value");
            Assert.AreEqual("true", file.Entries[2].Default);
            Assert.AreEqual("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<Settings Version=\"1\"/>\n", KbsettingsFile.Blank());
            KbsettingsFile.Parse("<Settings><Setting", out var broken);
            Assert.AreEqual(1, broken.Count);
        }
    }
}
