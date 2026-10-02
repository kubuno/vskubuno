using System;
using System.Collections.Generic;

namespace Kubuno.Desktop.Designer.Editing
{
    /// <summary>
    /// One <c>kubuno/applyEdit</c> response, ready to apply to a buffer:
    /// <see cref="BaseVersion"/> is the buffer version the language server computed <see cref="Edits"/>
    /// against (docs/DESIGNER.md §3's rowan tree, synced on every <c>didChange</c>) - the version check
    /// this task calls for ("reject if the buffer changed since the request snapshot") compares this
    /// against the live buffer's current version before applying anything.
    /// </summary>
    public sealed class ApplyEditRequest
    {
        public ApplyEditRequest(int baseVersion, IReadOnlyList<TextEditDto> edits)
        {
            BaseVersion = baseVersion;
            Edits = edits ?? throw new ArgumentNullException(nameof(edits));
        }

        public int BaseVersion { get; }

        public IReadOnlyList<TextEditDto> Edits { get; }
    }
}
