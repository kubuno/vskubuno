using System;

namespace Kubuno.VisualStudio.Designer.Editing
{
    /// <summary>Thrown by <see cref="TextEditPlanner.Plan"/> when two edits in the same batch cover overlapping text ranges - they cannot both be expressed as offsets into the same original snapshot without one invalidating the other.</summary>
    public sealed class TextEditConflictException : Exception
    {
        public TextEditConflictException(string message)
            : base(message)
        {
        }
    }
}
