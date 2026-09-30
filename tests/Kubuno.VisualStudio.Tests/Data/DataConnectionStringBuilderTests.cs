using System;
using System.Linq;
using Kubuno.VisualStudio.Core.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Tests.Data
{
    [TestClass]
    public sealed class DataConnectionStringBuilderTests
    {
        private const string NastyPassword = " p;a'ss\"w=rd{secret} ";

        [TestCleanup]
        public void Cleanup() => DataText.ForceFrench = null;

        [TestMethod]
        public void PostgresUsesTheKeyValueFormAndOmitsTheDefaultPort()
        {
            var settings = new DataConnectionSettings { Provider = DataProviderKind.Postgres, Server = "db.example", Port = 5432, Database = "shop", User = "kubuno" };

            Assert.AreEqual("Host=db.example;Database=shop;Username=kubuno;Password=s3cret;", DataConnectionStringBuilder.Build(settings, "s3cret"));

            settings.Port = 5433;
            settings.SslMode = "verify-full";
            Assert.AreEqual("Host=db.example;Port=5433;Database=shop;Username=kubuno;Password=s3cret;SslMode=verify-full;", DataConnectionStringBuilder.Build(settings, "s3cret"));
        }

        [TestMethod]
        public void DefaultSslModeIsLeftToKubunoData()
        {
            var settings = new DataConnectionSettings { Provider = DataProviderKind.Postgres, Server = "remote.example", User = "u" };

            Assert.IsFalse(DataConnectionStringBuilder.Build(settings, null).Contains("SslMode"), "unset = kubuno-data requires TLS for a remote host");
            Assert.AreEqual(string.Empty, DataConnectionStringBuilder.SslModes(DataProviderKind.Postgres)[0].Value);
            CollectionAssert.IsSubsetOf(new[] { "disable", "prefer", "require", "verify-full" }, DataConnectionStringBuilder.SslModes(DataProviderKind.Postgres).Select(m => m.Value).ToArray());
        }

        [TestMethod]
        public void SpecialCharactersInThePasswordAreQuotedTheWayKubunoDataParsesThem()
        {
            var settings = new DataConnectionSettings { Provider = DataProviderKind.Postgres, Server = "localhost", User = "app" };

            string built = DataConnectionStringBuilder.Build(settings, NastyPassword);

            Assert.AreEqual("Host=localhost;Username=app;Password=' p;a''ss\"w=rd{secret} ';", built);
            Assert.AreEqual(NastyPassword, ParsePassword(built), "round trip through kubuno-data's parsing rules");
        }

        [TestMethod]
        public void DisplayMasksThePassword()
        {
            var settings = new DataConnectionSettings { Provider = DataProviderKind.MySql, Server = "localhost", Database = "shop", User = "root" };

            string shown = DataConnectionStringBuilder.Display(settings, hasPassword: true);

            Assert.AreEqual("Server=localhost;Database=shop;User Id=root;Password=***;", shown);
            Assert.AreEqual("Server=localhost;Database=shop;User Id=root;", DataConnectionStringBuilder.Display(settings, hasPassword: false));
        }

        [TestMethod]
        public void MySqlUsesItsOwnSslModeNames()
        {
            var settings = new DataConnectionSettings { Provider = DataProviderKind.MySql, Server = "db", Port = 3307, User = "u", SslMode = "REQUIRED" };

            Assert.AreEqual("Server=db;Port=3307;User Id=u;Password=x;SslMode=REQUIRED;", DataConnectionStringBuilder.Build(settings, "x"));
        }

        [TestMethod]
        public void SqlServerIntegratedSecurityHasNoUserNorPassword()
        {
            var settings = new DataConnectionSettings { Provider = DataProviderKind.SqlServer, Server = @".\SQLEXPRESS", Database = "Shop", IntegratedSecurity = true, User = "ignored", TrustServerCertificate = true };

            Assert.AreEqual(@"Server=.\SQLEXPRESS;Database=Shop;Integrated Security=true;TrustServerCertificate=true;", DataConnectionStringBuilder.Build(settings, "ignored"));
            Assert.AreEqual(0, DataConnectionStringBuilder.Validate(settings, hasPassword: false).Count);
        }

        [TestMethod]
        public void SqlServerLoginWithPortAndEncryption()
        {
            var settings = new DataConnectionSettings { Provider = DataProviderKind.SqlServer, Server = "sql.example", Port = 14330, Database = "Shop", User = "sa", Encrypt = "true" };

            Assert.AreEqual("Server=tcp:sql.example,14330;Database=Shop;User Id=sa;Password='a;b';Encrypt=true;", DataConnectionStringBuilder.Build(settings, "a;b"));
        }

        [TestMethod]
        public void SqliteIsAUrlWithTheCreateMode()
        {
            var settings = new DataConnectionSettings { Provider = DataProviderKind.Sqlite, FilePath = @"C:\data\shop 100%?#.db", CreateIfMissing = true };

            Assert.AreEqual(@"sqlite:C:\data\shop 100%25%3F%23.db?mode=rwc", DataConnectionStringBuilder.Build(settings, null));
            settings.CreateIfMissing = false;
            Assert.AreEqual(@"sqlite:C:\data\shop 100%25%3F%23.db?mode=rw", DataConnectionStringBuilder.Display(settings, hasPassword: false));
        }

        [TestMethod]
        public void CreateIfMissingCreatesAnEmptyDatabaseFileOnce()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kubuno-data-" + Guid.NewGuid().ToString("N"));
            try
            {
                var settings = new DataConnectionSettings { Provider = DataProviderKind.Sqlite, FilePath = System.IO.Path.Combine(dir, "sub", "shop.db"), CreateIfMissing = true };

                Assert.IsTrue(DataConnectionStringBuilder.EnsureSqliteFile(settings));
                Assert.AreEqual(0, new System.IO.FileInfo(settings.FilePath).Length, "an empty file is an empty SQLite database");
                Assert.IsFalse(DataConnectionStringBuilder.EnsureSqliteFile(settings), "an existing file is left alone");
                settings.CreateIfMissing = false;
                settings.FilePath = System.IO.Path.Combine(dir, "other.db");
                Assert.IsFalse(DataConnectionStringBuilder.EnsureSqliteFile(settings));
                Assert.IsFalse(System.IO.File.Exists(settings.FilePath));
            }
            finally
            {
                if (System.IO.Directory.Exists(dir))
                {
                    System.IO.Directory.Delete(dir, recursive: true);
                }
            }
        }

        [TestMethod]
        public void ValidationListsTheMissingFields()
        {
            DataText.ForceFrench = false;
            Assert.AreEqual(1, DataConnectionStringBuilder.Validate(new DataConnectionSettings { Provider = DataProviderKind.Sqlite }, false).Count);
            var errors = DataConnectionStringBuilder.Validate(new DataConnectionSettings { Provider = DataProviderKind.Postgres, Port = 70000 }, false);
            CollectionAssert.AreEquivalent(new[] { DataText.ServerRequired, DataText.InvalidPort, DataText.UserRequired }, errors.ToArray());
        }

        [TestMethod]
        public void ConnectionNamesFollowTheToolsRule()
        {
            Assert.IsTrue(DataConnectionStringBuilder.IsValidConnectionName("Shop dev-1.0_a"));
            Assert.IsFalse(DataConnectionStringBuilder.IsValidConnectionName(""));
            Assert.IsFalse(DataConnectionStringBuilder.IsValidConnectionName(" Shop"));
            Assert.IsFalse(DataConnectionStringBuilder.IsValidConnectionName("Shop:1"));
            Assert.IsFalse(DataConnectionStringBuilder.IsValidConnectionName("Boutique é"));
            Assert.IsFalse(DataConnectionStringBuilder.IsValidConnectionName(new string('a', 65)));
        }

        [TestMethod]
        public void TheDefaultPortsAreTheProvidersOnes()
        {
            Assert.AreEqual(5432, DataConnectionStringBuilder.DefaultPort(DataProviderKind.Postgres));
            Assert.AreEqual(3306, DataConnectionStringBuilder.DefaultPort(DataProviderKind.MySql));
            Assert.AreEqual(1433, DataConnectionStringBuilder.DefaultPort(DataProviderKind.SqlServer));
        }

        /// <summary>A C# port of conn_string.rs's value parsing for <c>Password</c> (quotes, doubled quotes).</summary>
        private static string? ParsePassword(string text)
        {
            int i = 0;
            while (i < text.Length)
            {
                int eq = text.IndexOf('=', i);
                string key = text.Substring(i, eq - i).Trim();
                i = eq + 1;
                while (i < text.Length && text[i] == ' ')
                {
                    i++;
                }

                var value = new System.Text.StringBuilder();
                if (i < text.Length && (text[i] == '\'' || text[i] == '"'))
                {
                    char q = text[i++];
                    while (i < text.Length)
                    {
                        if (text[i] == q)
                        {
                            if (i + 1 < text.Length && text[i + 1] == q)
                            {
                                value.Append(q);
                                i += 2;
                                continue;
                            }

                            i++;
                            break;
                        }

                        value.Append(text[i++]);
                    }

                    while (i < text.Length && text[i] != ';')
                    {
                        i++;
                    }
                }
                else
                {
                    while (i < text.Length && text[i] != ';')
                    {
                        value.Append(text[i++]);
                    }
                }

                i++;
                if (string.Equals(key, "Password", StringComparison.OrdinalIgnoreCase))
                {
                    return value.ToString();
                }
            }

            return null;
        }
    }
}
