using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Kubuno.Web.Logic.DevCore;
using Kubuno.Web.Logic.Generation;
using Kubuno.Web.Logic.Modules;
using Kubuno.Web.Logic.Node;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Web.Tests
{
    [TestClass]
    public sealed class ModuleAndNodeTests
    {
        private string _root = string.Empty;

        [TestInitialize]
        public void CreateRoot()
        {
            _root = Path.Combine(Path.GetTempPath(), "kubuno-web-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TestCleanup]
        public void DeleteRoot()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        private const string Manifest = "[module]\nid = \"inventory\"\nversion = \"0.3.1\"\nruntime = \"rust\"\n\n[process]\nentrypoint = \"kubuno-inventory\"\n\n[server]\nport = 3150\n\n[[sidebar_items]]\nid = \"inventory\"\npath = \"/inventory\"\n";

        private string WriteModule()
        {
            var module = Path.Combine(_root, "inventory");
            Directory.CreateDirectory(Path.Combine(module, "migrations"));
            Directory.CreateDirectory(Path.Combine(module, "frontend", "dist", "chunks"));
            File.WriteAllText(Path.Combine(module, "module.toml"), Manifest);
            File.WriteAllText(Path.Combine(module, "migrations", "000001_init.up.sql"), "select 1;");
            File.WriteAllText(Path.Combine(module, "frontend", "package.json"), "{}");
            File.WriteAllText(Path.Combine(module, "frontend", "dist", "entry.js"), "export function register() {}");
            File.WriteAllText(Path.Combine(module, "frontend", "dist", "chunks", "a.js"), "1");
            File.WriteAllText(Path.Combine(module, "CHANGELOG.md"), "# Changelog");
            File.WriteAllText(Path.Combine(module, "kubuno-inventory.exe"), "MZ");
            return module;
        }

        [TestMethod]
        public void The_manifest_gives_id_entrypoint_port_and_sidebar_path()
        {
            var manifest = ModuleManifest.Parse(Manifest);
            Assert.AreEqual("inventory", manifest.Id);
            Assert.AreEqual("kubuno-inventory.exe", manifest.WindowsExecutableName);
            Assert.AreEqual(3150, manifest.Port);
            Assert.AreEqual("/inventory", manifest.SidebarPath);
            Assert.AreEqual("kubuno-x.exe", ModuleManifest.Parse("[module]\nid = \"x\"\n").WindowsExecutableName);
            Assert.ThrowsExactly<ModuleManifestException>(() => ModuleManifest.Parse("[module]\nname = \"x\"\n"));
        }

        [TestMethod]
        public void Deployment_copies_like_deploy_local_and_prefers_the_store()
        {
            var module = WriteModule();
            var layout = new DevCoreLayout(Path.Combine(_root, "dev-core"));
            layout.EnsureCreated();
            Assert.AreEqual(Path.Combine(layout.ModulesDirectory, "inventory"), layout.ModuleDirectory("inventory"));
            Directory.CreateDirectory(Path.Combine(layout.ModulesStoreDirectory, "inventory"));
            var target = layout.ModuleDirectory("inventory");
            Assert.AreEqual(Path.Combine(layout.ModulesStoreDirectory, "inventory"), target);

            var plan = ModuleDeployment.Plan(module, Path.Combine(module, "kubuno-inventory.exe"), target, ModuleManifest.Load(Path.Combine(module, "module.toml")));
            Assert.AreEqual(5, ModuleDeployment.Execute(plan));
            Assert.IsTrue(File.Exists(Path.Combine(target, "frontend", "chunks", "a.js")));
            Assert.IsTrue(File.Exists(Path.Combine(target, "migrations", "000001_init.up.sql")));
            Assert.AreEqual(0, ModuleDeployment.Execute(plan), "unchanged files are not copied again");
        }

        [TestMethod]
        public void The_kbpkg_has_the_module_folder_at_its_root_and_sha256sums()
        {
            var module = WriteModule();
            var package = KbpkgPackager.Pack(module, Path.Combine(module, "kubuno-inventory.exe"), ModuleManifest.Load(Path.Combine(module, "module.toml")), Path.Combine(module, "dist"));
            Assert.AreEqual("inventory-0.3.1-windows-x86_64.kbpkg", Path.GetFileName(package));
            using var archive = ZipFile.OpenRead(package);
            var names = archive.Entries.Select(entry => entry.FullName).ToList();
            CollectionAssert.IsSubsetOf(new[] { "kubuno-inventory.exe", "module.toml", "frontend/entry.js", "frontend/chunks/a.js", "migrations/000001_init.up.sql", "CHANGELOG.md", "SHA256SUMS" }, names);
            using var sums = new StreamReader(archive.GetEntry("SHA256SUMS")!.Open());
            var text = sums.ReadToEnd();
            StringAssert.Contains(text, KbpkgPackager.Sha256(Path.Combine(module, "module.toml")) + "  ./module.toml\n");
        }

        [TestMethod]
        public void A_backend_only_module_needs_no_dist_but_a_frontend_one_does()
        {
            var module = WriteModule();
            Directory.Delete(Path.Combine(module, "frontend", "dist"), recursive: true);
            Assert.ThrowsExactly<InvalidOperationException>(() => KbpkgPackager.PlanEntries(module, Path.Combine(module, "kubuno-inventory.exe"), ModuleManifest.Load(Path.Combine(module, "module.toml"))));
        }

        [TestMethod]
        [DataRow("@rolldown/binding-linux-x64-gnu", "@rolldown/binding-win32-x64-msvc")]
        [DataRow("lightningcss-linux-x64-musl", "lightningcss-win32-x64-msvc")]
        [DataRow("@tailwindcss/oxide-darwin-arm64", "@tailwindcss/oxide-win32-x64")]
        [DataRow("@esbuild/linux-x64", "@esbuild/win32-x64")]
        [DataRow("@rolldown/binding-win32-x64-msvc", null)]
        [DataRow("react", null)]
        public void Native_packages_of_other_systems_map_to_their_windows_build(string name, string? expected)
        {
            Assert.AreEqual(expected, NodeModulesPlatform.WindowsCounterpart(name));
        }

        [TestMethod]
        public void A_linux_tree_is_foreign_and_gets_shims_from_the_bin_fields()
        {
            var frontend = Path.Combine(_root, "frontend");
            var nodeModules = Path.Combine(frontend, "node_modules");
            Write(Path.Combine(nodeModules, "@rolldown", "binding-linux-x64-gnu", "package.json"), "{\"version\":\"1.0.3\"}");
            Write(Path.Combine(nodeModules, "lightningcss-linux-x64-gnu", "package.json"), "{\"version\":\"1.32.0\"}");
            Write(Path.Combine(nodeModules, "lightningcss-linux-x64-musl", "package.json"), "{\"version\":\"1.32.0\"}");
            Write(Path.Combine(nodeModules, "vite", "package.json"), "{\"version\":\"8.0.0\",\"bin\":{\"vite\":\"bin/vite.js\"}}");
            Write(Path.Combine(nodeModules, "typescript", "package.json"), "{\"version\":\"6.0.0\",\"bin\":{\"tsc\":\"./bin/tsc\",\"tsserver\":\"./bin/tsserver\"}}");
            Write(Path.Combine(nodeModules, "@scope", "tool", "package.json"), "{\"version\":\"1.0.0\",\"bin\":\"cli.js\"}");
            Write(Path.Combine(nodeModules, ".bin", "vite"), "#!/usr/bin/env node");
            Write(Path.Combine(nodeModules, ".bin", "tsc"), "#!/usr/bin/env node");
            Write(Path.Combine(nodeModules, ".bin", "tool"), "#!/usr/bin/env node");

            var state = NodeModulesPlatform.Inspect(frontend);
            Assert.IsTrue(state.IsForeign);
            CollectionAssert.AreEqual(new[] { "@rolldown/binding-win32-x64-msvc@1.0.3", "lightningcss-win32-x64-msvc@1.32.0" }, state.MissingWindowsPackages.Select(p => p.Name + "@" + p.Version).ToList());

            var bins = NodeModulesPlatform.BinCommands(nodeModules);
            CollectionAssert.AreEquivalent(new[] { "vite", "tsc", "tool" }, bins.Keys.ToList(), "tsserver is not linked in .bin");
            Assert.AreEqual(Path.Combine(nodeModules, "typescript", "bin", "tsc"), bins["tsc"]);
            StringAssert.Contains(NodeModulesPlatform.OverlayPackageJson(state.MissingWindowsPackages), "\"optionalDependencies\"");
            Assert.AreEqual(NodeModulesPlatform.OverlayDirectory(_root, state.MissingWindowsPackages), NodeModulesPlatform.OverlayDirectory(_root, state.MissingWindowsPackages.Reverse()));

            Write(Path.Combine(nodeModules, "@rolldown", "binding-win32-x64-msvc", "package.json"), "{\"version\":\"1.0.3\"}");
            Write(Path.Combine(nodeModules, "lightningcss-win32-x64-msvc", "package.json"), "{\"version\":\"1.32.0\"}");
            Assert.IsFalse(NodeModulesPlatform.Inspect(frontend).IsForeign, "a tree with the Windows packages is not foreign");
        }

        [TestMethod]
        public void Template_tokens_make_valid_ids_and_read_the_core_versions()
        {
            Assert.AreEqual("inventory", ModuleTemplateTokens.ModuleId("Inventory"));
            Assert.AreEqual("mynotes2", ModuleTemplateTokens.ModuleId("My Notes 2"));
            Assert.AreEqual("m3d", ModuleTemplateTokens.ModuleId("3D"));
            Assert.AreEqual("module", ModuleTemplateTokens.ModuleId("--"));

            var core = Path.Combine(_root, "core");
            Write(Path.Combine(core, "frontend", "packages", "sdk", "package.json"), "{\n  \"name\": \"@kubuno/sdk\",\n  \"version\": \"0.2.0\"\n}");
            var tokens = ModuleTemplateTokens.Build("Inventory", ModuleTemplateTokens.FindCoreRepository(Path.Combine(_root, "inventory")));
            Assert.AreEqual("kubuno-inventory", tokens["$cratename$"]);
            Assert.AreEqual("0.2.0", tokens["$kubunosdkversion$"]);
            Assert.AreEqual(ModuleTemplateTokens.UiVersion, tokens["$kubunouiversion$"]);
        }

        private static void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
    }
}
