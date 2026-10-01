using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Kubuno.Core.DevAssistant.Host
{
    /// <summary>
    /// kubuno-dev-assistant.exe: JSON-RPC lines on stdin/stdout (docs/AI-ASSISTANT.md section 9.1). Nothing is written
    /// to stdout except protocol lines; diagnostics go to stderr, never with message contents or keys. Takes no
    /// argument: in particular the API key is never passed on the command line - the host reads it from the Windows
    /// Credential Manager when a provider needs it.
    /// </summary>
    public static class Program
    {
        public static async Task<int> Main()
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            Console.InputEncoding = utf8;
            var input = new StreamReader(Console.OpenStandardInput(), utf8);
            var output = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = false, NewLine = "\n" };
            Console.SetOut(TextWriter.Null);

            using var server = new HostServer(input, output);
            server.Start();
            await server.Completion.ConfigureAwait(false);
            await Console.Error.WriteLineAsync("kubuno-dev-assistant: shutting down.").ConfigureAwait(false);
            return 0;
        }
    }
}
