using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Launch.Tests
{
    [TestClass]
    public sealed class PaintDebugTests
    {
        [TestMethod]
        public void FlagsFor_MapsCheckedStateToBitmask()
        {
            Assert.AreEqual(7, PaintDebug.FlagsFor(true));
            Assert.AreEqual(0, PaintDebug.FlagsFor(false));
        }

        [TestMethod]
        [DataRow(0, null)]
        [DataRow(7, "all")]
        [DataRow(1, "invalidate")]
        [DataRow(2, "layout")]
        [DataRow(4, "fps")]
        [DataRow(5, "invalidate,fps")]
        [DataRow(6, "layout,fps")]
        [DataRow(15, "all")]
        public void ToEnvironmentValue_ComputesRuntimeString(int flags, string? expected)
        {
            Assert.AreEqual(expected, PaintDebug.ToEnvironmentValue(flags));
        }

        [TestMethod]
        public void MergeInto_AddsVariableWhenOnAndNoUserValue()
        {
            var merged = PaintDebug.MergeInto(new Dictionary<string, string> { ["PATH"] = "x" }, 7, null);
            Assert.AreEqual("all", merged["KUBUNO_PAINT_DEBUG"]);
            Assert.AreEqual("x", merged["PATH"]);
        }

        [TestMethod]
        public void MergeInto_UserProfileValueWins()
        {
            var merged = PaintDebug.MergeInto(new Dictionary<string, string> { ["kubuno_paint_debug"] = "layout" }, 7, null);
            Assert.AreEqual("layout", merged["KUBUNO_PAINT_DEBUG"]);
        }

        [TestMethod]
        public void MergeInto_AmbientUserValueWins()
        {
            var merged = PaintDebug.MergeInto(null, 7, "0");
            Assert.IsFalse(merged.ContainsKey("KUBUNO_PAINT_DEBUG"));
        }

        [TestMethod]
        public void MergeInto_OffAddsNothing()
        {
            var merged = PaintDebug.MergeInto(new Dictionary<string, string>(), 0, null);
            Assert.AreEqual(0, merged.Count);
        }

        [TestMethod]
        public void DesiredProcessValue_OriginalUserValueWins()
        {
            Assert.AreEqual("layout", PaintDebug.DesiredProcessValue(7, "layout"));
            Assert.AreEqual("layout", PaintDebug.DesiredProcessValue(0, "layout"));
            Assert.AreEqual("all", PaintDebug.DesiredProcessValue(7, null));
            Assert.AreEqual("all", PaintDebug.DesiredProcessValue(7, ""));
            Assert.IsNull(PaintDebug.DesiredProcessValue(0, null));
        }

        [TestMethod]
        public void SelectHostWindows_KeepsOnlyExactHostClass()
        {
            var windows = new List<KeyValuePair<IntPtr, string?>>
            {
                new KeyValuePair<IntPtr, string?>((IntPtr)1, "KubunoControlsHost"),
                new KeyValuePair<IntPtr, string?>((IntPtr)2, "kubunocontrolshost"),
                new KeyValuePair<IntPtr, string?>((IntPtr)3, "Chrome_WidgetWin_1"),
                new KeyValuePair<IntPtr, string?>((IntPtr)4, null),
                new KeyValuePair<IntPtr, string?>((IntPtr)5, "KubunoControlsHost"),
            };

            CollectionAssert.AreEqual(new[] { (IntPtr)1, (IntPtr)5 }, new List<IntPtr>(PaintDebug.SelectHostWindows(windows)));
        }
    }
}
