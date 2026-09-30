using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Kubuno.VisualStudio.Core.Data;
using Kubuno.VisualStudio.Core.Migrations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Tests.Migrations
{
    [TestClass]
    public sealed class MigrationsTreeModelTests
    {
        [TestCleanup]
        public void Cleanup() => MigrationText.ForceFrench = null;

        private static MigrationStatusResult Fixture()
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Migrations", "Fixtures", "migrate-status.json")));
            return DataToolJson.Parse<MigrationStatusResult>(document.RootElement.Clone());
        }

        [TestMethod]
        public void TheFixtureParses()
        {
            var status = Fixture();
            Assert.AreEqual(3, status.Migrations.Count);
            Assert.AreEqual(1, status.PendingCount);
            var first = status.Migrations[0];
            Assert.AreEqual(20260930044040L, first.Version);
            Assert.AreEqual("create customers", first.Description);
            Assert.IsTrue(first.Applied);
            Assert.IsFalse(first.ChecksumMatches);
            Assert.AreEqual("2026-09-30T04:40:51Z", first.AppliedAt);
            Assert.AreEqual(@"C:\src\shop\migrations\20260930044040_create_customers.up.sql", first.File);
            Assert.IsFalse(status.Migrations[2].Reversible);
            Assert.IsNull(status.Migrations[2].AppliedAt);
            Assert.AreEqual(20260930044041L, status.LastApplied!.Version);
            Assert.AreEqual("20260930044040 create customers", first.Label);
        }

        [TestMethod]
        public void MigrationsAreOrderedByVersionWithTheirStatesInFrench()
        {
            MigrationText.ForceFrench = true;
            var status = Fixture();
            status.Migrations.Reverse();
            var model = MigrationsTreeModel.Build(status, null, new SqlxCacheStatus { Stale = false, QueryFiles = 3 }, TimeZoneInfo.Utc);

            Assert.AreEqual("Migrations (1 en attente)", model.RootText);
            Assert.AreEqual(MigrationsTreeModel.MonikerRootWarning, model.RootMonikerName, "a changed checksum flags the root");
            Assert.AreEqual(4, model.Children.Count);

            var customers = model.Children[0];
            Assert.AreEqual(MigrationsNodeKind.Migration, customers.Kind);
            Assert.AreEqual(MigrationState.ChecksumMismatch, customers.State);
            Assert.AreEqual("20260930044040 create customers \u2014 Somme de contrôle modifiée", customers.Text);
            Assert.AreEqual(MigrationsTreeModel.MonikerChecksum, customers.MonikerName);
            StringAssert.Contains(customers.ToolTip, "réappliquez");

            var orders = model.Children[1];
            Assert.AreEqual(MigrationState.Applied, orders.State);
            Assert.AreEqual("20260930044041 create orders \u2014 Appliquée le 30/09/2026 04:40", orders.Text);
            Assert.AreEqual(MigrationsTreeModel.MonikerApplied, orders.MonikerName);
            Assert.AreEqual(@"C:\src\shop\migrations\20260930044041_create_orders.up.sql", orders.FilePath);

            var vip = model.Children[2];
            Assert.AreEqual(MigrationState.Pending, vip.State);
            Assert.AreEqual("20260930044042 add vip flag \u2014 En attente", vip.Text);
            Assert.AreEqual(MigrationsTreeModel.MonikerPending, vip.MonikerName);
            StringAssert.Contains(vip.ToolTip, "pas de .down.sql");

            var cache = model.Children[3];
            Assert.AreEqual(MigrationsNodeKind.SqlxCache, cache.Kind);
            Assert.AreEqual("Cache SQLx : à jour (3 requêtes)", cache.Text);
            Assert.AreEqual(MigrationsTreeModel.MonikerSqlxFresh, cache.MonikerName);
            Assert.AreEqual(1, model.PendingCount);
            Assert.IsFalse(model.SqlxStale);
        }

        [TestMethod]
        public void EnglishTexts()
        {
            MigrationText.ForceFrench = false;
            var model = MigrationsTreeModel.Build(Fixture(), null, new SqlxCacheStatus { Stale = true, Reason = "the offline query cache (.sqlx) is older than `20260930044042_add_vip_flag.sql`: run `cargo sqlx prepare` (Visual Studio: Update the SQLx cache)", QueryFiles = 1 }, TimeZoneInfo.Utc);

            Assert.AreEqual("Migrations (1 pending)", model.RootText);
            Assert.AreEqual("20260930044040 create customers \u2014 Checksum changed", model.Children[0].Text);
            Assert.AreEqual("20260930044041 create orders \u2014 Applied 9/30/2026 4:40 AM", model.Children[1].Text);
            Assert.AreEqual("20260930044042 add vip flag \u2014 Pending", model.Children[2].Text);
            Assert.AreEqual("SQLx cache: stale (older than 20260930044042_add_vip_flag.sql)", model.Children[3].Text);
            Assert.AreEqual(MigrationsTreeModel.MonikerSqlxStale, model.Children[3].MonikerName);
            Assert.IsTrue(model.Children[3].SqlxStale);
            Assert.IsTrue(model.SqlxStale);
            StringAssert.StartsWith(model.Children[3].ToolTip, "the offline query cache");
        }

        [TestMethod]
        public void AllAppliedAndFreshIsNotAWarning()
        {
            MigrationText.ForceFrench = true;
            var status = Fixture();
            status.Migrations.RemoveAt(2);
            status.Migrations[0].ChecksumMatches = true;
            var model = MigrationsTreeModel.Build(status, null, null, TimeZoneInfo.Utc);

            Assert.AreEqual("Migrations", model.RootText);
            Assert.AreEqual(MigrationsTreeModel.MonikerRoot, model.RootMonikerName);
            Assert.AreEqual(2, model.Children.Count, "no cache node without a cache status");
            Assert.IsTrue(model.Children.All(c => c.State == MigrationState.Applied));
        }

        [TestMethod]
        public void FailedAndMissingMigrations()
        {
            MigrationText.ForceFrench = true;
            var status = new MigrationStatusResult();
            status.Migrations.Add(new MigrationInfo { Version = 2, Description = "b", Applied = false, Dirty = true, Reversible = true, File = @"C:\m\2_b.up.sql" });
            status.Migrations.Add(new MigrationInfo { Version = 1, Description = "a", Applied = true, AppliedAt = "2026-01-02T03:04:05Z", ChecksumMatches = false, Missing = true });
            var model = MigrationsTreeModel.Build(status, null, null, TimeZoneInfo.Utc);

            Assert.AreEqual(MigrationState.Missing, model.Children[0].State);
            Assert.AreEqual("1 a \u2014 Appliquée, fichier absent", model.Children[0].Text);
            Assert.IsNull(model.Children[0].FilePath);
            Assert.AreEqual(MigrationState.Failed, model.Children[1].State);
            Assert.AreEqual(MigrationsTreeModel.MonikerFailed, model.Children[1].MonikerName);
            Assert.AreEqual("Migrations (1 en attente)", model.RootText, "a dirty migration is not applied");
        }

        [TestMethod]
        public void AConnectionErrorShowsOneNodeAndTheCache()
        {
            MigrationText.ForceFrench = true;
            var model = MigrationsTreeModel.Build(null, "Secret: ConnectionStrings:Shop was not found (user secrets mig-live-check)\nmore", new SqlxCacheStatus { Stale = true, Reason = "no offline query cache (.sqlx) although `shop.kbdata` needs one: run `cargo sqlx prepare`" });

            Assert.AreEqual(2, model.Children.Count);
            Assert.AreEqual(MigrationsNodeKind.Message, model.Children[0].Kind);
            Assert.AreEqual("État indisponible : Secret: ConnectionStrings:Shop was not found (user secrets mig-live-check)", model.Children[0].Text);
            Assert.AreEqual(MigrationsTreeModel.MonikerError, model.Children[0].MonikerName);
            Assert.AreEqual("Cache SQLx : périmé (aucun cache .sqlx alors que shop.kbdata en a besoin)", model.Children[1].Text);
            Assert.AreEqual("Migrations", model.RootText);
            Assert.IsNull(model.Status);
        }

        [TestMethod]
        public void NoMigrationsAndNoCache()
        {
            MigrationText.ForceFrench = false;
            var model = MigrationsTreeModel.Build(new MigrationStatusResult(), null, new SqlxCacheStatus { Stale = false, QueryFiles = 0 });
            Assert.AreEqual(1, model.Children.Count, "an empty, fresh cache is not shown");
            Assert.AreEqual(MigrationText.NoMigrations, model.Children[0].Text);
            Assert.AreEqual("Migrations", model.RootText);

            var loading = MigrationsTreeModel.Loading();
            Assert.AreEqual(MigrationsTreeModel.MonikerLoading, loading.Children.Single().MonikerName);
        }

        [TestMethod]
        public void SqlxReasons()
        {
            MigrationText.ForceFrench = true;
            Assert.AreEqual("plus ancien que shop.kbdata", MigrationsTreeModel.SqlxReason("the offline query cache (.sqlx) is older than `shop.kbdata`: run `cargo sqlx prepare` (Visual Studio: Update the SQLx cache)"));
            Assert.AreEqual("plus ancien que 1_a.sql", MigrationsTreeModel.SqlxReason("the offline query cache (.sqlx) is older than the migration `1_a.sql`: regenerate it"));
            Assert.AreEqual("requêtes absentes du cache", MigrationsTreeModel.SqlxReason("the offline query cache (.sqlx) has no entry for customers.fill: regenerate it"));
            Assert.AreEqual("something else", MigrationsTreeModel.SqlxReason("something else: details"));
            Assert.AreEqual("à régénérer", MigrationsTreeModel.SqlxReason(null));
        }

        [TestMethod]
        public void AppliedDatesUseTheZoneAndCulture()
        {
            MigrationText.ForceFrench = true;
            var paris = TimeZoneInfo.CreateCustomTimeZone("UTC+2", TimeSpan.FromHours(2), "UTC+2", "UTC+2");
            Assert.AreEqual("30/09/2026 14:00", MigrationsTreeModel.FormatAppliedAt("2026-09-30T12:00:01Z", paris));
            Assert.AreEqual("not a date", MigrationsTreeModel.FormatAppliedAt("not a date", paris));
            Assert.AreEqual(string.Empty, MigrationsTreeModel.FormatAppliedAt(null, paris));
        }
    }
}
