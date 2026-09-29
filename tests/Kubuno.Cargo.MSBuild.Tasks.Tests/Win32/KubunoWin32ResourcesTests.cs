using System.Text;
using System.Xml.Linq;
using Kubuno.Cargo.MSBuild.Tasks.Tests.Fakes;
using Microsoft.Build.Framework;

namespace Kubuno.Cargo.MSBuild.Tasks.Tests.Win32
{
    public sealed class KubunoWin32ResourcesTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "kubuno-win32res-" + Guid.NewGuid().ToString("N"));
        private readonly RecordingBuildEngine _engine = new();

        public KubunoWin32ResourcesTests()
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

        private KubunoWin32Resources NewTask(Action<KubunoWin32Resources>? configure = null)
        {
            var t = new KubunoWin32Resources
            {
                BuildEngine = _engine,
                OutputFile = Path.Combine(_dir, "obj", "app.res"),
            };
            configure?.Invoke(t);
            return t;
        }

        private string WriteIco()
        {
            string path = Path.Combine(_dir, "app.ico");
            File.WriteAllBytes(path, BuildIco());
            return path;
        }

        private static byte[] BuildIco()
        {
            var bmp = new MemoryStream();
            var w = new BinaryWriter(bmp);
            w.Write(40); w.Write(16); w.Write(32); w.Write((ushort)1); w.Write((ushort)32);
            w.Write(0); w.Write(1024); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            w.Write(new byte[1024 + 64]);
            byte[] image = bmp.ToArray();

            var ms = new MemoryStream();
            var o = new BinaryWriter(ms);
            o.Write((ushort)0); o.Write((ushort)1); o.Write((ushort)1);
            o.Write((byte)16); o.Write((byte)16); o.Write((byte)0); o.Write((byte)0); o.Write((ushort)1); o.Write((ushort)32);
            o.Write(image.Length); o.Write(22);
            o.Write(image);
            return ms.ToArray();
        }

        /// <summary>Returns type -> list of (id, data).</summary>
        private static List<(ushort Type, ushort Id, ushort Lang, byte[] Data)> ReadRes(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            var list = new List<(ushort, ushort, ushort, byte[])>();
            int p = 0;
            while (p < b.Length)
            {
                Assert.Equal(0, p % 4);
                uint ds = BitConverter.ToUInt32(b, p);
                uint hs = BitConverter.ToUInt32(b, p + 4);
                ushort type = BitConverter.ToUInt16(b, p + 10);
                ushort id = BitConverter.ToUInt16(b, p + 14);
                ushort lang = BitConverter.ToUInt16(b, p + 22);
                if (ds > 0)
                {
                    list.Add((type, id, lang, b.AsSpan(p + (int)hs, (int)ds).ToArray()));
                }

                p = (int)((p + hs + ds + 3) & ~3u);
            }

            Assert.Equal(b.Length, p);
            return list;
        }

        private static string ManifestOf(string res) =>
            Encoding.UTF8.GetString(ReadRes(res).Single(e => e.Type == 24).Data);

        private static string VersionString(string res, string key)
        {
            byte[] data = ReadRes(res).Single(e => e.Type == 16).Data;
            string text = Encoding.Unicode.GetString(data);
            int i = text.IndexOf(key + "\0", StringComparison.Ordinal);
            Assert.True(i >= 0, key + " not found");
            int valueStart = i + key.Length + 1;
            if (valueStart % 2 == 1)
            {
                valueStart++;
            }

            int end = text.IndexOf('\0', valueStart);
            return text.Substring(valueStart, end - valueStart);
        }

        [Fact]
        public void Default_run_writes_icon_manifest_and_version()
        {
            string ico = WriteIco();
            KubunoWin32Resources task = NewTask(t =>
            {
                t.IconFile = ico;
                t.FileVersion = "1.2.3";
                t.ProductName = "Kubuno Test";
                t.CompanyName = "Kubuno";
                t.Copyright = "(c) Kubuno";
            });

            Assert.True(task.Execute());
            Assert.Empty(_engine.Errors);
            Assert.Equal(task.OutputFile, task.ResourceFile);
            Assert.True(File.Exists(task.ResourceFile));

            var entries = ReadRes(task.ResourceFile);
            Assert.Equal(new ushort[] { 3, 14, 16, 24 }, entries.Select(e => e.Type).OrderBy(x => x).ToArray());
            Assert.All(entries, e => Assert.Equal(0x0409, e.Lang));

            XDocument doc = XDocument.Parse(ManifestOf(task.ResourceFile));
            Assert.Equal("asInvoker", (string?)doc.Descendants("{urn:schemas-microsoft-com:asm.v3}requestedExecutionLevel").Single().Attribute("level"));
            Assert.Equal("Kubuno Test", VersionString(task.ResourceFile, "ProductName"));
            Assert.Equal("Kubuno", VersionString(task.ResourceFile, "CompanyName"));
            Assert.Equal("(c) Kubuno", VersionString(task.ResourceFile, "LegalCopyright"));
            // FileVersion given, ProductVersion falls back to it.
            Assert.Equal("1.2.3", VersionString(task.ResourceFile, "ProductVersion"));
            Assert.Contains(_engine.Messages, m => m.Message!.Contains("written"));
        }

        [Fact]
        public void Options_flow_into_the_manifest()
        {
            KubunoWin32Resources task = NewTask(t =>
            {
                t.ExecutionLevel = "requireAdministrator";
                t.DpiAwareness = "system";
                t.MinimumWindowsVersion = "7";
                t.LongPathAware = true;
                t.UseUtf8CodePage = true;
                t.CommonControlsV6 = true;
            });

            Assert.True(task.Execute());
            XDocument doc = XDocument.Parse(ManifestOf(task.ResourceFile));
            Assert.Equal("requireAdministrator", (string?)doc.Descendants("{urn:schemas-microsoft-com:asm.v3}requestedExecutionLevel").Single().Attribute("level"));
            Assert.Equal(4, doc.Descendants("{urn:schemas-microsoft-com:compatibility.v1}supportedOS").Count());
            Assert.Equal("true", doc.Descendants("{http://schemas.microsoft.com/SMI/2005/WindowsSettings}dpiAware").Single().Value);
            Assert.Equal("true", doc.Descendants("{http://schemas.microsoft.com/SMI/2016/WindowsSettings}longPathAware").Single().Value);
            Assert.Single(doc.Descendants("{http://schemas.microsoft.com/SMI/2019/WindowsSettings}activeCodePage"));
            Assert.Contains("Common-Controls", doc.ToString());
        }

        [Fact]
        public void Manifest_mode_none_omits_manifest()
        {
            KubunoWin32Resources task = NewTask(t => t.ManifestMode = "none");
            Assert.True(task.Execute());
            Assert.DoesNotContain(ReadRes(task.ResourceFile), e => e.Type == 24);
        }

        [Fact]
        public void Manifest_mode_custom_embeds_file_verbatim()
        {
            string manifest = Path.Combine(_dir, "my.manifest");
            const string xml = "<?xml version=\"1.0\"?><assembly xmlns=\"urn:schemas-microsoft-com:asm.v1\" manifestVersion=\"1.0\"/>";
            File.WriteAllText(manifest, xml);
            KubunoWin32Resources task = NewTask(t =>
            {
                t.ManifestMode = "Custom";
                t.CustomManifestFile = manifest;
            });
            Assert.True(task.Execute());
            Assert.Equal(xml, ManifestOf(task.ResourceFile));
        }

        [Fact]
        public void Version_falls_back_to_the_other_field()
        {
            KubunoWin32Resources task = NewTask(t => t.ProductVersion = "2.0.1");
            Assert.True(task.Execute());
            Assert.Equal("2.0.1", VersionString(task.ResourceFile, "FileVersion"));
        }

        [Fact]
        public void Unchanged_output_is_not_rewritten()
        {
            KubunoWin32Resources first = NewTask(t => t.FileVersion = "1.0.0");
            Assert.True(first.Execute());
            var old = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(first.OutputFile, old);

            KubunoWin32Resources second = NewTask(t => t.FileVersion = "1.0.0");
            Assert.True(second.Execute());
            Assert.Equal(old, File.GetLastWriteTimeUtc(second.OutputFile));

            KubunoWin32Resources third = NewTask(t => t.FileVersion = "1.0.1");
            Assert.True(third.Execute());
            Assert.NotEqual(old, File.GetLastWriteTimeUtc(third.OutputFile));
        }

        [Fact]
        public void Relative_icon_resolves_against_the_project_directory()
        {
            // RecordingBuildEngine reports "test.rsproj", i.e. the current directory.
            string name = "kubuno-rel-" + Guid.NewGuid().ToString("N") + ".ico";
            string full = Path.Combine(Directory.GetCurrentDirectory(), name);
            File.WriteAllBytes(full, BuildIco());
            try
            {
                KubunoWin32Resources task = NewTask(t => t.IconFile = name);
                Assert.True(task.Execute());
                Assert.Contains(ReadRes(task.ResourceFile), e => e.Type == 14);
            }
            finally
            {
                File.Delete(full);
            }
        }

        [Theory]
        [InlineData("IconMissing", "KUBUNO0101")]
        [InlineData("IconBad", "KUBUNO0102")]
        [InlineData("ManifestMode", "KUBUNO0103")]
        [InlineData("CustomUnset", "KUBUNO0104")]
        [InlineData("CustomMissing", "KUBUNO0104")]
        [InlineData("Dpi", "KUBUNO0105")]
        [InlineData("Level", "KUBUNO0106")]
        [InlineData("MinWin", "KUBUNO0107")]
        [InlineData("Output", "KUBUNO0108")]
        [InlineData("CustomBadXml", "KUBUNO0109")]
        public void Errors_carry_their_code(string scenario, string code)
        {
            string bad = Path.Combine(_dir, "bad.bin");
            File.WriteAllText(bad, "not an icon and not xml");
            KubunoWin32Resources task = NewTask(t =>
            {
                switch (scenario)
                {
                    case "IconMissing": t.IconFile = Path.Combine(_dir, "nope.ico"); break;
                    case "IconBad": t.IconFile = bad; break;
                    case "ManifestMode": t.ManifestMode = "Weird"; break;
                    case "CustomUnset": t.ManifestMode = "Custom"; break;
                    case "CustomMissing": t.ManifestMode = "Custom"; t.CustomManifestFile = Path.Combine(_dir, "nope.manifest"); break;
                    case "Dpi": t.DpiAwareness = "huge"; break;
                    case "Level": t.ExecutionLevel = "root"; break;
                    case "MinWin": t.MinimumWindowsVersion = "95"; break;
                    case "Output": t.OutputFile = bad + Path.DirectorySeparatorChar + "x.res"; break;
                    case "CustomBadXml": t.ManifestMode = "Custom"; t.CustomManifestFile = bad; break;
                }
            });

            Assert.False(task.Execute());
            BuildErrorEventArgs error = Assert.Single(_engine.Errors);
            Assert.Equal(code, error.Code);
            Assert.False(string.IsNullOrWhiteSpace(error.Message));
        }
    }
}
