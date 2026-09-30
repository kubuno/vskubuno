using System.Diagnostics;
using System.Runtime.InteropServices;
using Kubuno.Rust.Cargo.Win32;

namespace Kubuno.Rust.Cargo.Tests.Win32
{
    /// <summary>
    /// End-to-end: a generated .res is passed to the real MSVC linker through
    /// <c>cargo rustc -- -C link-arg=&lt;res&gt;</c> and the resulting exe is inspected.
    /// Returns early (passes vacuously) when not on Windows or when cargo is not installed.
    /// </summary>
    public class Win32ResourceLinkTests
    {
        private const string ScratchRoot = @"C:\kubuno-build\win32res-tests";

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibraryEx(string file, IntPtr hFile, uint flags);

        [DllImport("kernel32", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr module);

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);

        private static bool HasResource(string exe, int type, int id)
        {
            IntPtr module = LoadLibraryEx(exe, IntPtr.Zero, 0x2 /* LOAD_LIBRARY_AS_DATAFILE */);
            Assert.NotEqual(IntPtr.Zero, module);
            try
            {
                return FindResource(module, (IntPtr)id, (IntPtr)type) != IntPtr.Zero;
            }
            finally
            {
                FreeLibrary(module);
            }
        }

        [Fact]
        public void Cargo_links_generated_res_into_the_exe()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            string? cargo = FindCargo();
            if (cargo == null || !Directory.Exists(@"C:\"))
            {
                return;
            }

            string dir = Path.Combine(ScratchRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(dir, "src"));
            try
            {
                File.WriteAllText(Path.Combine(dir, "Cargo.toml"), "[package]\nname = \"restest\"\nversion = \"0.1.0\"\nedition = \"2021\"\n\n[dependencies]\n");
                File.WriteAllText(Path.Combine(dir, "src", "main.rs"), "fn main() {}\n");

                var res = new Win32ResourceFile();
                res.AddIcon(1, IcoBuilder.Build());
                res.AddManifest(Win32ManifestBuilder.Build(new Win32ManifestOptions
                {
                    ExecutionLevel = Win32ExecutionLevel.RequireAdministrator,
                    LongPathAware = true,
                    CommonControlsV6 = true,
                }));
                res.AddVersionInfo(new Win32VersionInfo
                {
                    FileVersion = "1.2.3",
                    ProductVersion = "1.2.3",
                    ProductName = "Kubuno Test",
                    CompanyName = "Kubuno",
                    FileDescription = "Resource link test",
                });
                string resPath = Path.Combine(dir, "app.res");
                File.WriteAllBytes(resPath, res.ToArray());

                var psi = new ProcessStartInfo(cargo)
                {
                    WorkingDirectory = dir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                psi.Environment["CARGO_TARGET_DIR"] = Path.Combine(dir, "target");
                foreach (string a in new[] { "rustc", "--release", "--", "-C", "link-arg=" + resPath })
                {
                    psi.ArgumentList.Add(a);
                }

                using Process p = Process.Start(psi)!;
                string stderr = p.StandardError.ReadToEnd();
                string stdout = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                Assert.True(p.ExitCode == 0, "cargo rustc failed:\n" + stdout + stderr);

                string exe = Path.Combine(dir, "target", "release", "restest.exe");
                Assert.True(File.Exists(exe));

                FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(exe);
                Assert.Equal("1.2.3", fvi.FileVersion);
                Assert.Equal(1, fvi.FileMajorPart);
                Assert.Equal(2, fvi.FileMinorPart);
                Assert.Equal(3, fvi.FileBuildPart);
                Assert.Equal("Kubuno Test", fvi.ProductName);
                Assert.Equal("Kubuno", fvi.CompanyName);
                Assert.Equal("Resource link test", fvi.FileDescription);

                Assert.True(HasResource(exe, 24, 1), "RT_MANIFEST (id 1) missing");
                Assert.True(HasResource(exe, 14, 1), "RT_GROUP_ICON (id 1) missing");
                Assert.True(HasResource(exe, 3, 1), "RT_ICON (id 1) missing");
            }
            finally
            {
                try
                {
                    Directory.Delete(dir, true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private static string? FindCargo()
        {
            string exeName = "cargo.exe";
            string? home = Environment.GetEnvironmentVariable("USERPROFILE");
            var candidates = new List<string>();
            if (home != null)
            {
                candidates.Add(Path.Combine(home, ".cargo", "bin", exeName));
            }

            foreach (string d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (d.Length > 0)
                {
                    candidates.Add(Path.Combine(d, exeName));
                }
            }

            return candidates.FirstOrDefault(File.Exists);
        }
    }
}
