namespace Kubuno.Cargo.Processes
{
    /// <summary>One line of output streamed from a running process, as it happens.</summary>
    public sealed class ProcessOutputLine
    {
        public ProcessOutputLine(string text, bool isError)
        {
            Text = text;
            IsError = isError;
        }

        public string Text { get; }

        /// <summary>True when this line came from standard error rather than standard output.</summary>
        public bool IsError { get; }

        public override string ToString() => Text;
    }
}
