using System.Diagnostics;
using Kubuno.Cargo.MSBuild.Tasks.Tests.Fakes;

namespace Kubuno.Cargo.MSBuild.Tasks.Tests.Win32
{
    /// <summary>
    /// How Kubuno.Rust.Sdk drives the task (docs/RSPROJ.md, "Project properties like .NET"): version fields default
    /// from Cargo.toml, and a <c>{hash}</c> file name changes with the content so cargo relinks the executable.
    /// </summary>
    public sealed class KubunoWin32ResourcesSdkTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "kubuno-win32sdk-" + Guid.NewGuid().ToString("N"));

        public KubunoWin32ResourcesSdkTests()
        {
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_dir, true);
            }
            catch (IOException)
            {
            }
        }

        private KubunoWin32Resources NewTask(string? manifest = null) => new KubunoWin32Resources
        {
            BuildEngine = new RecordingBuildEngine(),
            OutputFile = Path.Combine(_dir, "obj", "kubuno_win32_{hash}.res"),
            CargoManifestPath = manifest,
        };

        [Fact]
        public void Version_fields_default_from_the_package()
        {
            string manifest = Path.Combine(_dir, "Cargo.toml");
            File.WriteAllText(manifest, "[package]\nname = \"hello\"\nversion = \"1.4.2-beta.1\"\ndescription = \"Says hello\"\nauthors = [\"Jane Doe <jane@example.com>\", \"Kubuno\"]\n");

            KubunoWin32Resources task = NewTask(manifest);
            Assert.True(task.Execute());

            Assert.Equal("1.4.2-beta.1", task.FileVersion);
            Assert.Equal("1.4.2-beta.1", task.ProductVersion);
            Assert.Equal("hello", task.ProductName);
            Assert.Equal("Says hello", task.FileDescription);
            Assert.Equal("Jane Doe, Kubuno", task.CompanyName);
        }

        [Fact]
        public void Explicit_fields_win_over_the_package()
        {
            string manifest = Path.Combine(_dir, "Cargo.toml");
            File.WriteAllText(manifest, "[package]\nname = \"hello\"\nversion = \"1.0.0\"\n");

            KubunoWin32Resources task = NewTask(manifest);
            task.FileVersion = "2.3.4";
            task.CompanyName = "Kubuno SAS";
            Assert.True(task.Execute());

            Assert.Equal("2.3.4", task.FileVersion);
            Assert.Equal("Kubuno SAS", task.CompanyName);
            Assert.Equal("hello", task.ProductName);
        }

        [Fact]
        public void An_unreadable_manifest_only_skips_the_defaults()
        {
            string manifest = Path.Combine(_dir, "Cargo.toml");
            File.WriteAllText(manifest, "[package\nname = ");
            Assert.True(NewTask(manifest).Execute());
            Assert.True(NewTask(Path.Combine(_dir, "missing.toml")).Execute());
        }

        [Fact]
        public void The_hash_placeholder_names_the_file_after_its_content_and_drops_stale_files()
        {
            KubunoWin32Resources first = NewTask();
            first.ProductName = "One";
            Assert.True(first.Execute());
            Assert.Matches(@"kubuno_win32_[0-9a-f]{12}\.res$", first.ResourceFile);
            Assert.True(File.Exists(first.ResourceFile));

            KubunoWin32Resources same = NewTask();
            same.ProductName = "One";
            Assert.True(same.Execute());
            Assert.Equal(first.ResourceFile, same.ResourceFile);

            KubunoWin32Resources changed = NewTask();
            changed.ProductName = "Two";
            Assert.True(changed.Execute());
            Assert.NotEqual(first.ResourceFile, changed.ResourceFile);
            Assert.True(File.Exists(changed.ResourceFile));
            Assert.False(File.Exists(first.ResourceFile), "the previous content's file is deleted");
            Assert.Single(Directory.GetFiles(Path.Combine(_dir, "obj"), "kubuno_win32_*.res"));
        }
    }
}
