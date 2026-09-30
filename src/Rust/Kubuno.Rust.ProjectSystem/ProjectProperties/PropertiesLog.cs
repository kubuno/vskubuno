using System;
using System.IO;

namespace Kubuno.VisualStudio.RustProjectSystem.ProjectProperties
{
    /// <summary>
    /// Diagnostics of the Project Properties integration: failures are always traced; with the environment variable
    /// <c>KUBUNO_PROPERTIES_LOG=1</c> they (and every read/write) also go to <c>%TEMP%\kubuno-properties.log</c>.
    /// </summary>
    internal static class PropertiesLog
    {
        private static readonly object Gate = new object();
        private static readonly bool Enabled = Environment.GetEnvironmentVariable("KUBUNO_PROPERTIES_LOG") == "1";

        public static void Write(string message)
        {
            System.Diagnostics.Trace.WriteLine("Kubuno properties: " + message);
            if (!Enabled)
            {
                return;
            }
            try
            {
                lock (Gate)
                {
                    File.AppendAllText(Path.Combine(Path.GetTempPath(), "kubuno-properties.log"), $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
