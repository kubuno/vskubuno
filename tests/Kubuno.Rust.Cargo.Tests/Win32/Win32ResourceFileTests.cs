using System.Text;
using System.Xml.Linq;
using Kubuno.Rust.Cargo.Win32;

namespace Kubuno.Rust.Cargo.Tests.Win32
{
    public class Win32ResourceFileTests
    {
        [Fact]
        public void Empty_file_is_only_the_32_byte_sentinel()
        {
            byte[] bytes = new Win32ResourceFile().ToArray();
            Assert.Equal(32, bytes.Length);
            Assert.Equal(0u, BitConverter.ToUInt32(bytes, 0));
            Assert.Equal(32u, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal(0xFFFF, BitConverter.ToUInt16(bytes, 8));
            Assert.Equal(0xFFFF, BitConverter.ToUInt16(bytes, 12));
        }

        [Fact]
        public void AddIcon_emits_one_icon_per_image_and_a_group()
        {
            byte[] ico = IcoBuilder.Build();
            var res = new Win32ResourceFile();
            res.AddIcon(1, ico);
            List<ResEntry> entries = ResReader.Read(res.ToArray());

            Assert.Equal(4, entries.Count); // sentinel + 2 icons + group
            Assert.Equal(0, entries[0].Type);
            Assert.Equal(3, entries[1].Type);
            Assert.Equal(1, entries[1].Id);
            Assert.Equal(3, entries[2].Type);
            Assert.Equal(2, entries[2].Id);
            Assert.Equal(IcoBuilder.Bmp16(), entries[1].Data);
            Assert.Equal(IcoBuilder.Png256(), entries[2].Data);
            Assert.All(entries.Skip(1), e => Assert.Equal(0x0409, e.Language));

            ResEntry group = entries[3];
            Assert.Equal(14, group.Type);
            Assert.Equal(1, group.Id);
            Assert.Equal(0x1030, group.MemoryFlags);
            Assert.Equal(6 + 2 * 14, group.Data.Length);
            Assert.Equal(0, BitConverter.ToUInt16(group.Data, 0));
            Assert.Equal(1, BitConverter.ToUInt16(group.Data, 2));
            Assert.Equal(2, BitConverter.ToUInt16(group.Data, 4));
            for (int i = 0; i < 2; i++)
            {
                int g = 6 + i * 14;
                int s = 6 + i * 16;
                Assert.Equal(ico.AsSpan(s, 12).ToArray(), group.Data.AsSpan(g, 12).ToArray());
                Assert.Equal(i + 1, BitConverter.ToUInt16(group.Data, g + 12));
            }

            Assert.Equal(16, group.Data[6]);
            Assert.Equal(0, group.Data[6 + 14]); // 256 encoded as 0
        }

        [Fact]
        public void Icon_ids_stay_unique_across_two_groups()
        {
            var res = new Win32ResourceFile();
            res.AddIcon(1, IcoBuilder.Build());
            res.AddIcon(2, IcoBuilder.Build());
            List<ResEntry> icons = ResReader.Read(res.ToArray()).Where(e => e.Type == 3).ToList();
            Assert.Equal(new ushort[] { 1, 2, 3, 4 }, icons.Select(i => i.Id).ToArray());
        }

        [Fact]
        public void AddIcon_rejects_non_ico_data()
        {
            var res = new Win32ResourceFile();
            Assert.Throws<InvalidDataException>(() => res.AddIcon(1, new byte[] { 1, 2, 3 }));
            Assert.Throws<InvalidDataException>(() => res.AddIcon(1, Encoding.ASCII.GetBytes("this is not an icon file")));
            byte[] truncated = IcoBuilder.Build().Take(50).ToArray();
            Assert.Throws<InvalidDataException>(() => res.AddIcon(1, truncated));
        }

        [Fact]
        public void AddManifest_stores_utf8_without_bom()
        {
            var res = new Win32ResourceFile();
            res.AddManifest("<a>é</a>");
            ResEntry e = ResReader.Read(res.ToArray()).Single(x => x.Type == 24);
            Assert.Equal(1, e.Id);
            Assert.Equal(0x0030, e.MemoryFlags);
            Assert.Equal(new UTF8Encoding(false).GetBytes("<a>é</a>"), e.Data);
        }

        [Fact]
        public void AddVersionInfo_round_trips()
        {
            var res = new Win32ResourceFile();
            res.AddVersionInfo(new Win32VersionInfo
            {
                FileVersion = "1.2.3",
                ProductVersion = "4.5.6-beta+x",
                CompanyName = "Kubuno",
                ProductName = "Kubuno Test",
                FileDescription = "A test",
                LegalCopyright = "(c) Kubuno",
                OriginalFilename = "t.exe",
            });
            ResEntry e = ResReader.Read(res.ToArray()).Single(x => x.Type == 16);
            Assert.Equal(1, e.Id);

            VersionNode root = VersionNode.Parse(e.Data, 0, out int end);
            Assert.Equal("VS_VERSION_INFO", root.Key);
            Assert.Equal(e.Data.Length, root.Length);
            Assert.Equal(e.Data.Length, end);
            Assert.Equal(52, root.ValueLength);
            Assert.Equal(0u, BitConverter.ToUInt32(new byte[4], 0));
            Assert.Equal(0xFEEF04BDu, BitConverter.ToUInt32(root.Value, 0));
            Assert.Equal(0x00010000u, BitConverter.ToUInt32(root.Value, 4));
            Assert.Equal(0x00010002u, BitConverter.ToUInt32(root.Value, 8));
            Assert.Equal(0x00030000u, BitConverter.ToUInt32(root.Value, 12));
            Assert.Equal(0x00040005u, BitConverter.ToUInt32(root.Value, 16));
            Assert.Equal(0x00060000u, BitConverter.ToUInt32(root.Value, 20));
            Assert.Equal(0x3Fu, BitConverter.ToUInt32(root.Value, 24));
            Assert.Equal(0u, BitConverter.ToUInt32(root.Value, 28));
            Assert.Equal(0x40004u, BitConverter.ToUInt32(root.Value, 32));
            Assert.Equal(1u, BitConverter.ToUInt32(root.Value, 36));

            VersionNode table = root.Find("StringFileInfo").Find("040904B0");
            var strings = table.Children.ToDictionary(c => c.Key, c => c.ValueText);
            Assert.Equal("Kubuno", strings["CompanyName"]);
            Assert.Equal("Kubuno Test", strings["ProductName"]);
            Assert.Equal("1.2.3", strings["FileVersion"]);
            Assert.Equal("4.5.6-beta+x", strings["ProductVersion"]);
            Assert.Equal("t.exe", strings["OriginalFilename"]);
            Assert.False(strings.ContainsKey("Comments")); // empty strings are omitted

            VersionNode translation = root.Find("VarFileInfo").Find("Translation");
            Assert.Equal(new byte[] { 0x09, 0x04, 0xB0, 0x04 }, translation.Value);
        }

        [Fact]
        public void AddVersionInfo_marks_dll()
        {
            var res = new Win32ResourceFile();
            res.AddVersionInfo(new Win32VersionInfo { IsDll = true });
            ResEntry e = ResReader.Read(res.ToArray()).Single(x => x.Type == 16);
            VersionNode root = VersionNode.Parse(e.Data, 0, out _);
            Assert.Equal(2u, BitConverter.ToUInt32(root.Value, 36));
        }

        [Fact]
        public void Every_entry_is_dword_aligned_with_odd_sized_data()
        {
            var res = new Win32ResourceFile();
            res.AddManifest("abc");
            res.AddManifest("abcde", 2);
            res.AddVersionInfo(new Win32VersionInfo { ProductName = "x" });
            List<ResEntry> entries = ResReader.Read(res.ToArray()); // asserts alignment and exact length
            Assert.Equal(4, entries.Count);
        }

        [Theory]
        [InlineData("1.2.3", "1.2.3.0")]
        [InlineData("1.2.3.4", "1.2.3.4")]
        [InlineData("1.2.3-beta.1+abc", "1.2.3.0")]
        [InlineData("1.2.3+abc", "1.2.3.0")]
        [InlineData("1.2", "1.2.0.0")]
        [InlineData("", "0.0.0.0")]
        [InlineData(null, "0.0.0.0")]
        [InlineData("abc", "0.0.0.0")]
        [InlineData("70000.1.99999999999.4", "65535.1.65535.4")]
        public void ParseVersion(string? input, string expected)
        {
            Assert.Equal(expected, Win32VersionInfo.ParseVersion(input).ToString());
        }

        [Fact]
        public void Manifest_default_is_well_formed_with_expected_content()
        {
            string xml = Win32ManifestBuilder.Build(new Win32ManifestOptions());
            XDocument doc = XDocument.Parse(xml);
            XNamespace v1 = "urn:schemas-microsoft-com:asm.v1";
            XNamespace v3 = "urn:schemas-microsoft-com:asm.v3";
            XNamespace compat = "urn:schemas-microsoft-com:compatibility.v1";
            Assert.Equal(v1 + "assembly", doc.Root!.Name);
            Assert.Null(doc.Root.Element(v1 + "assemblyIdentity"));
            Assert.Null(doc.Root.Element(v1 + "dependency"));

            XElement level = doc.Descendants(v3 + "requestedExecutionLevel").Single();
            Assert.Equal("asInvoker", (string?)level.Attribute("level"));
            Assert.Equal("false", (string?)level.Attribute("uiAccess"));

            string[] ids = doc.Descendants(compat + "supportedOS").Select(e => (string)e.Attribute("Id")!).ToArray();
            Assert.Equal(new[] { "{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" }, ids);

            XNamespace s2005 = "http://schemas.microsoft.com/SMI/2005/WindowsSettings";
            XNamespace s2016 = "http://schemas.microsoft.com/SMI/2016/WindowsSettings";
            Assert.Equal("true/pm", doc.Descendants(s2005 + "dpiAware").Single().Value);
            Assert.Equal("PerMonitorV2, PerMonitor", doc.Descendants(s2016 + "dpiAwareness").Single().Value);
            Assert.Empty(doc.Descendants(s2016 + "longPathAware"));
        }

        [Fact]
        public void Manifest_all_options()
        {
            string xml = Win32ManifestBuilder.Build(new Win32ManifestOptions
            {
                AssemblyName = "Kubuno.<App>",
                AssemblyVersion = "1.2.3.4",
                ExecutionLevel = Win32ExecutionLevel.RequireAdministrator,
                MinimumWindows = Win32MinimumWindows.Windows7,
                DpiAwareness = Win32DpiAwareness.PerMonitor,
                LongPathAware = true,
                UseUtf8CodePage = true,
                CommonControlsV6 = true,
            });
            XDocument doc = XDocument.Parse(xml);
            XNamespace v1 = "urn:schemas-microsoft-com:asm.v1";
            XNamespace v3 = "urn:schemas-microsoft-com:asm.v3";
            XNamespace compat = "urn:schemas-microsoft-com:compatibility.v1";
            XElement identity = doc.Root!.Element(v1 + "assemblyIdentity")!;
            Assert.Equal("Kubuno.<App>", (string?)identity.Attribute("name"));
            Assert.Equal("amd64", (string?)identity.Attribute("processorArchitecture"));
            Assert.Equal("requireAdministrator", (string?)doc.Descendants(v3 + "requestedExecutionLevel").Single().Attribute("level"));
            Assert.Equal(4, doc.Descendants(compat + "supportedOS").Count());

            XElement cc = doc.Root.Element(v1 + "dependency")!.Element(v1 + "dependentAssembly")!.Element(v1 + "assemblyIdentity")!;
            Assert.Equal("Microsoft.Windows.Common-Controls", (string?)cc.Attribute("name"));
            Assert.Equal("6.0.0.0", (string?)cc.Attribute("version"));
            Assert.Equal("6595b64144ccf1df", (string?)cc.Attribute("publicKeyToken"));
            Assert.Equal("*", (string?)cc.Attribute("processorArchitecture"));
            Assert.Equal("*", (string?)cc.Attribute("language"));

            XNamespace s2016 = "http://schemas.microsoft.com/SMI/2016/WindowsSettings";
            XNamespace s2019 = "http://schemas.microsoft.com/SMI/2019/WindowsSettings";
            Assert.Equal("PerMonitor", doc.Descendants(s2016 + "dpiAwareness").Single().Value);
            Assert.Equal("true", doc.Descendants(s2016 + "longPathAware").Single().Value);
            Assert.Equal("UTF-8", doc.Descendants(s2019 + "activeCodePage").Single().Value);
        }

        [Theory]
        [InlineData(Win32MinimumWindows.Windows10, 1)]
        [InlineData(Win32MinimumWindows.Windows81, 2)]
        [InlineData(Win32MinimumWindows.Windows8, 3)]
        [InlineData(Win32MinimumWindows.Windows7, 4)]
        public void Manifest_supported_os_count(Win32MinimumWindows min, int count)
        {
            string xml = Win32ManifestBuilder.Build(new Win32ManifestOptions { MinimumWindows = min });
            Assert.Equal(count, XDocument.Parse(xml).Descendants("{urn:schemas-microsoft-com:compatibility.v1}supportedOS").Count());
        }

        [Theory]
        [InlineData(Win32DpiAwareness.Unaware, "false", false)]
        [InlineData(Win32DpiAwareness.System, "true", false)]
        public void Manifest_legacy_dpi(Win32DpiAwareness dpi, string aware, bool hasAwareness)
        {
            XDocument doc = XDocument.Parse(Win32ManifestBuilder.Build(new Win32ManifestOptions { DpiAwareness = dpi }));
            Assert.Equal(aware, doc.Descendants("{http://schemas.microsoft.com/SMI/2005/WindowsSettings}dpiAware").Single().Value);
            Assert.Equal(hasAwareness, doc.Descendants("{http://schemas.microsoft.com/SMI/2016/WindowsSettings}dpiAwareness").Any());
        }
    }
}
