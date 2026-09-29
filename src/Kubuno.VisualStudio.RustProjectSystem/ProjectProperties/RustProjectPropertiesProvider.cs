using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Properties;
using Microsoft.VisualStudio.Threading;

namespace Kubuno.VisualStudio.RustProjectSystem.ProjectProperties
{
    /// <summary>
    /// The <c>KubunoRust</c> persistence of Kubuno.Rust.Sdk's rule files (docs/RSPROJ.md, "Project properties like
    /// .NET"): CPS resolves a rule's <c>DataSource Persistence="KubunoRust"</c> to the
    /// <see cref="IProjectPropertiesProvider"/> exported with that <c>Name</c> metadata (checked in the installed
    /// <c>Microsoft.VisualStudio.ProjectSystem.Implementation.dll</c>, <c>PropertyPagesDataModelProvider</c>) - the
    /// same seam the .NET project system uses for its <c>ProjectFileWithInterception</c> persistence. Project-level
    /// reads and writes go to <see cref="RustProjectProperties"/> (Cargo.toml, profiles, lints, rustfmt.toml,
    /// windows_subsystem, and a few MSBuild properties needing a value conversion); item-level ones and events
    /// are the project file's own.
    /// </summary>
    [Export(typeof(IProjectPropertiesProvider))]
    [Export(PersistenceName, typeof(IProjectPropertiesProvider))]
    [ExportMetadata("Name", PersistenceName)]
    [AppliesTo(RustProjectCapabilities.RustProjectSystem)]
    internal sealed class RustProjectPropertiesProvider : IProjectPropertiesProvider
    {
        public const string PersistenceName = "KubunoRust";

        private readonly IProjectPropertiesProvider _projectFile;
        private readonly IProjectPropertiesProvider _userFile;
        private readonly ConfiguredProject _configuredProject;
        private readonly IProjectThreadingService _threading;

        [ImportingConstructor]
        public RustProjectPropertiesProvider(
            [Import("Microsoft.VisualStudio.ProjectSystem.ProjectFile")] IProjectPropertiesProvider projectFile,
            [Import("Microsoft.VisualStudio.ProjectSystem.UserFile")] IProjectPropertiesProvider userFile,
            ConfiguredProject configuredProject,
            IProjectThreadingService threading)
        {
            _projectFile = projectFile;
            _userFile = userFile;
            _configuredProject = configuredProject;
            _threading = threading;
        }

        public string DefaultProjectPath => _projectFile.DefaultProjectPath;

        public event AsyncEventHandler<ProjectPropertyChangedEventArgs>? ProjectPropertyChanging
        {
            add => _projectFile.ProjectPropertyChanging += value;
            remove => _projectFile.ProjectPropertyChanging -= value;
        }

        public event AsyncEventHandler<ProjectPropertyChangedEventArgs>? ProjectPropertyChangedOnWriter
        {
            add => _projectFile.ProjectPropertyChangedOnWriter += value;
            remove => _projectFile.ProjectPropertyChangedOnWriter -= value;
        }

        public event AsyncEventHandler<ProjectPropertyChangedEventArgs>? ProjectPropertyChanged
        {
            add => _projectFile.ProjectPropertyChanged += value;
            remove => _projectFile.ProjectPropertyChanged -= value;
        }

        public IProjectProperties GetCommonProperties() =>
            Wrap(_projectFile.GetCommonProperties());

        public IProjectProperties GetItemTypeProperties(string? itemType) =>
            _projectFile.GetItemTypeProperties(itemType);

        public IProjectProperties GetItemProperties(string? itemType, string? item) =>
            itemType is null && item is null ? GetCommonProperties() : _projectFile.GetItemProperties(itemType, item);

        public IProjectProperties GetProperties(string file, string? itemType, string? item) =>
            itemType is null && item is null
                ? Wrap(_projectFile.GetProperties(file, itemType, item))
                : _projectFile.GetProperties(file, itemType, item);

        private IProjectProperties Wrap(IProjectProperties projectFileProperties)
        {
            PropertiesLog.Write($"properties requested for {projectFileProperties.FileFullPath} [{string.Join(",", _configuredProject.ProjectConfiguration.Dimensions.Values)}]");
            return new RustProjectProperties(projectFileProperties, _userFile, _configuredProject, _threading);
        }
    }
}
