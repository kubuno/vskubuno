using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kubuno.Web.Logic.DevCore
{
    /// <summary>
    /// The development core of this machine (docs/WEB.md, "The dev core"): one folder, by default
    /// <c>%LOCALAPPDATA%\Kubuno\dev-core</c> (<see cref="RootVariable"/> or the <c>KubunoDevCoreRoot</c> property moves it),
    /// holding everything the Linux layout spreads over <c>/etc/kubuno</c>, <c>/var/lib/kubuno</c> and
    /// <c>/var/log/kubuno</c>: the modules deployed by F5 (<c>modules\&lt;id&gt;</c>, the <c>modules_dir</c> of the
    /// core), the <c>.kbpkg</c> store (<c>modules-store</c>), the modules' configuration and data, the stored files,
    /// the logs, the data key and the generated development secrets. The core's working directory is this folder, so
    /// the files the core places relative to it (<c>data.key</c>, <c>initial-admin-password</c>) land here too.
    /// </summary>
    public sealed class DevCoreLayout
    {
        /// <summary>Moves the dev core folder (also the <c>KubunoDevCoreRoot</c> MSBuild property).</summary>
        public const string RootVariable = "KUBUNO_DEV_CORE_ROOT";

        /// <summary>The default port: the one the core's Vite dev server proxies to (core/frontend/vite.config.ts).</summary>
        public const int DefaultPort = 8080;

        public DevCoreLayout(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new ArgumentException("The dev core folder is empty.", nameof(root));
            }

            Root = Path.GetFullPath(root);
        }

        public string Root { get; }

        /// <summary>Modules deployed by F5 (deploy_local.sh's "system layout"): the core's <c>server.modules_dir</c>.</summary>
        public string ModulesDirectory => Path.Combine(Root, "modules");

        /// <summary>Modules installed from a <c>.kbpkg</c>: the core's <c>server.modules_install_dir</c> (wins over <see cref="ModulesDirectory"/>).</summary>
        public string ModulesStoreDirectory => Path.Combine(Root, "modules-store");

        public string ModulesConfigDirectory => Path.Combine(Root, "modules-config");

        public string ModulesDataDirectory => Path.Combine(Root, "modules-data");

        public string ThemesDirectory => Path.Combine(Root, "themes");

        public string FilesDirectory => Path.Combine(Root, "files");

        public string LogsDirectory => Path.Combine(Root, "logs");

        /// <summary>SQLite files (unused with PostgreSQL, but the default would be a Linux path).</summary>
        public string SqliteDirectory => Path.Combine(Root, "db");

        /// <summary>The <c>KUBUNO_PATHS_CONFIG_DIR</c> of the dev core: the system configuration file it would read (never %ProgramData%\Kubuno).</summary>
        public string ConfigDirectory => Path.Combine(Root, "config");

        public string CacheDirectory => Path.Combine(Root, "cache");

        public string RuntimeDirectory => Path.Combine(Root, "run");

        public string BackupsDirectory => Path.Combine(Root, "backups");

        public string DataKeyFile => Path.Combine(Root, "data.key");

        /// <summary>Where the core writes the first administrator's generated password on a fresh database.</summary>
        public string InitialPasswordFile => Path.Combine(Root, "initial-admin-password");

        public string SecretsFile => Path.Combine(Root, "dev-secrets.json");

        /// <summary>The core repository of the last core F5 (a module outside <c>..\core</c> takes its host frontend from there).</summary>
        public string CoreRepositoryFile => Path.Combine(Root, "core-repository.txt");

        /// <summary>Records the core repository a core F5 started from. Best effort.</summary>
        public void RememberCoreRepository(string coreRepository)
        {
            try
            {
                Directory.CreateDirectory(Root);
                File.WriteAllText(CoreRepositoryFile, Path.GetFullPath(coreRepository));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                // Only a fallback for later module launches.
            }
        }

        /// <summary>The core repository recorded by <see cref="RememberCoreRepository"/>, when it still holds the core; else null.</summary>
        public string? RememberedCoreRepository()
        {
            try
            {
                if (!File.Exists(CoreRepositoryFile))
                {
                    return null;
                }

                var path = File.ReadAllText(CoreRepositoryFile).Trim();
                return path.Length > 0 && File.Exists(Path.Combine(path, "crates", "kubuno-core", "Cargo.toml")) ? path : null;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                return null;
            }
        }

        /// <summary><c>%LOCALAPPDATA%\Kubuno\dev-core</c>, or <see cref="RootVariable"/> when set.</summary>
        public static DevCoreLayout Default(Func<string, string?>? environment = null)
        {
            environment ??= Environment.GetEnvironmentVariable;
            var configured = environment(RootVariable);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return new DevCoreLayout(configured!);
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return new DevCoreLayout(Path.Combine(localAppData, "Kubuno", "dev-core"));
        }

        /// <summary>
        /// Where a module is deployed: its store folder when it was installed from a <c>.kbpkg</c> (the core prefers it),
        /// else <c>modules\&lt;id&gt;</c> - the same choice as <c>_tools/deploy_local.sh</c>.
        /// </summary>
        public string ModuleDirectory(string moduleId)
        {
            var store = Path.Combine(ModulesStoreDirectory, moduleId);
            return Directory.Exists(store) ? store : Path.Combine(ModulesDirectory, moduleId);
        }

        /// <summary>
        /// The folders the core starts module executables from (<see cref="ModulesDirectory"/> and
        /// <see cref="ModulesStoreDirectory"/>): any process whose image lies there belongs to this dev core. Stopping
        /// the debugger terminates the core without its children, so every module it started - not only the one being
        /// debugged - would keep running, holding its port for the next launch.
        /// </summary>
        public IReadOnlyList<string> ModuleProcessDirectories => new[] { ModulesDirectory, ModulesStoreDirectory };

        /// <summary>Creates every folder (idempotent).</summary>
        public void EnsureCreated()
        {
            foreach (var directory in new[]
            {
                Root, ModulesDirectory, ModulesStoreDirectory, ModulesConfigDirectory, ModulesDataDirectory, ThemesDirectory,
                FilesDirectory, LogsDirectory, SqliteDirectory, ConfigDirectory, CacheDirectory, RuntimeDirectory, BackupsDirectory,
            })
            {
                Directory.CreateDirectory(directory);
            }
        }

        /// <summary>
        /// The development secrets of this dev core (internal secret, JWT secret), generated on first use and kept in
        /// <see cref="SecretsFile"/>: they must survive restarts (tokens, the data key seeded from the JWT secret) and
        /// they are local to this machine - never the server's. Modules get the internal secret from the core.
        /// </summary>
        public DevCoreSecrets LoadOrCreateSecrets()
        {
            if (File.Exists(SecretsFile))
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(SecretsFile));
                    var internalSecret = document.RootElement.TryGetProperty("internal_secret", out var a) ? a.GetString() : null;
                    var jwtSecret = document.RootElement.TryGetProperty("jwt_secret", out var b) ? b.GetString() : null;
                    if (!string.IsNullOrEmpty(internalSecret) && !string.IsNullOrEmpty(jwtSecret))
                    {
                        return new DevCoreSecrets(internalSecret!, jwtSecret!);
                    }
                }
                catch (JsonException)
                {
                    // Unreadable: regenerated below (a dev core only - nothing but local test data depends on it).
                }
            }

            var secrets = new DevCoreSecrets(NewSecret(), NewSecret());
            Directory.CreateDirectory(Root);
            var json = "{\n  \"internal_secret\": \"" + secrets.InternalSecret + "\",\n  \"jwt_secret\": \"" + secrets.JwtSecret + "\"\n}\n";
            File.WriteAllText(SecretsFile, json, new UTF8Encoding(false));
            return secrets;
        }

        /// <summary>
        /// Copies the core repository's built-in themes into <see cref="ThemesDirectory"/> once (the core's
        /// <c>themes_dir</c> is writable data, like <c>/var/lib/kubuno/themes</c> on Linux). Existing files are kept.
        /// </summary>
        public void SeedThemes(string? coreThemesDirectory)
        {
            if (string.IsNullOrEmpty(coreThemesDirectory) || !Directory.Exists(coreThemesDirectory))
            {
                return;
            }

            foreach (var source in Directory.GetFiles(coreThemesDirectory!, "*", SearchOption.AllDirectories))
            {
                var relative = source.Substring(coreThemesDirectory!.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var target = Path.Combine(ThemesDirectory, relative);
                if (!File.Exists(target))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(source, target);
                }
            }
        }

        /// <summary>A string value for the core's configuration (prefixed: the <c>KV__</c> loader parses numbers).</summary>
        private static string NewSecret()
        {
            var bytes = new byte[48];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }

            return "kubunodev_" + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }

    /// <summary>The generated secrets of a dev core.</summary>
    public sealed class DevCoreSecrets
    {
        public DevCoreSecrets(string internalSecret, string jwtSecret)
        {
            InternalSecret = internalSecret;
            JwtSecret = jwtSecret;
        }

        public string InternalSecret { get; }

        public string JwtSecret { get; }
    }

    /// <summary>
    /// The environment of a dev core process: the core reads its configuration from <c>config.toml</c> then from
    /// <c>KV__SECTION__KEY</c> variables (core/crates/kubuno-core/src/config/settings.rs), so every Linux default path is
    /// replaced by a folder of <see cref="DevCoreLayout"/> without writing any configuration file - and the database URL
    /// is passed through the environment only, never written to disk.
    /// </summary>
    public static class DevCoreEnvironment
    {
        /// <param name="layout">The dev core folders.</param>
        /// <param name="databaseUrl">The accepted development database URL (DevDatabaseGuard).</param>
        /// <param name="secrets">The dev core's secrets.</param>
        /// <param name="frontendDist">The host frontend the core serves (core\frontend\dist).</param>
        /// <param name="port">The HTTP port.</param>
        public static IReadOnlyDictionary<string, string> Build(DevCoreLayout layout, string databaseUrl, DevCoreSecrets secrets, string? frontendDist, int port)
        {
            if (layout is null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (secrets is null)
            {
                throw new ArgumentNullException(nameof(secrets));
            }

            var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["KV__DATABASE__URL"] = databaseUrl,
                ["KV__DATABASE__PATH"] = layout.SqliteDirectory,
                ["KV__SERVER__HOST"] = "127.0.0.1",
                ["KV__SERVER__PORT"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["KV__SERVER__INTERNAL_SECRET"] = secrets.InternalSecret,
                ["KV__SERVER__MODULES_DIR"] = layout.ModulesDirectory,
                ["KV__SERVER__MODULES_INSTALL_DIR"] = layout.ModulesStoreDirectory,
                ["KV__SERVER__MODULES_CONFIG_DIR"] = layout.ModulesConfigDirectory,
                ["KV__SERVER__MODULES_DATA_DIR"] = layout.ModulesDataDirectory,
                ["KV__SERVER__THEMES_DIR"] = layout.ThemesDirectory,
                ["KV__AUTH__JWT_SECRET"] = secrets.JwtSecret,
                ["KV__STORAGE__LOCAL_PATH"] = layout.FilesDirectory,
                ["KV__LOGGING__LOG_DIR"] = layout.LogsDirectory,
                ["KUBUNO_DATA_KEY_FILE"] = layout.DataKeyFile,
                ["KUBUNO_INITIAL_PASSWORD_FILE"] = layout.InitialPasswordFile,
                // The core's platform layout (kubuno-paths): a per-user instance whose every directory is a folder of the
                // dev core - without them a Windows core defaults to %ProgramData%\Kubuno (system mode) and would read an
                // installed Kubuno's configuration file and write its state, backups and module data there.
                ["KUBUNO_PATHS_MODE"] = "user",
                ["KUBUNO_PATHS_CONFIG_DIR"] = layout.ConfigDirectory,
                ["KUBUNO_PATHS_STATE_DIR"] = layout.Root,
                ["KUBUNO_PATHS_DATA_DIR"] = layout.Root,
                ["KUBUNO_PATHS_LOG_DIR"] = layout.LogsDirectory,
                ["KUBUNO_PATHS_CACHE_DIR"] = layout.CacheDirectory,
                ["KUBUNO_PATHS_RUNTIME_DIR"] = layout.RuntimeDirectory,
                ["KUBUNO_PATHS_BACKUP_DIR"] = layout.BackupsDirectory,
                ["KUBUNO_PATHS_MODULES_STORE"] = layout.ModulesStoreDirectory,
                ["KUBUNO_PATHS_MODULES_CONFIG_DIR"] = layout.ModulesConfigDirectory,
                ["KUBUNO_PATHS_MODULES_DATA_DIR"] = layout.ModulesDataDirectory,
            };

            if (!string.IsNullOrEmpty(frontendDist))
            {
                environment["KV__SERVER__FRONTEND_DIST"] = frontendDist!;
            }

            return environment;
        }

        /// <summary>The names <see cref="Build"/> sets whose value must never be logged.</summary>
        public static readonly IReadOnlyCollection<string> SecretNames = new[]
        {
            "KV__DATABASE__URL", "KV__SERVER__INTERNAL_SECRET", "KV__AUTH__JWT_SECRET",
        };
    }
}
