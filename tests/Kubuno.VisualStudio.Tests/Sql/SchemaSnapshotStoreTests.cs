using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Kubuno.VisualStudio.Core.Sql;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Tests.Sql
{
    [TestClass]
    public sealed class SchemaSnapshotStoreTests
    {
        private string _root = string.Empty;

        [TestInitialize]
        public void CreateRoot()
        {
            _root = Path.Combine(Path.GetTempPath(), "kubuno-sql-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TestCleanup]
        public void DeleteRoot()
        {
            try
            {
                Directory.Delete(_root, true);
            }
            catch (IOException)
            {
            }
        }

        [TestMethod]
        public void PathsAndFileNames()
        {
            Assert.AreEqual(Path.Combine(_root, ".vs", "kubuno", "schema"), SchemaSnapshotStore.GetSchemaDirectory(_root));
            Assert.AreEqual(Path.Combine(_root, ".vs", "kubuno", "schema", "Shop.json"), SchemaSnapshotStore.GetSnapshotPath(_root, "Shop"));
            Assert.AreEqual("My Shop-2.local", SchemaSnapshotStore.ToFileName("My Shop-2.local"));
            Assert.AreEqual("a_b_c", SchemaSnapshotStore.ToFileName("a/b:c"));
            Assert.AreEqual("_", SchemaSnapshotStore.ToFileName(".."));
            Assert.ThrowsExactly<ArgumentException>(() => SchemaSnapshotStore.ToFileName(" "));
        }

        [TestMethod]
        public void WriteReadListDelete()
        {
            Assert.IsNull(SchemaSnapshotStore.TryRead(_root, "Shop"));
            var path = SchemaSnapshotStore.Write(_root, "Shop", SqlFixtures.Json("Shop"));
            Assert.IsTrue(File.Exists(path));
            Assert.AreEqual(0, Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Length);

            var snapshot = SchemaSnapshotStore.TryRead(_root, "Shop");
            Assert.IsNotNull(snapshot);
            Assert.AreEqual("sqlite", snapshot!.Provider);
            Assert.AreEqual("shop.db", snapshot.Database);
            var customers = snapshot.Schemas.Single().Tables.First(t => t.Name == "customers");
            Assert.AreEqual(4, customers.Columns.Count);
            Assert.IsTrue(customers.FindColumn("ID")!.PrimaryKey);
            Assert.IsTrue(snapshot.Schemas.Single().Tables.Single(t => t.Name == "order_totals").IsView);

            // Overwriting replaces the file.
            SchemaSnapshotStore.Write(_root, "Shop", SqlFixtures.Json("Sales"));
            Assert.AreEqual("postgres", SchemaSnapshotStore.TryRead(_root, "Shop")!.Provider);
            CollectionAssert.AreEqual(new[] { "Shop" }, SchemaSnapshotStore.List(_root).ToArray());

            SchemaSnapshotStore.Delete(_root, "Shop");
            Assert.IsNull(SchemaSnapshotStore.TryRead(_root, "Shop"));
        }

        [TestMethod]
        public void InvalidJsonIsRejectedOnWriteAndIgnoredOnRead()
        {
            Assert.Throws<JsonException>(() => SchemaSnapshotStore.Write(_root, "Bad", "{not json"));
            Directory.CreateDirectory(SchemaSnapshotStore.GetSchemaDirectory(_root));
            File.WriteAllText(SchemaSnapshotStore.GetSnapshotPath(_root, "Bad"), "[1, 2]");
            Assert.IsNull(SchemaSnapshotStore.TryRead(_root, "Bad"));
        }

        [TestMethod]
        public void WriteRaisesChanged()
        {
            var seen = new ConcurrentQueue<SchemaSnapshotChangedEventArgs>();
            EventHandler<SchemaSnapshotChangedEventArgs> handler = (_, e) =>
            {
                if (e.Root.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
                {
                    seen.Enqueue(e);
                }
            };
            SchemaSnapshotStore.Changed += handler;
            try
            {
                SchemaSnapshotStore.Write(_root, "Shop", SqlFixtures.Json("Shop"));
            }
            finally
            {
                SchemaSnapshotStore.Changed -= handler;
            }

            Assert.IsTrue(seen.Any(e => e.Connection == "Shop" && e.Path == SchemaSnapshotStore.GetSnapshotPath(_root, "Shop")));
        }

        [TestMethod]
        public void TheWatcherSeesFilesWrittenByOthers()
        {
            using var signal = new ManualResetEventSlim();
            string? connection = null;
            EventHandler<SchemaSnapshotChangedEventArgs> handler = (_, e) =>
            {
                if (string.Equals(e.Root, _root, StringComparison.OrdinalIgnoreCase))
                {
                    connection = e.Connection;
                    signal.Set();
                }
            };
            SchemaSnapshotStore.Changed += handler;
            try
            {
                using (SchemaSnapshotStore.Watch(_root))
                {
                    Assert.IsTrue(Directory.Exists(SchemaSnapshotStore.GetSchemaDirectory(_root)), "the directory is created to be watched");
                    File.WriteAllText(SchemaSnapshotStore.GetSnapshotPath(_root, "Other"), SqlFixtures.Json("Shop"));
                    Assert.IsTrue(signal.Wait(TimeSpan.FromSeconds(10)), "no change event");
                }
            }
            finally
            {
                SchemaSnapshotStore.Changed -= handler;
            }

            Assert.AreEqual("Other", connection);
        }

        [TestMethod]
        public void RootResolution()
        {
            var crate = Path.Combine(_root, "apps", "shop");
            Directory.CreateDirectory(Path.Combine(crate, "src"));
            File.WriteAllText(Path.Combine(_root, "Cargo.toml"), "[workspace]\n");
            File.WriteAllText(Path.Combine(crate, "Cargo.toml"), "[package]\nname = \"shop\"\n");
            var file = Path.Combine(crate, "src", "main.rs");

            // A candidate (workspace/solution directory) that contains the file wins.
            Assert.AreEqual(_root, SchemaSnapshotStore.ResolveRoot(file, new[] { null, Path.Combine(_root, "elsewhere"), _root }));

            // Otherwise the nearest ancestor that already has snapshots...
            Directory.CreateDirectory(SchemaSnapshotStore.GetSchemaDirectory(crate));
            Assert.AreEqual(crate, SchemaSnapshotStore.ResolveRoot(file, null));
            Directory.Delete(Path.Combine(crate, ".vs"), true);

            // ...then the first candidate, then the topmost Cargo.toml directory.
            Assert.AreEqual(Path.Combine(_root, "elsewhere"), SchemaSnapshotStore.ResolveRoot(file, new[] { Path.Combine(_root, "elsewhere") }));
            Assert.AreEqual(_root, SchemaSnapshotStore.ResolveRoot(file, null));
        }

        [TestMethod]
        public void KbdataConnectionsOfACrateSelectItsSnapshots()
        {
            var crate = Path.Combine(_root, "shop");
            var data = Path.Combine(crate, "src", "data");
            Directory.CreateDirectory(data);
            File.WriteAllText(Path.Combine(crate, "Cargo.toml"), "[package]\nname = \"shop\"\n");
            File.WriteAllText(Path.Combine(data, "customers.kbdata"), "# Data source\nconnection = \"Shop\"\nprovider = \"sqlite\"\n\n[tables.customers]\nschema = \"main\"\n");
            File.WriteAllText(Path.Combine(data, "orders.kbdata"), "connection = \"Shop\"\n");
            File.WriteAllText(Path.Combine(crate, "src", "sales.kbdata"), "connection = \"Sales\"\nprovider = \"postgres\"\n");
            File.WriteAllText(Path.Combine(crate, "src", "broken.kbdata"), "connection = \n");

            Assert.AreEqual(crate, KbdataConnections.FindCrateDirectory(Path.Combine(crate, "src", "main.rs")));
            var connections = KbdataConnections.Read(crate);
            CollectionAssert.AreEquivalent(new[] { "Shop", "Sales" }, connections.Select(c => c.Connection).ToArray());
            Assert.AreEqual("sqlite", connections.Single(c => c.Connection == "Shop").Provider);

            // No snapshot yet: an empty index (keywords only), then the merged snapshots.
            Assert.IsTrue(SqlSchemaSource.Load(crate, _root).IsEmpty);
            SchemaSnapshotStore.Write(_root, "Shop", SqlFixtures.Json("Shop"));
            var shopOnly = SqlSchemaSource.Load(crate, _root);
            Assert.AreEqual("Shop", shopOnly.ConnectionLabel);
            SchemaSnapshotStore.Write(_root, "Sales", SqlFixtures.Json("Sales"));
            SchemaSnapshotStore.Write(_root, "Unrelated", SqlFixtures.Json("Sales"));
            var both = SqlSchemaSource.Load(crate, _root);
            Assert.AreEqual(2, both.Snapshots.Count);
            Assert.IsNotNull(both.FindTable(null, "customers"));
            Assert.IsNotNull(both.FindTable("billing", "invoices"));
        }

        [TestMethod]
        public void TheSharedSourceReloadsWhenASnapshotChanges()
        {
            var crate = Path.Combine(_root, "shop");
            Directory.CreateDirectory(Path.Combine(crate, "src"));
            File.WriteAllText(Path.Combine(crate, "Cargo.toml"), "[package]\nname = \"shop\"\n");
            File.WriteAllText(Path.Combine(crate, "src", "shop.kbdata"), "connection = \"Shop\"\n");

            var source = SqlSchemaSource.GetOrCreate(crate, _root);
            try
            {
                Assert.AreSame(source, SqlSchemaSource.GetOrCreate(crate, _root));
                source.LoadNow();
                Assert.IsTrue(source.Index.IsEmpty);

                using var reloaded = new ManualResetEventSlim();
                source.Changed += (_, _) =>
                {
                    if (!source.Index.IsEmpty)
                    {
                        reloaded.Set();
                    }
                };
                SchemaSnapshotStore.Write(_root, "Shop", SqlFixtures.Json("Shop"));
                Assert.IsTrue(reloaded.Wait(TimeSpan.FromSeconds(10)), "no reload after the snapshot was written");
                Assert.IsNotNull(source.Index.FindTable(null, "orders"));
            }
            finally
            {
                source.Dispose();
            }
        }
    }
}
