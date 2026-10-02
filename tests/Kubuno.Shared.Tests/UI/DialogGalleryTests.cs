using System;
using System.Collections.Generic;
using System.Linq;
using Kubuno.Core.UI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Core.Tests.UI
{
    [TestClass]
    public sealed class DialogGalleryTests
    {
        [TestMethod]
        public void A_registered_provider_runs_only_when_the_gallery_reads_its_entries()
        {
            var name = "Lazy " + Guid.NewGuid();
            var calls = 0;
            DialogGallery.Register(() =>
            {
                calls++;
                return new[] { new KeyValuePair<string, Func<bool?>>(name, () => true) };
            });

            // Registration happens during the package load: building the entries must wait for the gallery.
            Assert.AreEqual(0, calls);

            Assert.AreEqual(1, DialogGallery.Entries.Count(e => e.Key == name));
            Assert.AreEqual(1, calls);
        }

        [TestMethod]
        public void Entries_keep_the_registration_order()
        {
            var first = "First " + Guid.NewGuid();
            var second = "Second " + Guid.NewGuid();
            DialogGallery.Register(first, () => null);
            DialogGallery.Register(() => new[] { new KeyValuePair<string, Func<bool?>>(second, () => null) });

            var names = DialogGallery.Entries.Select(e => e.Key).ToList();
            Assert.IsTrue(names.IndexOf(first) >= 0 && names.IndexOf(first) < names.IndexOf(second));
        }
    }
}
