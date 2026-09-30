using System;
using System.Collections.Generic;
using System.Linq;

namespace Kubuno.Rust.Logic.SolutionExplorer
{
    /// <summary>One read-only row of the Properties window for a dependency node.</summary>
    public sealed class DependencyPropertyRow
    {
        public DependencyPropertyRow(string key, string displayName, string value, string description)
        {
            Key = key;
            DisplayName = displayName;
            Value = value;
            Description = description;
        }

        /// <summary>Language-independent identifier (the property descriptor's name).</summary>
        public string Key { get; }

        public string DisplayName { get; }

        public string Value { get; }

        /// <summary>Shown in the Properties window's bottom pane.</summary>
        public string Description { get; }

        public override string ToString() => $"{DisplayName} = {Value}";
    }

    /// <summary>
    /// What F4 shows for a dependency node - the Cargo counterpart of .NET's "Package reference
    /// properties" (<c>ResolvedPackageReference</c> rule: Name, Version, Path...), read-only.
    /// </summary>
    public static class DependencyProperties
    {
        /// <summary>The Properties window's class name for <paramref name="item"/> (the grey text next to the component name).</summary>
        public static string ClassName(DependencyItem item) => item.ItemKind == DependencyItemKind.Crate
            ? DependenciesText.CrateClassName
            : DependenciesText.ToolchainClassName;

