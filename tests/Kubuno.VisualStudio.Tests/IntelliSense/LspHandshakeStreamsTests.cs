using System.IO;
using System.Text;
using System.Threading.Tasks;
using Kubuno.VisualStudio.Core.IntelliSense;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Tests.IntelliSense
{
    [TestClass]
    public sealed class LspHandshakeStreamsTests
    {
        private const string Initialize = "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"initialize\",\"params\":{\"capabilities\":{}}}";
        private const string InitializeResult = "{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"capabilities\":{\"hoverProvider\":true}}}";

        [TestMethod]
        public void TheInitializeRequestIsRewrittenThenLaterMessagesPassUntouched()
        {
            var serverInput = new MemoryStream();
            var streams = new LspHandshakeStreams(serverInput, new MemoryStream(), json => json.Replace("\"capabilities\":{}", "\"capabilities\":{\"x\":1}"), _ => null);

            var initialize = LspHandshakeStreams.Frame(Initialize);
            // Written in two pieces: the stream has to put the frame back together.
            streams.ClientToServer.Write(initialize, 0, 10);
            streams.ClientToServer.Write(initialize, 10, initialize.Length - 10);
            var didOpen = LspHandshakeStreams.Frame("{\"jsonrpc\":\"2.0\",\"method\":\"initialized\",\"params\":{\"capabilities\":{}}}");
            streams.ClientToServer.Write(didOpen, 0, didOpen.Length);

            var written = Encoding.UTF8.GetString(serverInput.ToArray());
            StringAssert.Contains(written, "\"capabilities\":{\"x\":1}");
            StringAssert.EndsWith(written, "\"method\":\"initialized\",\"params\":{\"capabilities\":{}}}");
            StringAssert.StartsWith(written, "Content-Length: " + Encoding.UTF8.GetByteCount(Initialize.Replace("{}", "{\"x\":1}")) + "\r\n\r\n");
        }

        [TestMethod]
        public async Task TheAnswerToInitializeIsRewrittenAndWhatFollowsIsReadAsIs()
        {
            var output = new MemoryStream();
            var log = LspHandshakeStreams.Frame("{\"jsonrpc\":\"2.0\",\"method\":\"window/logMessage\",\"params\":{\"message\":\"é\"}}");
            var result = LspHandshakeStreams.Frame(InitializeResult);
            var after = LspHandshakeStreams.Frame("{\"jsonrpc\":\"2.0\",\"id\":3,\"result\":null}");
            output.Write(log, 0, log.Length);
            output.Write(result, 0, result.Length);
            output.Write(after, 0, after.Length);
            output.Position = 0;

            var streams = new LspHandshakeStreams(new MemoryStream(), output, _ => null, json => json.Replace("\"hoverProvider\":true", "\"hoverProvider\":false"));
            var request = LspHandshakeStreams.Frame(Initialize);
            streams.ClientToServer.Write(request, 0, request.Length);

            var read = new MemoryStream();
            var buffer = new byte[7];
            int n;
            while ((n = await streams.ServerToClient.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                read.Write(buffer, 0, n);
            }

            var text = Encoding.UTF8.GetString(read.ToArray());
            StringAssert.Contains(text, "\"hoverProvider\":false");
            StringAssert.Contains(text, "\"message\":\"é\"");
            StringAssert.EndsWith(text, "{\"jsonrpc\":\"2.0\",\"id\":3,\"result\":null}");
            Assert.IsTrue(streams.IsHandshakeDone);
        }

        [TestMethod]
        public void ARewriteThatThrowsForwardsTheOriginalMessage()
        {
            var serverInput = new MemoryStream();
            string? error = null;
            var streams = new LspHandshakeStreams(serverInput, new MemoryStream(), _ => throw new InvalidDataException("boom"), _ => null)
            {
                OnRewriteError = e => error = e.Message,
            };

            var frame = LspHandshakeStreams.Frame(Initialize);
            streams.ClientToServer.Write(frame, 0, frame.Length);

            Assert.AreEqual("boom", error);
            StringAssert.EndsWith(Encoding.UTF8.GetString(serverInput.ToArray()), Initialize);
        }
    }
}
