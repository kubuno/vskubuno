using Kubuno.VisualStudio.Core.Migrations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Tests.Migrations
{
    /// <summary>The "Add Migration" dialog's validation: the same rules as the helper's <c>snake_description</c>.</summary>
    [TestClass]
    public sealed class MigrationDescriptionTests
    {
        [TestCleanup]
        public void Cleanup() => MigrationText.ForceFrench = null;

        [TestMethod]
        public void SnakeCaseLikeTheHelper()
        {
            Assert.AreEqual("create_customers_table", MigrationDescription.ToSnake("Create  customers-table"));
            Assert.AreEqual("add_vip_flag_2", MigrationDescription.ToSnake("add_VIP flag 2"));
            Assert.AreEqual("x", MigrationDescription.ToSnake("  x -- "));
        }

        [TestMethod]
        public void ValidDescriptions()
        {
            Assert.IsNull(MigrationDescription.Validate("create customers"));
            Assert.IsNull(MigrationDescription.Validate("  Add_VIP-flag 2  "));
            Assert.IsNull(MigrationDescription.Validate(new string('a', 100)));
        }

        [TestMethod]
        public void RefusedDescriptionsSayWhy()
        {
            MigrationText.ForceFrench = true;
            Assert.AreEqual(MigrationText.DescriptionEmpty, MigrationDescription.Validate(""));
            Assert.AreEqual(MigrationText.DescriptionEmpty, MigrationDescription.Validate("   "));
            Assert.AreEqual(MigrationText.DescriptionEmpty, MigrationDescription.Validate(null));
            Assert.AreEqual(MigrationText.DescriptionNeedsAlphanumeric, MigrationDescription.Validate("---"));
            Assert.AreEqual(MigrationText.DescriptionInvalidCharacters, MigrationDescription.Validate("drop; table"));
            Assert.AreEqual(MigrationText.DescriptionInvalidCharacters, MigrationDescription.Validate("créer clients"));
            Assert.AreEqual(MigrationText.DescriptionInvalidCharacters, MigrationDescription.Validate("a/b"));
            Assert.AreEqual(MigrationText.DescriptionTooLong, MigrationDescription.Validate(new string('x', 101)));
            StringAssert.Contains(MigrationText.DescriptionInvalidCharacters, "sans accents");
        }

        [TestMethod]
        public void FilesPreview()
        {
            Assert.AreEqual("<version>_create_customers.up.sql, <version>_create_customers.down.sql", MigrationDescription.FilesPreview("Create customers", reversible: true));
            Assert.AreEqual("<version>_create_customers.sql", MigrationDescription.FilesPreview("Create customers", reversible: false));
            Assert.AreEqual(string.Empty, MigrationDescription.FilesPreview("--", reversible: true));
        }
    }
}
