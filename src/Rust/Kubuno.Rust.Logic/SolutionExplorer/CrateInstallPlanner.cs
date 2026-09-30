using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Kubuno.Rust.Cargo.Commands;
using Kubuno.Rust.Cargo.Metadata;

namespace Kubuno.Rust.Logic.SolutionExplorer
{
    /// <summary>
    /// Turns what the crate manager (or a Dependencies menu) asks for into <c>cargo add</c> /
    /// <c>cargo remove</c> invocations - cargo edits Cargo.toml, never this extension. <c>cargo add</c>
    /// on an existing dependency updates its version requirement and adds features but cannot drop
    /// one, so removing a feature is a <c>cargo remove</c> followed by a <c>cargo add</c> that restores
    /// every other setting (rename, optional, default-features, table, target).
    /// </summary>
    public static class CrateInstallPlanner
    {
        /// <summary>A new dependency: <c>cargo add name@version [--features ..] [--no-default-features] [--dev|--build]</c>.</summary>
        public static IReadOnlyList<CargoCommand> Install(string manifestPath, string? packageName, string crateName, string? version, IEnumerable<string> features, bool defaultFeatures, DependencyKinds kind)
        {
            var args = new List<string> { Spec(crateName, version) };
            AddFeatures(args, features.ToList());
            if (!defaultFeatures)
            {
                args.Add("--no-default-features");
            }

            AddKind(args, KindName(kind), target: null);
            return new[] { Command(CargoCommand.Add(), manifestPath, packageName, args) };
        }

        /// <summary>
        /// Changes an installed dependency (every table/target it is declared in) to <paramref name="version"/>
        /// with exactly <paramref name="features"/> and <paramref name="defaultFeatures"/>.
        /// </summary>
        public static IReadOnlyList<CargoCommand> Update(string manifestPath, string? packageName, DependencyItem installed, string? version, IEnumerable<string> features, bool defaultFeatures)
        {
            if (installed is null)
            {
                throw new ArgumentNullException(nameof(installed));
            }

            var wanted = features.Distinct(StringComparer.Ordinal).ToList();
            var commands = new List<CargoCommand>();
            foreach (var declaration in Tables(installed))
            {
                var current = declaration.Features;
                var dropsFeatures = current.Any(f => !wanted.Contains(f, StringComparer.Ordinal));
                if (dropsFeatures)
                {
                    commands.Add(RemoveCommand(manifestPath, packageName, declaration.LocalName, declaration.Kind, declaration.Target));
                }

                var args = new List<string> { Spec(installed.Name, version) };
                if (declaration.Rename != null)
                {
                    args.Add("--rename");
                    args.Add(declaration.Rename);
                }

                // After a remove every wanted feature must be re-added; otherwise only the new ones.
                AddFeatures(args, dropsFeatures ? wanted : wanted.Where(f => !current.Contains(f, StringComparer.Ordinal)).ToList());
                if (dropsFeatures && declaration.Optional)
                {
                    args.Add("--optional");
                }

                if (defaultFeatures != declaration.UsesDefaultFeatures || (dropsFeatures && !defaultFeatures))
                {
                    args.Add(defaultFeatures ? "--default-features" : "--no-default-features");
                }

                AddKind(args, declaration.Kind, declaration.Target);
                commands.Add(Command(CargoCommand.Add(), manifestPath, packageName, args));
            }

            return commands;
        }

        /// <summary><c>cargo remove</c> once per table/target the dependency is declared in.</summary>
        public static IReadOnlyList<CargoCommand> Uninstall(string manifestPath, string? packageName, string localName, IReadOnlyList<CargoDependency> declarations)
        {
            var tables = declarations.Select(d => (d.Kind, d.Target)).Distinct().ToList();
            if (tables.Count == 0)
            {
                tables.Add((null, null));
            }

            return tables.Select(t => RemoveCommand(manifestPath, packageName, localName, t.Kind, t.Target)).ToList();
        }

        /// <summary>The <c>cargo ...</c> text of a planned command, for the Output pane and error messages.</summary>
        public static string Describe(CargoCommand command)
        {
            var line = command.ToCommandLine();
            return "cargo " + line.Arguments;
        }

        /// <summary>A download count the way NuGet shows it: <c>1.4G</c>, <c>326.3M</c>, <c>12.5K</c>, <c>812</c>.</summary>
        public static string FormatCount(long count, CultureInfo? culture = null)
        {
            culture ??= CultureInfo.CurrentCulture;
            if (count >= 1_000_000_000)
            {
                return (count / 1_000_000_000d).ToString("0.#", culture) + "G";
            }

            if (count >= 1_000_000)
            {
                return (count / 1_000_000d).ToString("0.#", culture) + "M";
            }

            return count >= 1_000 ? (count / 1_000d).ToString("0.#", culture) + "K" : count.ToString(culture);
        }

        private static IEnumerable<CargoDependency> Tables(DependencyItem item) =>
            item.Declarations.Count > 0
                ? item.Declarations.GroupBy(d => (d.Kind, d.Target)).Select(g => g.First())
                : new[] { new CargoDependency { Name = item.Name, Rename = item.Rename } };

        private static CargoCommand RemoveCommand(string manifestPath, string? packageName, string localName, string? kind, string? target)
        {
            var args = new List<string> { localName };
            AddKind(args, kind, target);
            return Command(CargoCommand.Remove(), manifestPath, packageName, args);
        }

        private static CargoCommand Command(CargoCommand command, string manifestPath, string? packageName, List<string> args)
        {
            command = command.WithManifestPath(manifestPath);
            if (!string.IsNullOrEmpty(packageName))
            {
                command = command.WithPackage(packageName!);
            }

            return command.WithExtraArgs(args.ToArray());
        }

        private static string Spec(string name, string? version) => string.IsNullOrEmpty(version) ? name : $"{name}@{version}";

        private static void AddFeatures(List<string> args, IReadOnlyList<string> features)
        {
            if (features.Count > 0)
            {
                args.Add("--features");
                args.Add(string.Join(",", features));
            }
        }

        private static void AddKind(List<string> args, string? kind, string? target)
        {
            if (kind == "dev")
            {
                args.Add("--dev");
            }
            else if (kind == "build")
            {
                args.Add("--build");
            }

            if (target != null)
            {
                args.Add("--target");
                args.Add(target);
            }
        }

        private static string? KindName(DependencyKinds kind) =>
            (kind & DependencyKinds.Dev) != 0 ? "dev" : (kind & DependencyKinds.Build) != 0 ? "build" : null;
    }
}
