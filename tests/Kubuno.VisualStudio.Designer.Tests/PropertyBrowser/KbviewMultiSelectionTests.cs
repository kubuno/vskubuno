using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Kubuno.VisualStudio.Designer.PropertyBrowser;
using Kubuno.VisualStudio.Designer.Registry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Designer.Tests.PropertyBrowser
{
    /// <summary>
    /// docs/DESIGNER.md §13: several elements shown at once in Visual Studio's Properties window. The window merges
    /// the objects' properties by name and type (WinForms <c>PropertyGrid</c> semantics), blanks the values that
    /// differ, and sets an edited value on every object in turn - which must land as ONE batch.
    /// </summary>
    [TestClass]
    public class KbviewMultiSelectionTests
    {
        private const string View = "<Panel>\n  <Button x:Name=\"ok\" Text=\"OK\" X=\"10\"/>\n  <Button x:Name=\"cancel\" Text=\"Cancel\" X=\"100\"/>\n  <Stack/>\n</Panel>";

        private static readonly ComponentRegistry Registry = ComponentRegistry.FromJson(TestFixtures.ReadAllText("registry.sample.json"));

        [TestInitialize]
        public void ForceEnglish() => DesignerText.ForceFrench = false;

        [TestCleanup]
        public void ResetLanguage() => DesignerText.ForceFrench = null;

        /// <summary>The rows the Properties window shows for several objects: those every object has, with the same name and type.</summary>
        private static IReadOnlyList<string> CommonRows(params object[] objects) =>
            objects
                .Select(o => TypeDescriptor.GetProperties(o).Cast<PropertyDescriptor>().Where(p => p.IsBrowsable).Select(p => (p.Name, p.PropertyType)).ToList())
                .Aggregate((a, b) => a.Intersect(b).ToList())
                .Select(p => p.Name)
                .ToList();

        [TestMethod]
        public void TwoButtons_ShareEveryRow_AndTheirValuesDiffer()
        {
            var host = new BatchingHost(View);
            var ok = new KbviewElementObject(host, "0", Registry.Find("Button")!);
            var cancel = new KbviewElementObject(host, "1", Registry.Find("Button")!);

            var rows = CommonRows(ok, cancel);
            CollectionAssert.IsSubsetOf(new[] { "x:Name", "Text", KbviewElementObject.LocationRow, KbviewElementObject.SizeRow, "Dock", "Anchor" }, rows.ToArray());

            // What the grid compares to blank a differing value.
            var text = TypeDescriptor.GetProperties(ok).Find("Text", false)!;
            Assert.AreEqual("OK", text.GetValue(ok));
            Assert.AreEqual("Cancel", text.GetValue(cancel));
            // The grid merges rows by name AND type: every plain row is string-typed, the expandable ones share one value type.
            Assert.IsTrue(TypeDescriptor.GetProperties(ok).Cast<PropertyDescriptor>().All(p => p.PropertyType == typeof(string) || p.PropertyType == typeof(KbviewCompositeValue) || p.PropertyType == typeof(KbviewBindingsValue)));
        }

        [TestMethod]
        public void DifferentComponents_ShareOnlyTheirCommonRows()
        {
            var host = new BatchingHost(View);
            var button = new KbviewElementObject(host, "0", Registry.Find("Button")!);
            var stack = new KbviewElementObject(host, "2", Registry.Find("Stack")!);

            var rows = CommonRows(button, stack);
            CollectionAssert.Contains(rows.ToArray(), KbviewElementObject.LocationRow);
            CollectionAssert.Contains(rows.ToArray(), "x:Name");
            CollectionAssert.DoesNotContain(rows.ToArray(), "Text", "a Stack has no Text");
        }

        [TestMethod]
        public void EditingTwoElements_IsOneBatch()
        {
            var host = new BatchingHost(View);
            var ok = new KbviewElementObject(host, "0", Registry.Find("Button")!);
            var cancel = new KbviewElementObject(host, "1", Registry.Find("Button")!);
            var width = TypeDescriptor.GetProperties(ok).Find("Width", false)!;
            var x = TypeDescriptor.GetProperties(ok).Find("X", false)!;

            // What the grid does for an edit on a multi-selection: set the value on each object in turn.
            width.SetValue(ok, "120");
            width.SetValue(cancel, "120");
            x.ResetValue(ok);
            Assert.AreEqual(0, host.Batches.Count, "nothing applied before the dispatcher turn");

            host.RunScheduled();

            Assert.AreEqual(1, host.Batches.Count, "one batch = one undo unit");
            CollectionAssert.AreEqual(
                new[] { "set 0 Width=120", "set 1 Width=120", "remove 0 X" },
                host.Batches[0].Select(e => e.ToString()).ToArray());
            Assert.AreEqual("120", width.GetValue(cancel), "shown optimistically until the buffer catches up");
        }

        [TestMethod]
        public void Batcher_SchedulesOnceAndStartsANewBatchAfterAFlush()
        {
            var scheduled = new List<Action>();
            var batches = new List<IReadOnlyList<PropertyEdit>>();
            var batcher = new PropertyEditBatcher(scheduled.Add, batches.Add);

            batcher.Enqueue(new PropertyEdit("0", "Text", "a"));
            batcher.Enqueue(new PropertyEdit("1", "Text", "a"));
            Assert.AreEqual(1, scheduled.Count);
            scheduled[0]();
            Assert.AreEqual(2, batches[0].Count);

            batcher.Enqueue(new PropertyEdit("0", "Text", null));
            Assert.AreEqual(2, scheduled.Count, "a new batch schedules a new flush");
            scheduled[1]();
            Assert.AreEqual("remove 0 Text", batches[1].Single().ToString());
            batcher.Flush();
            Assert.AreEqual(2, batches.Count, "an empty flush applies nothing");
        }

        /// <summary>A host batching its edits like the designer does, with a manual dispatcher.</summary>
        private sealed class BatchingHost : IKbviewElementHost
        {
            private readonly string _text;
            private readonly List<Action> _scheduled = new List<Action>();
            private readonly PropertyEditBatcher _batcher;

            public BatchingHost(string text)
            {
                _text = text;
                _batcher = new PropertyEditBatcher(_scheduled.Add, Batches.Add);
            }

            public List<IReadOnlyList<PropertyEdit>> Batches { get; } = new List<IReadOnlyList<PropertyEdit>>();

            public ComponentRegistry Registry => KbviewMultiSelectionTests.Registry;

            public int CurrentVersion => 1;

            public string GetCurrentText() => _text;

            public void SetAttribute(string elementId, string name, string value) => _batcher.Enqueue(new PropertyEdit(elementId, name, value));

            public void RemoveAttribute(string elementId, string name) => _batcher.Enqueue(new PropertyEdit(elementId, name, null));

            public void CreateOrShowHandler(string elementId, string eventName, string? suggestedName)
            {
            }

            public bool IsHandlerRequestRecent(string elementId, string eventName) => false;

            public IReadOnlyList<string> GetCompatibleHandlers(string elementId, string eventName) => Array.Empty<string>();

            public void RenameHandler(string elementId, string eventName, string oldName, string newName)
            {
            }

            public void RemoveHandler(string elementId, string eventName)
            {
            }

            public void RunScheduled()
            {
                var pending = _scheduled.ToList();
                _scheduled.Clear();
                pending.ForEach(a => a());
            }
        }
    }
}
