using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Rust.ProjectSystem.ProjectProperties
{
    /// <summary>
    /// Runs the Project Properties file writes (Cargo.toml, rustfmt.toml, src/main.rs) one at a time, outside the
    /// CPS project lock <c>SetPropertyValueAsync</c> is called under: the work is started with the execution
    /// context NOT flowing, so it neither inherits the caller's lock (whose forks may not request other locks)
    /// nor keeps it waiting. Writes are applied in the order the editor made them.
    /// </summary>
    internal static class PropertyWriteQueue
    {
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);

        public static void Enqueue(Func<Task> work)
        {
            using (ExecutionContext.SuppressFlow())
            {
                _ = Task.Run(async () =>
                {
                    await Gate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        await ThreadHelper.JoinableTaskFactory.RunAsync(work);
                    }
                    catch (Exception ex)
                    {
                        PropertiesLog.Write("queued property write failed: " + ex);
                    }
                    finally
                    {
                        Gate.Release();
                    }
                });
            }
        }
    }
}
