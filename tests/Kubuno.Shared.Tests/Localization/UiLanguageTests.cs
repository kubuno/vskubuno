using System.Globalization;
using System.Threading;
using Kubuno.Core.Logic.Localization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Core.Tests.Localization
{
    [TestClass]
    public sealed class UiLanguageTests
    {
        [TestMethod]
        public void The_language_follows_the_UI_culture_unless_forced()
        {
            var culture = Thread.CurrentThread.CurrentUICulture;
            try
            {
                UiLanguage.ForceFrench = null;
                Thread.CurrentThread.CurrentUICulture = new CultureInfo("fr-FR");
                Assert.IsTrue(UiLanguage.IsFrench);
                Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
                Assert.IsFalse(UiLanguage.IsFrench);

                UiLanguage.ForceFrench = true;
                Assert.IsTrue(UiLanguage.IsFrench);
            }
            finally
            {
                UiLanguage.ForceFrench = null;
                Thread.CurrentThread.CurrentUICulture = culture;
            }
        }
    }
}
