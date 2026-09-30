using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kubuno.Desktop.Logic.Migrations
{
    /// <summary>The state of one migration in Solution Explorer.</summary>
    public enum MigrationState
    {
        Applied,
        Pending,

        /// <summary>Applied, but the file changed since (sqlx refuses to run until it is resolved).</summary>
        ChecksumMismatch,

        /// <summary>A row with <c>success = false</c>.</summary>
        Failed,

        /// <summary>Applied, but the file is gone.</summary>
        Missing,
    }

    /// <summary>What a child of the <c>Migrations</c> node shows.</summary>
    public enum MigrationsNodeKind
    {
        Migration,

        /// <summary>The SQLx offline cache state.</summary>
        SqlxCache,

        /// <summary>Loading / no migration / status unavailable.</summary>
        Message,
    }

    /// <summary>One child of the <c>Migrations</c> node (pure data; the VS tree item renders it).</summary>
    public sealed class MigrationsNode
    {
        public MigrationsNode(MigrationsNodeKind kind, string key, string text, string toolTip, string monikerName)
        {
            Kind = kind;
            Key = key;
            Text = text;
            ToolTip = toolTip;
            MonikerName = monikerName;
        }

        public MigrationsNodeKind Kind { get; }

        /// <summary>Identity across refreshes (the tree is merged in place).</summary>
        public string Key { get; }

        public string Text { get; }

        public string ToolTip { get; }

        /// <summary>A <c>KnownMonikers</c> property name.</summary>
        public string MonikerName { get; }

        public MigrationInfo? Migration { get; set; }

        public MigrationState? State { get; set; }

        /// <summary>The file a double-click opens (the up file), or null.</summary>
        public string? FilePath { get; set; }

        /// <summary>Only for <see cref="MigrationsNodeKind.SqlxCache"/>.</summary>
        public bool SqlxStale { get; set; }
    }

    /// <summary>
    /// The view model of a crate's <c>Migrations</c> node (docs/DATA.md DATA-7): built from <c>migrate.status</c> (or the
    /// error it failed with) and <c>sqlx.status</c>; migrations in version order, each with its state as text and icon,
    /// then the SQLx cache node. Pure: no Visual Studio dependency, unit-tested with JSON fixtures.
    /// </summary>
    public sealed class MigrationsTreeModel
    {
        public const string MonikerApplied = "StatusOK";
        public const string MonikerPending = "Time";
        public const string MonikerChecksum = "StatusWarning";
        public const string MonikerFailed = "StatusError";
        public const string MonikerMissing = "StatusInvalid";
        public const string MonikerRoot = "DatabaseScript";
        public const string MonikerRootWarning = "DatabaseWarning";
        public const string MonikerSqlxFresh = "SQLQueryChecked";
        public const string MonikerSqlxStale = "StatusWarning";
        public const string MonikerLoading = "Loading";
        public const string MonikerError = "StatusError";
        public const string MonikerInformation = "StatusInformation";

        private MigrationsTreeModel(string rootText, string rootMoniker, IReadOnlyList<MigrationsNode> children, MigrationStatusResult? status, SqlxCacheStatus? cache)
        {
            RootText = rootText;
            RootMonikerName = rootMoniker;
            Children = children;
            Status = status;
            Cache = cache;
        }

        public string RootText { get; }

        public string RootMonikerName { get; }

        public IReadOnlyList<MigrationsNode> Children { get; }

        /// <summary>The status it was built from (null when unavailable).</summary>
        public MigrationStatusResult? Status { get; }

        public SqlxCacheStatus? Cache { get; }

        public int PendingCount => Status?.Migrations.Count(m => !m.Applied && !m.Missing) ?? 0;

        public bool SqlxStale => Cache?.Stale == true;

        /// <summary>The model while the first status is being read.</summary>
        public static MigrationsTreeModel Loading() => new MigrationsTreeModel(
            MigrationText.MigrationsNode,
            MonikerRoot,
            new[] { new MigrationsNode(MigrationsNodeKind.Message, "loading", MigrationText.Loading, MigrationText.Loading, MonikerLoading) },
            null,
            null);

        /// <summary>
        /// Builds the model. <paramref name="statusError"/> (a connection error, no connection...) replaces the migrations by
        /// one "Status unavailable: ..." node; <paramref name="cache"/> adds the SQLx cache node when it matters (a cache
        /// exists or is needed). <paramref name="timeZone"/> is the zone applied dates are shown in (local by default).
        /// </summary>
        public static MigrationsTreeModel Build(MigrationStatusResult? status, string? statusError, SqlxCacheStatus? cache, TimeZoneInfo? timeZone = null)
        {
            var zone = timeZone ?? TimeZoneInfo.Local;
            var children = new List<MigrationsNode>();
            bool warning = false;
            string rootText = MigrationText.MigrationsNode;

            if (status is null || statusError != null)
            {
                warning = true;
                var message = FirstLine(statusError ?? MigrationText.NoConnection);
                children.Add(new MigrationsNode(MigrationsNodeKind.Message, "unavailable", MigrationText.Unavailable(message), statusError ?? message, MonikerError));
                status = null;
            }
            else
            {
                var ordered = status.Migrations.OrderBy(m => m.Version).ToList();
                foreach (var migration in ordered)
                {
                    var node = MigrationNode(migration, zone);
                    warning |= node.State is MigrationState.ChecksumMismatch or MigrationState.Failed or MigrationState.Missing;
                    children.Add(node);
                }

                if (ordered.Count == 0)
                {
                    children.Add(new MigrationsNode(MigrationsNodeKind.Message, "empty", MigrationText.NoMigrations, MigrationText.NoMigrations, MonikerInformation));
                }

                rootText = MigrationText.MigrationsNodePending(ordered.Count(m => !m.Applied && !m.Missing));
            }

            if (cache != null && (cache.Stale || cache.QueryFiles > 0))
            {
                var text = cache.Stale ? MigrationText.SqlxStale(SqlxReason(cache.Reason)) : MigrationText.SqlxUpToDate(cache.QueryFiles);
                var toolTip = cache.Stale && !string.IsNullOrEmpty(cache.Reason) ? cache.Reason + Environment.NewLine + Environment.NewLine + MigrationText.SqlxToolTip : MigrationText.SqlxToolTip;
                children.Add(new MigrationsNode(MigrationsNodeKind.SqlxCache, "sqlx", text, toolTip, cache.Stale ? MonikerSqlxStale : MonikerSqlxFresh) { SqlxStale = cache.Stale });
                warning |= cache.Stale;
            }

            return new MigrationsTreeModel(rootText, warning ? MonikerRootWarning : MonikerRoot, children, status, cache);
        }

        /// <summary>The state of a migration.</summary>
        public static MigrationState StateOf(MigrationInfo migration)
        {
            if (migration.Dirty)
            {
                return MigrationState.Failed;
            }

            if (migration.Missing)
            {
                return MigrationState.Missing;
            }

            if (!migration.Applied)
            {
                return MigrationState.Pending;
            }

            return migration.ChecksumMatches ? MigrationState.Applied : MigrationState.ChecksumMismatch;
        }

        /// <summary><c>2026-09-30T12:00:01Z</c> in <paramref name="zone"/>, short date and time of the UI culture; the raw text when unparsable.</summary>
        public static string FormatAppliedAt(string? appliedAt, TimeZoneInfo zone)
        {
            if (string.IsNullOrWhiteSpace(appliedAt))
            {
                return string.Empty;
            }

            if (DateTimeOffset.TryParse(appliedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var when))
            {
                var local = TimeZoneInfo.ConvertTime(when, zone);
                return local.ToString("g", MigrationText.Culture);
            }

            return appliedAt!;
        }

        /// <summary>
        /// The helper's (English) stale reason, short and localized for the node text: "no .sqlx cache", "older than
        /// `x`"; anything else up to its first ':'.
        /// </summary>
        public static string SqlxReason(string? reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return MigrationText.T("regenerate it", "à régénérer");
            }

            var older = Regex.Match(reason, "older than (?:the migration )?`([^`]+)`");
            if (older.Success)
            {
                return MigrationText.T($"older than {older.Groups[1].Value}", $"plus ancien que {older.Groups[1].Value}");
            }

            var missing = Regex.Match(reason, "no offline query cache \\(\\.sqlx\\) although `([^`]+)` needs one");
            if (missing.Success)
            {
                return MigrationText.T($"no .sqlx cache although {missing.Groups[1].Value} needs one", $"aucun cache .sqlx alors que {missing.Groups[1].Value} en a besoin");
            }

            if (reason!.IndexOf("has no entry for", StringComparison.Ordinal) >= 0)
            {
                return MigrationText.T("queries missing from the cache", "requêtes absentes du cache");
            }

            var colon = reason.IndexOf(": ", StringComparison.Ordinal);
            return colon > 0 ? reason.Substring(0, colon) : reason;
        }

        private static MigrationsNode MigrationNode(MigrationInfo migration, TimeZoneInfo zone)
        {
            var state = StateOf(migration);
            string stateText;
            string moniker;
            string extra = string.Empty;
            switch (state)
            {
                case MigrationState.Applied:
                    var when = FormatAppliedAt(migration.AppliedAt, zone);
                    stateText = when.Length > 0 ? MigrationText.StateAppliedAt(when) : MigrationText.StateApplied;
                    moniker = MonikerApplied;
                    break;
                case MigrationState.ChecksumMismatch:
                    stateText = MigrationText.StateChecksumMismatch;
                    moniker = MonikerChecksum;
                    extra = MigrationText.ChecksumToolTip;
                    break;
                case MigrationState.Failed:
                    stateText = MigrationText.StateFailed;
                    moniker = MonikerFailed;
                    extra = MigrationText.FailedToolTip;
                    break;
                case MigrationState.Missing:
                    stateText = MigrationText.StateMissing;
                    moniker = MonikerMissing;
                    extra = MigrationText.MissingToolTip;
                    break;
                default:
                    stateText = MigrationText.StatePending;
                    moniker = MonikerPending;
                    break;
            }

            var toolTip = migration.Label + Environment.NewLine + stateText;
            if (extra.Length > 0)
            {
                toolTip += Environment.NewLine + extra;
            }

            if (!migration.Reversible && !migration.Missing)
            {
                toolTip += Environment.NewLine + MigrationText.NotReversible;
            }

            if (!string.IsNullOrEmpty(migration.File))
            {
                toolTip += Environment.NewLine + migration.File;
            }

            return new MigrationsNode(MigrationsNodeKind.Migration, "m|" + migration.Version.ToString(CultureInfo.InvariantCulture), migration.Label + " — " + stateText, toolTip, moniker)
            {
                Migration = migration,
                State = state,
                FilePath = migration.File,
            };
        }

        private static string FirstLine(string message)
        {
            var line = message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(l => l.Trim().Length > 0) ?? message;
            return line.Length > 200 ? line.Substring(0, 200) + "..." : line;
        }
    }
}