        public static IReadOnlyList<DependencyPropertyRow> For(DependencyItem item)
        {
            if (item is null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            var rows = new List<DependencyPropertyRow>();
            void Add(string key, string en, string fr, string? value, string enDescription, string frDescription)
            {
                rows.Add(new DependencyPropertyRow(key, DependenciesText.T(en, fr), value ?? string.Empty, DependenciesText.T(enDescription, frDescription)));
            }

            if (item.ItemKind != DependencyItemKind.Crate)
            {
                var toolchain = item.Toolchain;
                Add("Name", "Name", "Nom", item.Name, "Name of the crate or toolchain.", "Nom de la crate ou de la chaîne d'outils.");
                Add("Version", "Version", "Version", toolchain?.Release, "Version of the Rust compiler (rustc -vV).", "Version du compilateur Rust (rustc -vV).");
                Add("Channel", "Channel", "Canal", toolchain?.Channel, "Release channel of the toolchain: stable, beta or nightly.", "Canal de publication de la chaîne d'outils : stable, beta ou nightly.");
                Add("Host", "Target", "Cible", toolchain?.Host, "Target triple the compiler builds for by default.", "Triplet de la cible pour laquelle le compilateur génère par défaut.");
                if (item.ItemKind == DependencyItemKind.Toolchain)
                {
                    Add("CommitHash", "Commit", "Commit", toolchain?.CommitHash, "Commit of the Rust repository the compiler was built from.", "Commit du dépôt Rust à partir duquel le compilateur a été généré.");
                    Add("CommitDate", "Commit date", "Date du commit", toolchain?.CommitDate, "Date of that commit.", "Date de ce commit.");
                    Add("LlvmVersion", "LLVM version", "Version LLVM", toolchain?.LlvmVersion, "Version of the LLVM back end.", "Version du back-end LLVM.");
                }

                Add("Path", "Path", "Chemin d'accès", item.Directory, item.ItemKind == DependencyItemKind.Toolchain
                    ? "Sysroot of the toolchain (rustc --print sysroot)."
                    : "Source folder of the crate (installed with the rust-src component).", item.ItemKind == DependencyItemKind.Toolchain
                    ? "Sysroot de la chaîne d'outils (rustc --print sysroot)."
                    : "Dossier source de la crate (installé avec le composant rust-src).");
                return rows;
            }

            var declaration = item.PrimaryDeclaration;
            var package = item.Package;
            Add("Name", "Name", "Nom", item.Name, "Name of the crate.", "Nom de la crate.");
            if (item.Rename != null)
            {
                Add("Rename", "Alias", "Alias", item.Rename, "Name the crate is imported under in this package (package = \"...\" in Cargo.toml).", "Nom sous lequel la crate est importée dans ce package (package = \"...\" dans Cargo.toml).");
            }

            Add("RequestedVersion", "Requested version", "Version demandée", item.IsTransitive ? null : item.RequestedVersion ?? declaration?.Req,
                "Version requirement written in Cargo.toml.", "Exigence de version écrite dans Cargo.toml.");
            Add("ResolvedVersion", "Resolved version", "Version résolue", item.ResolvedVersion,
                "Version cargo selected (recorded in Cargo.lock).", "Version sélectionnée par cargo (enregistrée dans Cargo.lock).");
            if (item.LatestVersion != null)
            {
                Add("LatestVersion", "Latest version", "Dernière version", item.LatestVersion, "Latest stable version published on crates.io.", "Dernière version stable publiée sur crates.io.");
            }

            if (item.IsCratesIo || item.IsRegistry)
            {
                Add("Yanked", "Yanked", "Retirée (yanked)", Bool(item.IsYanked), "Whether the resolved version has been yanked from the registry.", "Indique si la version résolue a été retirée du registre.");
            }

            Add("Source", "Source", "Source", SourceText(item), "Where the crate comes from: a registry, a local path or a git repository.", "Provenance de la crate : un registre, un chemin local ou un dépôt git.");
            Add("Path", "Path", "Chemin d'accès", item.Directory, "Folder of the crate's Cargo.toml.", "Dossier du Cargo.toml de la crate.");
            Add("ActivatedFeatures", "Activated features", "Fonctionnalités activées", string.Join(", ", item.ActivatedFeatures),
                "Features cargo enabled for this crate in the build (unified across the workspace).", "Fonctionnalités (features) activées par cargo pour cette crate (unifiées dans l'espace de travail).");
            if (!item.IsTransitive)
            {
                Add("RequestedFeatures", "Requested features", "Fonctionnalités demandées", string.Join(", ", item.Declarations.SelectMany(d => d.Features).Distinct()),
                    "Features listed in Cargo.toml for this dependency.", "Fonctionnalités listées dans Cargo.toml pour cette dépendance.");
                Add("DefaultFeatures", "Default features", "Fonctionnalités par défaut", Bool(item.Declarations.All(d => d.UsesDefaultFeatures)),
                    "Whether the crate's default features are enabled (default-features).", "Indique si les fonctionnalités par défaut de la crate sont activées (default-features).");
                Add("Optional", "Optional", "Facultative", Bool(item.IsOptional),
                    "Whether the dependency is optional (only built when a feature enables it).", "Indique si la dépendance est facultative (générée seulement quand une fonctionnalité l'active).");
            }

            Add("Kind", "Type", "Type", KindText(item.Kinds), "Cargo.toml table the dependency is declared in: [dependencies], [dev-dependencies] or [build-dependencies].", "Table de Cargo.toml où la dépendance est déclarée : [dependencies], [dev-dependencies] ou [build-dependencies].");
            if (!item.IsTransitive)
            {
                Add("Target", "Target", "Cible", item.Target, "Platform condition of a [target.'cfg(...)'.dependencies] declaration.", "Condition de plateforme d'une déclaration [target.'cfg(...)'.dependencies].");
            }

            Add("License", "License", "Licence", package?.License ?? package?.LicenseFile, "License of the crate (SPDX expression).", "Licence de la crate (expression SPDX).");
            Add("Repository", "Repository", "Dépôt", package?.Repository, "Source repository of the crate.", "Dépôt de code source de la crate.");
            Add("Description", "Description", "Description", package?.Description, "Description of the crate.", "Description de la crate.");
            Add("Transitive", "Transitive", "Transitive", Bool(item.IsTransitive), "Whether this crate is a dependency of another dependency rather than declared in Cargo.toml.", "Indique si cette crate est une dépendance d'une autre dépendance plutôt que déclarée dans Cargo.toml.");
            return rows;
        }

        public static string KindText(DependencyKinds kinds)
        {
            var parts = new List<string>();
            if ((kinds & DependencyKinds.Normal) != 0)
            {
                parts.Add(DependenciesText.KindNormal);
            }

            if ((kinds & DependencyKinds.Dev) != 0)
            {
                parts.Add(DependenciesText.KindDev);
            }

            if ((kinds & DependencyKinds.Build) != 0)
            {
                parts.Add(DependenciesText.KindBuild);
            }

            return string.Join(", ", parts);
        }

        /// <summary><c>Registre (crates.io)</c>, <c>Chemin</c>, <c>Git (https://..., tag=1.0.18) @ 12746aa</c>.</summary>
        public static string SourceText(DependencyItem item)
        {
            var source = item.Source;
            if (source is null)
            {
                return item.ItemKind == DependencyItemKind.Crate ? DependenciesText.SourcePath : DependenciesText.SourceSysroot;
            }

            if (item.IsGit)
            {
                // git+URL?ref#commit
                var body = source.Substring("git+".Length);
                string? commit = null;
                var hash = body.IndexOf('#');
                if (hash >= 0)
                {
                    commit = body.Substring(hash + 1);
                    body = body.Substring(0, hash);
                }

                string? reference = null;
                var query = body.IndexOf('?');
                if (query >= 0)
                {
                    reference = body.Substring(query + 1);
                    body = body.Substring(0, query);
                }

                var text = DependenciesText.SourceGit(body, reference);
                return commit != null && commit.Length >= 7 ? $"{text} @ {commit.Substring(0, Math.Min(12, commit.Length))}" : text;
            }

            if (item.IsCratesIo)
            {
                return DependenciesText.SourceRegistry("crates.io");
            }

            var registry = source.Substring(source.IndexOf('+') + 1);
            return DependenciesText.SourceRegistry(item.PrimaryDeclaration?.Registry ?? registry);
        }

        private static string Bool(bool value) => value ? DependenciesText.Yes : DependenciesText.No;
    }
}
