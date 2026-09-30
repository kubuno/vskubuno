using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Architecture.Tests
{
    /// <summary>
    /// docs/ARCHITECTURE.md, "Layers (as built)": the extension is split into layers whose dependencies only go down -
    /// Core &lt;- Rust &lt;- Desktop / Web / Mobile - so a Kubuno target (desktop apps today, web modules and mobile apps
    /// next) can be added without touching the layers below it, and the targets never depend on each other. Checked on
    /// the project files and on the compiled assemblies.
    /// </summary>
    [TestClass]
    public sealed class LayeringTests
    {
        private enum Layer
        {
            Core,
            Rust,
            Desktop,
            Web,
            Mobile,
            Packaging,
        }

        /// <summary>What each layer may reference (itself included).</summary>
        private static readonly Dictionary<Layer, Layer[]> Allowed = new()
        {
            [Layer.Core] = new[] { Layer.Core },
            [Layer.Rust] = new[] { Layer.Core, Layer.Rust },
            [Layer.Desktop] = new[] { Layer.Core, Layer.Rust, Layer.Desktop },
            [Layer.Web] = new[] { Layer.Core, Layer.Rust, Layer.Web },
            [Layer.Mobile] = new[] { Layer.Core, Layer.Rust, Layer.Mobile },
            [Layer.Packaging] = new[] { Layer.Core, Layer.Rust, Layer.Desktop, Layer.Web, Layer.Mobile, Layer.Packaging },
        };

        /// <summary>The product layers, each with the one assembly that registers its <c>KubunoLayer</c>.</summary>
        private static readonly (Layer Layer, string Assembly, string LayerClass)[] ProductLayers =
        {
            (Layer.Rust, "Kubuno.Rust", "Kubuno.Rust.RustLayer"),
            (Layer.Desktop, "Kubuno.Desktop", "Kubuno.Desktop.DesktopLayer"),
            (Layer.Web, "Kubuno.Web", "Kubuno.Web.WebLayer"),
            (Layer.Mobile, "Kubuno.Mobile", "Kubuno.Mobile.MobileLayer"),
        };

        /// <summary>The layer of a Kubuno assembly (or project), null for anything else (the framework, Visual Studio, NuGet packages).</summary>
        private static Layer? LayerOf(string assemblyName)
        {
            if (assemblyName == "Kubuno.VisualStudio")
            {
                return Layer.Packaging;
            }

            if (assemblyName == "kubuno-vs-mcp")
            {
                return Layer.Core; // Kubuno.Core.Mcp's executable name.
            }

            (string Prefix, Layer Layer)[] prefixes =
            {
                ("Kubuno.Core", Layer.Core),
                ("Kubuno.Rust", Layer.Rust),
                ("Kubuno.Cargo.MSBuild.Tasks", Layer.Rust), // Kubuno.Rust.Sdk's task assembly: its name is part of the SDK.
                ("Kubuno.Desktop", Layer.Desktop),
                ("Kubuno.Web", Layer.Web),
                ("Kubuno.Mobile", Layer.Mobile),
            };
            foreach (var (prefix, layer) in prefixes)
            {
                if (assemblyName == prefix || assemblyName.StartsWith(prefix + ".", StringComparison.Ordinal))
                {
                    return layer;
                }
            }

            return null;
        }

        private static string RepositoryRoot()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Kubuno.VisualStudio.sln")))
            {
                dir = dir.Parent;
            }

            Assert.IsNotNull(dir, "Kubuno.VisualStudio.sln not found above the test assembly.");
            return dir!.FullName;
        }

        /// <summary>The build configuration this test assembly was built in (bin\&lt;Configuration&gt;\net48\).</summary>
        private static string Configuration() =>
            new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\')).Parent!.Name;

        /// <summary>Every product project of src/ (the packaging project included), with its assembly name.</summary>
        private static IReadOnlyList<(string Path, string Name, string AssemblyName)> SourceProjects()
        {
            return Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "src"), "*.csproj", SearchOption.AllDirectories)
                .Where(p => !p.Contains(@"\obj\") && !p.Contains(@"\bin\"))
                .Select(p =>
                {
                    var name = Path.GetFileNameWithoutExtension(p);
                    var assemblyName = XDocument.Load(p).Descendants().FirstOrDefault(e => e.Name.LocalName == "AssemblyName")?.Value ?? name;
                    return (p, name, assemblyName);
                })
                .OrderBy(p => p.name, StringComparer.Ordinal)
                .ToList();
        }

        private static IEnumerable<string> ProjectReferences(string projectPath) =>
            XDocument.Load(projectPath).Descendants()
                .Where(e => e.Name.LocalName == "ProjectReference")
                .Select(e => Path.GetFileNameWithoutExtension((string)e.Attribute("Include")!));

        /// <summary>The built assembly of a project (any target framework), or null when it has not been built.</summary>
        private static string? BuiltAssembly(string projectPath, string assemblyName)
        {
            var bin = Path.Combine(Path.GetDirectoryName(projectPath)!, "bin", Configuration());
            if (!Directory.Exists(bin))
            {
                return null;
            }

            return Directory.EnumerateFiles(bin, assemblyName + ".dll", SearchOption.AllDirectories)
                .Where(f => !f.Contains(@"\ref\"))
                .OrderBy(f => f.Contains("net48") || f.Contains("net472") || f.Contains("netstandard") ? 0 : 1)
                .ThenBy(f => f, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        private static IReadOnlyList<string> AssemblyReferences(string assemblyPath)
        {
            using var stream = File.OpenRead(assemblyPath);
            using var pe = new PEReader(stream);
            var reader = pe.GetMetadataReader();
            return reader.AssemblyReferences.Select(h => reader.GetString(reader.GetAssemblyReference(h).Name)).ToList();
        }

        [TestMethod]
        public void Every_layer_has_its_projects()
        {
            var projects = SourceProjects().Select(p => p.Name).ToList();
            foreach (var expected in new[]
            {
                "Kubuno.Core", "Kubuno.Core.Logic", "Kubuno.Core.Mcp", "Kubuno.Core.Mcp.Bridge",
                "Kubuno.Rust", "Kubuno.Rust.Logic", "Kubuno.Rust.Cargo", "Kubuno.Rust.Launch", "Kubuno.Rust.ProjectSystem",
                "Kubuno.Rust.TestAdapter", "Kubuno.Rust.Debugger", "Kubuno.Rust.TemplateWizard", "Kubuno.Cargo.MSBuild.Tasks",
                "Kubuno.Desktop", "Kubuno.Desktop.Logic", "Kubuno.Desktop.ProjectSystem", "Kubuno.Desktop.TemplateWizard",
                "Kubuno.Web", "Kubuno.Mobile", "Kubuno.VisualStudio",
            })
            {
                CollectionAssert.Contains(projects, expected, expected + " is missing from src/.");
            }

            foreach (var project in SourceProjects())
            {
                Assert.IsNotNull(LayerOf(project.Name), project.Name + " belongs to no layer: name it Kubuno.<Layer>[.Part].");
            }
        }

        [TestMethod]
        public void Project_references_only_go_down_the_layers()
        {
            var violations = new List<string>();
            foreach (var project in SourceProjects())
            {
                var from = LayerOf(project.Name)!.Value;
                foreach (var reference in ProjectReferences(project.Path))
                {
                    if (LayerOf(reference) is { } to && !Allowed[from].Contains(to))
                    {
                        violations.Add($"{project.Name} ({from}) references {reference} ({to})");
                    }
                }
            }

            Assert.AreEqual(0, violations.Count, "Layer violations in the project files:\n" + string.Join("\n", violations));
        }

        [TestMethod]
        public void Compiled_assemblies_only_reference_down_the_layers()
        {
            var violations = new List<string>();
            var missing = new List<string>();
            foreach (var project in SourceProjects())
            {
                var assembly = BuiltAssembly(project.Path, project.AssemblyName);
                if (assembly is null)
                {
                    missing.Add(project.Name);
                    continue;
                }

                var from = LayerOf(project.Name)!.Value;
                foreach (var reference in AssemblyReferences(assembly))
                {
                    if (LayerOf(reference) is { } to && !Allowed[from].Contains(to))
                    {
                        violations.Add($"{project.AssemblyName} ({from}) references {reference} ({to})");
                    }
                }
            }

            Assert.AreEqual(0, missing.Count, $"Build the solution ({Configuration()}) first; not built: " + string.Join(", ", missing));
            Assert.AreEqual(0, violations.Count, "Layer violations in the compiled assemblies:\n" + string.Join("\n", violations));
        }

        [TestMethod]
        public void Pure_logic_assemblies_do_not_reference_the_Visual_Studio_SDK()
        {
            var pure = SourceProjects().Where(p => p.Name.EndsWith(".Logic", StringComparison.Ordinal) || p.Name is "Kubuno.Rust.Cargo" or "Kubuno.Rust.Launch").ToList();
            Assert.AreEqual(5, pure.Count, "Kubuno.Core.Logic, Kubuno.Rust.Logic, Kubuno.Rust.Cargo, Kubuno.Rust.Launch, Kubuno.Desktop.Logic");
            foreach (var project in pure)
            {
                var assembly = BuiltAssembly(project.Path, project.AssemblyName);
                Assert.IsNotNull(assembly, project.Name + " is not built.");
                var vs = AssemblyReferences(assembly!).Where(r => r.StartsWith("Microsoft.VisualStudio", StringComparison.Ordinal) || r.StartsWith("EnvDTE", StringComparison.Ordinal)).ToList();
                Assert.AreEqual(0, vs.Count, $"{project.Name} must stay testable without Visual Studio, but references {string.Join(", ", vs)}.");
            }
        }

        [TestMethod]
        public void Each_product_layer_registers_exactly_one_KubunoLayer()
        {
            var projects = SourceProjects().ToDictionary(p => p.Name);
            foreach (var (_, assemblyName, layerClass) in ProductLayers)
            {
                var assembly = BuiltAssembly(projects[assemblyName].Path, assemblyName);
                Assert.IsNotNull(assembly, assemblyName + " is not built.");

                using var stream = File.OpenRead(assembly!);
                using var pe = new PEReader(stream);
                var reader = pe.GetMetadataReader();
                var layers = new List<string>();
                foreach (var handle in reader.TypeDefinitions)
                {
                    var type = reader.GetTypeDefinition(handle);
                    if (type.BaseType.IsNil || type.BaseType.Kind != HandleKind.TypeReference)
                    {
                        continue;
                    }

                    var baseType = reader.GetTypeReference((TypeReferenceHandle)type.BaseType);
                    if (reader.GetString(baseType.Namespace) == "Kubuno.Core.Extensibility" && reader.GetString(baseType.Name) == "KubunoLayer")
                    {
                        layers.Add(reader.GetString(type.Namespace) + "." + reader.GetString(type.Name));
                    }
                }

                CollectionAssert.AreEqual(new[] { layerClass }, layers, assemblyName + " must declare exactly one KubunoLayer, " + layerClass + ".");
            }
        }

        [TestMethod]
        public void The_packaging_project_ships_every_layer_and_lists_every_layer_class()
        {
            var root = RepositoryRoot();
            var packaging = Path.Combine(root, "src", "Kubuno.VisualStudio", "Kubuno.VisualStudio.csproj");
            var referenced = ProjectReferences(packaging).ToList();

            // Kubuno.Cargo.MSBuild.Tasks ships inside the Kubuno.Rust.Sdk NuGet package (tools\SdkFeed\), not as a VSIX assembly.
            var shipped = SourceProjects().Select(p => p.Name).Where(n => n is not "Kubuno.VisualStudio" and not "Kubuno.Cargo.MSBuild.Tasks");
            foreach (var project in shipped)
            {
                CollectionAssert.Contains(referenced, project, "src/Kubuno.VisualStudio must reference " + project + " so the VSIX ships it.");
            }

            var package = File.ReadAllText(Path.Combine(root, "src", "Kubuno.VisualStudio", "KubunoPackage.cs"));
            foreach (var (_, _, layerClass) in ProductLayers)
            {
                StringAssert.Contains(package, "new " + layerClass + "()", "KubunoPackage.CreateLayers must list " + layerClass + ".");
            }
        }

        [TestMethod]
        public void Namespaces_follow_the_layer_assemblies()
        {
            var root = RepositoryRoot();
            var wrong = new List<string>();
            foreach (var project in SourceProjects())
            {
                var rootNamespace = XDocument.Load(project.Path).Descendants().FirstOrDefault(e => e.Name.LocalName == "RootNamespace")?.Value ?? project.Name;
                var dir = Path.GetDirectoryName(project.Path)!;
                foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains(@"\obj\") && !f.Contains(@"\bin\")))
                {
                    foreach (Match match in Regex.Matches(File.ReadAllText(file), @"^\s*namespace\s+([\w\.]+)", RegexOptions.Multiline))
                    {
                        var ns = match.Groups[1].Value;
                        if (ns != rootNamespace && !ns.StartsWith(rootNamespace + ".", StringComparison.Ordinal) && ns != "System.Runtime.CompilerServices")
                        {
                            wrong.Add($"{file.Substring(root.Length + 1)}: namespace {ns} (expected {rootNamespace}.*)");
                        }
                    }
                }
            }

            Assert.AreEqual(0, wrong.Count, "Files outside their assembly's namespace:\n" + string.Join("\n", wrong));
        }
    }
}
