using System;

namespace Kubuno.Cargo.MSBuild.Tasks.Internal
{
    /// <summary>
    /// An <see cref="IProgress{T}"/> that runs its handler right away, on the reporting thread, one report at a time.
    /// <see cref="Progress{T}"/> posts every report to the thread pool when there is no synchronization context (always the
    /// case in an MSBuild task), so some of cargo's last lines were handled after the process - and sometimes after the task
    /// itself - had finished: an executable missing from <c>CargoBuild.Executables</c>, a diagnostic logged too late.
    /// </summary>
    public sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;
        private readonly object _gate = new();

        public InlineProgress(Action<T> handler) => _handler = handler ?? throw new ArgumentNullException(nameof(handler));

        public void Report(T value)
        {
            lock (_gate)
            {
                _handler(value);
            }
        }
    }
}
