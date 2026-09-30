namespace Kubuno.Cargo.Win32
{
    /// <summary>Oldest Windows version the application declares support for.</summary>
    public enum Win32MinimumWindows
    {
        /// <summary>Windows 7 (all supportedOS entries).</summary>
        Windows7,

        /// <summary>Windows 8.</summary>
        Windows8,

        /// <summary>Windows 8.1.</summary>
        Windows81,

        /// <summary>Windows 10 and 11.</summary>
        Windows10,
    }

    /// <summary>DPI awareness declared in the manifest.</summary>
    public enum Win32DpiAwareness
    {
        /// <summary>DPI unaware (the system bitmap-scales the window).</summary>
        Unaware,

        /// <summary>System DPI aware.</summary>
        System,

        /// <summary>Per-monitor aware.</summary>
        PerMonitor,

        /// <summary>Per-monitor aware, version 2 (Windows 10 1703+; falls back to PerMonitor).</summary>
        PerMonitorV2,
    }

    /// <summary>UAC requested execution level.</summary>
    public enum Win32ExecutionLevel
    {
        /// <summary>Runs with the parent process token.</summary>
        AsInvoker,

        /// <summary>Highest privileges the user can get.</summary>
        HighestAvailable,

        /// <summary>Always elevates.</summary>
        RequireAdministrator,
    }

    /// <summary>Inputs of <see cref="Win32ManifestBuilder.Build"/>.</summary>
    public sealed class Win32ManifestOptions
    {
        /// <summary>When non-empty, an assemblyIdentity element with this name is emitted.</summary>
        public string? AssemblyName { get; set; }

        /// <summary>Version of the assemblyIdentity (default "1.0.0.0").</summary>
        public string AssemblyVersion { get; set; } = "1.0.0.0";

        /// <summary>Requested UAC execution level.</summary>
        public Win32ExecutionLevel ExecutionLevel { get; set; } = Win32ExecutionLevel.AsInvoker;

        /// <summary>Minimum supported Windows version.</summary>
        public Win32MinimumWindows MinimumWindows { get; set; } = Win32MinimumWindows.Windows10;

        /// <summary>DPI awareness.</summary>
        public Win32DpiAwareness DpiAwareness { get; set; } = Win32DpiAwareness.PerMonitorV2;

        /// <summary>Opt in to paths longer than MAX_PATH.</summary>
        public bool LongPathAware { get; set; }

        /// <summary>Opt in to the UTF-8 active code page.</summary>
        public bool UseUtf8CodePage { get; set; }

        /// <summary>Depend on Microsoft.Windows.Common-Controls 6.0.0.0 (visual styles).</summary>
        public bool CommonControlsV6 { get; set; }
    }
}
