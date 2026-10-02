using System;
using System.Collections.Generic;
using System.Linq;
using Kubuno.Shared.DevAssistant.Logic.Changes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Shared.DevAssistant.Tests
{
    [TestClass]
    public sealed class ChangeSetTests
    {
        private const string View =
            "<Panel>\r\n" +
            "  <Label x:Name=\"a\"/>\r\n" +
            "  <Label x:Name=\"b\"/>\r\n" +
            "  <Label x:Name=\"c\"/>\r\n" +
            "  <Label x:Name=\"d\"/>\r\n" +
            "</Panel>\r\n";

        private const string Proposed =
            "<Panel>\r\n" +
            "  <Label x:Name=\"a\"/>\r\n" +
            "  <Switch x:Name=\"new1\"/>\r\n" +
            "  <Label x:Name=\"b\"/>\r\n" +
            "  <Label x:Name=\"c\"/>\r\n" +
            "  <Label x:Name=\"d\" Text=\"changed\"/>\r\n" +
            "</Panel>\r\n";

        [TestMethod]
        public void Diff_finds_one_hunk_per_separate_change_and_keeps_line_endings()
        {
            var hunks = LineDiff.Compute(View, Proposed);
            Assert.AreEqual(2, hunks.Count);
            Assert.AreEqual(2, hunks[0].OldStart);
            Assert.AreEqual(0, hunks[0].OldCount);
            Assert.AreEqual("  <Switch x:Name=\"new1\"/>\r\n", hunks[0].NewLines.Single());
            Assert.AreEqual(4, hunks[1].OldStart);
            Assert.AreEqual(1, hunks[1].OldCount);
            Assert.AreEqual(Proposed, LineDiff.ApplySelected(View, hunks, new HashSet<int> { 0, 1 }));
            Assert.AreEqual(View, LineDiff.ApplySelected(View, hunks, new HashSet<int>()));
        }

        [TestMethod]
        public void Accepting_one_hunk_of_two_applies_only_it_as_a_single_undo_unit()
        {
            var host = new FakeBuffers { [@"C:\s\v.kbview"] = View, [@"C:\s\other.rs"] = "fn a() {}\n" };
            var changes = new ChangeSet();
            var file = changes.Propose(@"C:\s\v.kbview", View, Proposed);
            changes.Propose(@"C:\s\other.rs", "fn a() {}\n", "fn a() {}\nfn b() {}\n");
            file.Accepted.Remove(1);

            var report = ChangeSetApplier.Apply(host, changes, "assistant");
            Assert.AreEqual(2, report.AppliedHunks);
            Assert.AreEqual(2, report.FilesTouched);
            Assert.AreEqual(0, report.Conflicts.Count);
            StringAssert.Contains(host[@"C:\s\v.kbview"], "new1");
            Assert.IsFalse(host[@"C:\s\v.kbview"].Contains("changed"));
            Assert.AreEqual("fn a() {}\nfn b() {}\n", host[@"C:\s\other.rs"]);
            Assert.AreEqual(1, host.UndoUnits.Count, "one undo unit for the whole change set");

            host.Undo();
            Assert.AreEqual(View, host[@"C:\s\v.kbview"], "one Ctrl+Z restores every file");
            Assert.AreEqual("fn a() {}\n", host[@"C:\s\other.rs"]);
        }

        [TestMethod]
        public void A_buffer_changed_since_the_proposal_is_re_anchored_or_reported_as_conflict()
        {
            var changes = new ChangeSet();
            changes.Propose(@"C:\s\v.kbview", View, Proposed);

            // Unrelated edit at the top: both hunks still anchor uniquely.
            var shifted = "<!-- header -->\r\n" + View;
            var host = new FakeBuffers { [@"C:\s\v.kbview"] = shifted };
            var report = ChangeSetApplier.Apply(host, changes, "assistant");
            Assert.AreEqual(2, report.AppliedHunks);
            Assert.AreEqual("<!-- header -->\r\n" + Proposed, host[@"C:\s\v.kbview"]);

            // The developer rewrote the lines around the second change: it becomes a conflict, the first still applies.
            var edited = View.Replace("  <Label x:Name=\"c\"/>\r\n", "  <Label x:Name=\"c2\"/>\r\n");
            var host2 = new FakeBuffers { [@"C:\s\v.kbview"] = edited };
            var report2 = ChangeSetApplier.Apply(host2, changes, "assistant");
            Assert.AreEqual(1, report2.AppliedHunks);
            Assert.AreEqual(1, report2.Conflicts.Count);
            Assert.AreEqual(1, report2.Conflicts[0].HunkIndex);
            StringAssert.Contains(host2[@"C:\s\v.kbview"], "new1");
            StringAssert.Contains(host2[@"C:\s\v.kbview"], "<Label x:Name=\"d\"/>");
        }

        [TestMethod]
        public void A_new_file_is_created_and_an_existing_one_is_never_overwritten()
        {
            var changes = new ChangeSet();
            changes.Propose(@"C:\s\new_view.kbview", null, "<Panel/>\n");
            var host = new FakeBuffers();
            Assert.AreEqual(1, ChangeSetApplier.Apply(host, changes, "x").FilesTouched);
            Assert.AreEqual("<Panel/>\n", host[@"C:\s\new_view.kbview"]);

            var again = ChangeSetApplier.Apply(host, changes, "x");
            Assert.AreEqual(0, again.FilesTouched);
            Assert.AreEqual(1, again.Conflicts.Count);
        }

        [TestMethod]
        public void A_second_proposal_on_the_same_file_keeps_the_first_snapshot()
        {
            var changes = new ChangeSet();
            changes.Propose(@"C:\s\v.kbview", View, View.Replace("\"a\"", "\"a1\""));
            changes.Propose(@"C:\s\v.kbview", View.Replace("\"a\"", "\"a1\""), View.Replace("\"a\"", "\"a1\"").Replace("\"d\"", "\"d1\""));
            Assert.AreEqual(1, changes.Files.Count);
            Assert.AreEqual(View, changes.Files[0].OriginalText);
            Assert.AreEqual(2, changes.Files[0].Hunks.Count);
        }

        [TestMethod]
        public void Anchored_edits_need_a_unique_anchor()
        {
            var text = "a\nb\na\n";
            Assert.IsFalse(AnchoredEditApplier.Apply(text, new[] { new AnchoredEdit { OldText = "a", NewText = "x" } }, null).Succeeded);
            Assert.IsFalse(AnchoredEditApplier.Apply(text, new[] { new AnchoredEdit { OldText = "zzz", NewText = "x" } }, null).Succeeded);
            var ok = AnchoredEditApplier.Apply(text, new[] { new AnchoredEdit { OldText = "a\nb", NewText = "a\nB" } }, null);
            Assert.AreEqual("a\nB\na\n", ok.Text);
            var crlf = AnchoredEditApplier.Apply("x\r\ny\r\n", new[] { new AnchoredEdit { OldText = "x\ny", NewText = "x\nz" } }, null);
            Assert.AreEqual("x\r\nz\r\n", crlf.Text, "a \\n anchor matches a CRLF file and the replacement follows its line endings");
        }

        /// <summary>In-memory buffers with a linked undo stack, like the VS host.</summary>
        private sealed class FakeBuffers : Dictionary<string, string>, IChangeSetBufferHost
        {
            private List<KeyValuePair<string, string?>>? _open;

            public FakeBuffers()
                : base(StringComparer.OrdinalIgnoreCase)
            {
            }

            public List<List<KeyValuePair<string, string?>>> UndoUnits { get; } = new List<List<KeyValuePair<string, string?>>>();

            public string? GetCurrentText(string path) => TryGetValue(path, out var text) ? text : null;

            public IDisposable BeginUndoUnit(string description, IReadOnlyList<string> paths)
            {
                _open = new List<KeyValuePair<string, string?>>();
                return new Closer(() =>
                {
                    UndoUnits.Add(_open!);
                    _open = null;
                });
            }

            public void Replace(string path, IReadOnlyList<TextReplacement> replacements)
            {
                Assert.IsNotNull(_open, "edits happen inside the undo unit");
                _open!.Add(new KeyValuePair<string, string?>(path, this[path]));
                var text = this[path];
                foreach (var r in replacements.OrderByDescending(r => r.Start))
                {
                    text = text.Substring(0, r.Start) + r.NewText + text.Substring(r.Start + r.Length);
                }

                this[path] = text;
            }

            public void CreateFile(string path, string text)
            {
                _open?.Add(new KeyValuePair<string, string?>(path, null));
                this[path] = text;
            }

            public void Undo()
            {
                var unit = UndoUnits[UndoUnits.Count - 1];
                UndoUnits.RemoveAt(UndoUnits.Count - 1);
                foreach (var (path, before) in Enumerable.Reverse(unit))
                {
                    if (before is null)
                    {
                        Remove(path);
                    }
                    else
                    {
                        this[path] = before;
                    }
                }
            }

            private sealed class Closer : IDisposable
            {
                private readonly Action _close;

                public Closer(Action close) => _close = close;

                public void Dispose() => _close();
            }
        }
    }
}
