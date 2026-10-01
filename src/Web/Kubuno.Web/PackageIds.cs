namespace Kubuno.Web
{
    /// <summary>IDs of the commands the web layer handles, in Kubuno.Core's command set, matching <c>KubunoCommands.vsct</c> (0x07xx).</summary>
    public static class PackageIds
    {
        /// <summary>Tools > "Kubuno: Generate Web Solution" (the core or the module of the active document/solution).</summary>
        public const int GenerateWebSolutionCommand = 0x0700;

        /// <summary>Tools > "Kubuno: Generate Multi-Repository Web Solution" (the core and chosen modules).</summary>
        public const int GenerateMultiRepoSolutionCommand = 0x0701;

        /// <summary>Tools > "Kubuno: Check Versions" (check_versions.py, or its built-in subset).</summary>
        public const int CheckVersionsCommand = 0x0702;

        /// <summary>Tools > "Kubuno: Prepare npm Floors" (bump_npm_floors.sh without the commits).</summary>
        public const int PrepareNpmFloorsCommand = 0x0703;

        /// <summary>Tools > "Kubuno: Prepare Shared Crate Tags" (bump_shared_crates.sh without the commits).</summary>
        public const int PrepareSharedCrateTagsCommand = 0x0704;

        /// <summary>Tools > "Kubuno: Package Module (.kbpkg)".</summary>
        public const int PackModuleCommand = 0x0705;

        /// <summary>Tools > "Kubuno Core Web: Open Development Database Tunnel" (the SSH tunnel F5 opens, without the core).</summary>
        public const int OpenDevDatabaseTunnelCommand = 0x0706;
    }
}
