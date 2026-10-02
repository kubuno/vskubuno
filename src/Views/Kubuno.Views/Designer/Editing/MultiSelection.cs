using System;
using System.Collections.Generic;
using System.Linq;
using Kubuno.Views.Designer.Selection;

namespace Kubuno.Views.Designer.Editing
{
    /// <summary>
    /// Pure helpers over a multi-selection of stable element ids (docs/DESIGNER.md §13) - the C# mirror of
    /// <c>kubuno_views::design::top_level_ids</c>/<c>is_ancestor_id</c>, used by every group gesture the
    /// host carries out itself (Delete, Cut/Copy/Paste, Duplicate, Bring to Front/Send to Back, the Layout
    /// commands' enabling).
    /// </summary>
    public static class MultiSelection
    {
        /// <summary>Whether <paramref name="ancestor"/> is a strict ancestor of <paramref name="id"/> (<c>""</c>, the root, is everyone's).</summary>
        public static bool IsAncestor(string ancestor, string id)
        {
            if (ancestor is null || id is null || ancestor == id)
            {
                return false;
            }

            return ancestor.Length == 0 || id.StartsWith(ancestor + ".", StringComparison.Ordinal);
        }

        /// <summary>
        /// The ids a group gesture applies to: the root dropped, every id one of whose ancestors is also selected
        /// dropped (it follows its container), duplicates dropped, in document order.
        /// </summary>
        public static IReadOnlyList<string> TopLevel(IEnumerable<string> ids)
        {
            var all = (ids ?? Array.Empty<string>()).Where(id => id is not null).Distinct(StringComparer.Ordinal).ToList();
            return InDocumentOrder(all.Where(id => id.Length > 0 && !all.Any(other => other.Length > 0 && IsAncestor(other, id))));
        }

        /// <summary><paramref name="ids"/> sorted in document order (by their ordinal paths); malformed ids last.</summary>
        public static IReadOnlyList<string> InDocumentOrder(IEnumerable<string> ids) => ids.OrderBy(id => id, DocumentOrder.Instance).ToList();

        /// <summary>Compares two stable ids in document order (a container before its children, siblings by index).</summary>
        public sealed class DocumentOrder : IComparer<string>
        {
            public static readonly DocumentOrder Instance = new DocumentOrder();

            public int Compare(string? x, string? y)
            {
                var okX = StableElementId.TryParseSegments(x ?? string.Empty, out var a);
                var okY = StableElementId.TryParseSegments(y ?? string.Empty, out var b);
                if (!okX || !okY)
                {
                    return okX == okY ? string.CompareOrdinal(x, y) : okX ? -1 : 1;
                }

                for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
                {
                    if (a[i] != b[i])
                    {
                        return a[i].CompareTo(b[i]);
                    }
                }

                return a.Length.CompareTo(b.Length);
            }
        }
    }
}
