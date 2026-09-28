using System.Collections.Generic;
using System.Text.Json;
using Kubuno.Launch;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Launch.Tests
{
    [TestClass]
    public sealed class LaunchVsJsonWriterTests
    {
        private static LaunchDescription SampleDescription() => new()
        {
            Name = "hello",
            ExecutablePath = @"C:\kubuno-build\desktop-target\debug\examples\hello.exe",
            Arguments = new[] { "a1", "a2" },
            WorkingDirectory = @"Z:\projects\kubuno\desktop\windows",
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["RUST_BACKTRACE"] = "1",
                ["PATH"] = @"C:\kubuno-build\desktop-target\debug;C:\Windows\System32",
            },
            NatvisFiles = new[] { @"C:\...\rustlib\etc\libstd.natvis" },
            Debugger = "native",
        };

        [TestMethod]
        public void WriteConfigurationEntry_ProducesValidJson_WithExpectedFields()
        {
            var text = LaunchVsJsonWriter.WriteConfigurationEntry(SampleDescription(), @"target\debug\examples\hello.exe");

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            Assert.AreEqual("default", root.GetProperty("type").GetString());
            Assert.AreEqual(@"target\debug\examples\hello.exe", root.GetProperty("project").GetString());
            Assert.AreEqual(string.Empty, root.GetProperty("projectTarget").GetString());
            Assert.AreEqual("hello", root.GetProperty("name").GetString());
            Assert.AreEqual(@"Z:\projects\kubuno\desktop\windows", root.GetProperty("currentDir").GetString());

            var args = root.GetProperty("args");
            Assert.AreEqual(2, args.GetArrayLength());
            Assert.AreEqual("a1", args[0].GetString());
            Assert.AreEqual("a2", args[1].GetString());
        }

        [TestMethod]
        public void WriteConfigurationEntry_EmitsEnvAsAPlainVarToValueObject()
        {
            // Not an array of {name, value} objects (that shape is for C++ Linux's `environment`
            // property, and real-world CMake launch.vs.json files - but confirmed live that VS's
            // native debug engine silently ignores it for a "type": "default" configuration's own
            // `env`, which must be a plain object - see LaunchVsJsonWriter's own remarks).
            var text = LaunchVsJsonWriter.WriteConfigurationEntry(SampleDescription(), @"target\debug\examples\hello.exe");

            using var doc = JsonDocument.Parse(text);
            var env = doc.RootElement.GetProperty("env");

            Assert.AreEqual(JsonValueKind.Object, env.ValueKind);
            Assert.AreEqual("1", env.GetProperty("RUST_BACKTRACE").GetString());
            Assert.AreEqual(@"C:\kubuno-build\desktop-target\debug;C:\Windows\System32", env.GetProperty("PATH").GetString());
        }

        [TestMethod]
        public void WriteConfigurationEntry_OmitsArgsAndEnv_WhenEmpty()
        {
            var description = new LaunchDescription
            {
                Name = "kubuno",
                ExecutablePath = @"C:\kubuno-build\desktop-target\debug\kubuno.exe",
                WorkingDirectory = @"Z:\projects\kubuno\desktop\windows",
            };

            var text = LaunchVsJsonWriter.WriteConfigurationEntry(description, @"target\debug\kubuno.exe");

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            Assert.IsFalse(root.TryGetProperty("args", out _));
            Assert.IsFalse(root.TryGetProperty("env", out _));
            // Explicitly not emitted: no documented natvis field exists for a local
            // "type": "default" configuration (see LaunchVsJsonWriter's XML doc remarks).
            Assert.IsFalse(root.TryGetProperty("natvisFile", out _));
            Assert.IsFalse(root.TryGetProperty("natvis", out _));
        }

        [TestMethod]
        public void WriteConfigurationEntry_EscapesBackslashesAndQuotesInPaths()
        {
            var description = new LaunchDescription
            {
                Name = "weird \"name\"",
                ExecutablePath = @"C:\path\to\app.exe",
                WorkingDirectory = @"C:\path\with\backslashes",
            };

            var text = LaunchVsJsonWriter.WriteConfigurationEntry(description, @"target\debug\app.exe");

            using var doc = JsonDocument.Parse(text); // throws if escaping is wrong
            Assert.AreEqual("weird \"name\"", doc.RootElement.GetProperty("name").GetString());
            Assert.AreEqual(@"C:\path\with\backslashes", doc.RootElement.GetProperty("currentDir").GetString());
        }

        [TestMethod]
        public void WriteFile_ProducesParsableLaunchVsJson_WithVersionAndConfigurationsArray()
        {
            var text = LaunchVsJsonWriter.WriteFile(new[]
            {
                (SampleDescription(), @"target\debug\examples\hello.exe"),
            });

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            Assert.AreEqual("0.2.1", root.GetProperty("version").GetString());
            Assert.AreEqual(JsonValueKind.Object, root.GetProperty("defaults").ValueKind);

            var configurations = root.GetProperty("configurations");
            Assert.AreEqual(1, configurations.GetArrayLength());
            Assert.AreEqual("hello", configurations[0].GetProperty("name").GetString());
        }
    }
}
