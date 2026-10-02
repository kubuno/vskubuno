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
    /// Shared &lt;- Rust &lt;- Views &lt;- Desktop / Web / Mobile - so a Kubuno target (desktop apps today, web modules and
    /// mobile apps next) can be added without touching the layers below it, and the targets never depend on each other.
    /// Views holds what every target with .kbview views shares (the view designer, the .kbview language client, the .kbres
    /// editor; docs/WEB-VIEWS.md WV-8). Checked on the project files and on the compiled assemblies.
    /// </summary>
    [TestClass]
    public sealed class LayeringTests
    {
        private enum Layer
        {
            Shared,
            Rust,
            Views,
            Desktop,
            Web,
            Mobile,
            Packaging,
        }

        /// <summary>What each layer may reference (itself included).</summary>
        private static readonly Dictionary<Layer, Layer[]> Allowed = new()
        {
            [Layer.Shared] = new[] { Layer.Shared },
            [Layer.Rust] = new[] { Layer.Shared, Layer.Rust },
            [Layer.Views] = new[] { Layer.Shared, Layer.Rust, Layer.Views },
            [Layer.Desktop] = new[] { Layer.Shared, Layer.Rust, Layer.Views, Layer.Desktop },
            [Layer.Web] = new[] { Layer.Shared, Layer.Rust, Layer.Views, Layer.Web },
            [Layer.Mobile] = new[] { Layer.Shared, Layer.Rust, Layer.Views, Layer.Mobile },
            [Layer.Packaging] = new[] { Layer.Shared, Layer.Rust, Layer.Views, Layer.Desktop, Layer.Web, Layer.Mobile, Layer.Packaging },
        };

        /// <summary>The product layers, each with the one assembly that registers its <c>KubunoLayer</c>.</summary>
        private static readonly (Layer Layer, string Assembly, string LayerClass)[] ProductLayers =
        {
            (Layer.Rust, "Kubuno.Rust", "Kubuno.Rust.RustLayer"),
            (Layer.Views, "Kubuno.Views", "Kubuno.Views.ViewsLayer"),
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
                return Layer.Shared; // Kubuno.Shared.Mcp's executable name.
            }

            (string Prefix, Layer Layer)[] prefixes =
            {
                ("Kubuno.Shared", Layer.Shared),
                ("Kubuno.Rust", Layer.Rust),
                ("Kubuno.Cargo.MSBuild.Tasks", Layer.Rust), // Kubuno.Rust.Sdk's task assembly: its name is part of the SDK.
                ("Kubuno.Views", Layer.Views),
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
                "Kubuno.Shared", "Kubuno.Shared.Logic", "Kubuno.Shared.Mcp", "Kubuno.Shared.Mcp.Bridge",
                "Kubuno.Shared.DevAssistant", "Kubuno.Shared.DevAssistant.Logic", "Kubuno.Shared.DevAssistant.Host",
                "Kubuno.Rust", "Kubuno.Rust.Logic", "Kubuno.Rust.Cargo", "Kubuno.Rust.Launch", "Kubuno.Rust.ProjectSystem",
                "Kubuno.Rust.TestAdapter", "Kubuno.Rust.Debugger", "Kubuno.Rust.TemplateWizard", "Kubuno.Cargo.MSBuild.Tasks",
                "Kubuno.Views", "Kubuno.Views.Logic",
                "Kubuno.Desktop", "Kubuno.Desktop.Logic", "Kubuno.Desktop.ProjectSystem", "Kubuno.Desktop.TemplateWizard",
                "Kubuno.Web", "Kubuno.Web.Logic", "Kubuno.Web.ProjectSystem", "Kubuno.Web.MSBuild.Tasks", "Kubuno.Web.TemplateWizard",
                "Kubuno.Mobile", "Kubuno.VisualStudio",
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

        /// <summary>
        /// docs/WEB-VIEWS.md WV-8: the web layer reuses the view designer through the views layer, never through the desktop
        /// layer - checked transitively on the project files (no project the web layer builds against is a desktop one,
        /// whatever the path) and on the compiled web assemblies.
        /// </summary>
        [TestMethod]
        public void The_web_layer_never_references_the_desktop_layer()
        {
            var violations = TargetReferences(Layer.Web, new[] { Layer.Desktop });
            Assert.AreEqual(0, violations.Count, "The web layer reaches the desktop layer:\n" + string.Join("\n", violations));
        }

        /// <summary>The views layer is shared by every target, so it never reaches one of them, even transitively.</summary>
        [TestMethod]
        public void The_views_layer_never_references_a_target()
        {
            var violations = TargetReferences(Layer.Views, new[] { Layer.Desktop, Layer.Web, Layer.Mobile });
            Assert.AreEqual(0, violations.Count, "The views layer reaches a target layer:\n" + string.Join("\n", violations));
        }

        /// <summary>
        /// The projects of layer <paramref name="from"/> whose transitive project references, or whose compiled assembly's
        /// references, reach a project or assembly of one of the <paramref name="forbidden"/> layers.
        /// </summary>
        private static List<string> TargetReferences(Layer from, Layer[] forbidden)
        {
            var violations = new List<string>();
            var projects = SourceProjects().Where(p => LayerOf(p.Name) == from).ToList();
            Assert.IsTrue(projects.Count > 0, "no project of the " + from + " layer");
            foreach (var project in projects)
            {
                foreach (var reached in ProjectClosure(project.Path))
                {
                    var name = Path.GetFileNameWithoutExtension(reached);
                    if (LayerOf(name) is { } to && forbidden.Contains(to))
                    {
                        violations.Add($"{project.Name} builds against {name} ({to})");
                    }
                }

                if (BuiltAssembly(project.Path, project.AssemblyName) is { } assembly)
                {
                    foreach (var reference in AssemblyReferences(assembly))
                    {
                        if (LayerOf(reference) is { } to && forbidden.Contains(to))
                        {
                            violations.Add($"{project.AssemblyName} references the assembly {reference} ({to})");
                        }
                    }
                }
            }

            return violations;
        }

        /// <summary>Every project reachable through the ProjectReference items of <paramref name="projectPath"/> (full paths).</summary>
        private static IReadOnlyCollection<string> ProjectClosure(string projectPath)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Stack<string>();
            pending.Push(Path.GetFullPath(projectPath));
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                foreach (var include in XDocument.Load(current).Descendants().Where(e => e.Name.LocalName == "ProjectReference").Select(e => (string)e.Attribute("Include")!))
                {
                    var reference = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(current)!, include));
                    if (File.Exists(reference) && seen.Add(reference))
                    {
                        pending.Push(reference);
                    }
                }
            }

            return seen;
        }

        [TestMethod]
        public void Pure_logic_assemblies_do_not_reference_the_Visual_Studio_SDK()
        {
            var pure = SourceProjects().Where(p => p.Name.EndsWith(".Logic", StringComparison.Ordinal) || p.Name is "Kubuno.Rust.Cargo" or "Kubuno.Rust.Launch").ToList();
            Assert.AreEqual(8, pure.Count, "Kubuno.Shared.Logic, Kubuno.Shared.DevAssistant.Logic, Kubuno.Rust.Logic, Kubuno.Rust.Cargo, Kubuno.Rust.Launch, Kubuno.Views.Logic, Kubuno.Desktop.Logic, Kubuno.Web.Logic");
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
                    if (reader.GetString(baseType.Namespace) == "Kubuno.Shared.Extensibility" && reader.GetString(baseType.Name) == "KubunoLayer")
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

            // Kubuno.Cargo.MSBuild.Tasks and Kubuno.Web.MSBuild.Tasks ship inside the Kubuno.Rust.Sdk and Kubuno.Web.Sdk NuGet
            // packages (tools\SdkFeed\), not as VSIX assemblies.
            var shipped = SourceProjects().Select(p => p.Name).Where(n => n is not "Kubuno.VisualStudio" and not "Kubuno.Cargo.MSBuild.Tasks" and not "Kubuno.Web.MSBuild.Tasks");
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
