using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Core.Mcp.Bridge.PipeProtocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Core.Mcp.Tests
{
    /// <summary>
    /// Wire-level tests for <see cref="PipeMessageFraming"/>: the 4-byte-length-prefix framing
    /// shared by <see cref="VsMcpBridgeHost"/> and <see cref="VsMcpBridgeClient"/>. Uses a plain
    /// <see cref="MemoryStream"/> rather than a real named pipe - framing correctness does not
    /// depend on the transport (see PipeEndToEndTests for the real-pipe version).
    /// </summary>
    [TestClass]
    public sealed class PipeMessageFramingTests
    {
        [TestMethod]
        public async Task WriteThenRead_RoundTripsExactText()
        {
            using var stream = new MemoryStream();
            await PipeMessageFraming.WriteMessageAsync(stream, "{\"hello\":\"world\"}", CancellationToken.None);

            stream.Position = 0;
            string? read = await PipeMessageFraming.ReadMessageAsync(stream, CancellationToken.None);

            Assert.AreEqual("{\"hello\":\"world\"}", read);
        }

        [TestMethod]
        public async Task WriteThenRead_RoundTripsEmptyMessage()
        {
            using var stream = new MemoryStream();
            await PipeMessageFraming.WriteMessageAsync(stream, string.Empty, CancellationToken.None);

            stream.Position = 0;
            string? read = await PipeMessageFraming.ReadMessageAsync(stream, CancellationToken.None);

            Assert.AreEqual(string.Empty, read);
        }

        [TestMethod]
        public async Task WriteThenRead_RoundTripsMultibyteUtf8Text()
        {
            using var stream = new MemoryStream();
            string text = "{\"message\":\"Bonjour, ça marche ? 你好\"}";
            await PipeMessageFraming.WriteMessageAsync(stream, text, CancellationToken.None);

            stream.Position = 0;
            string? read = await PipeMessageFraming.ReadMessageAsync(stream, CancellationToken.None);

            Assert.AreEqual(text, read);
        }

        [TestMethod]
        public async Task WriteThenRead_RoundTripsMultipleSequentialMessages()
        {
            using var stream = new MemoryStream();
            await PipeMessageFraming.WriteMessageAsync(stream, "first", CancellationToken.None);
            await PipeMessageFraming.WriteMessageAsync(stream, "second", CancellationToken.None);
            await PipeMessageFraming.WriteMessageAsync(stream, "third", CancellationToken.None);

            stream.Position = 0;
            Assert.AreEqual("first", await PipeMessageFraming.ReadMessageAsync(stream, CancellationToken.None));
            Assert.AreEqual("second", await PipeMessageFraming.ReadMessageAsync(stream, CancellationToken.None));
            Assert.AreEqual("third", await PipeMessageFraming.ReadMessageAsync(stream, CancellationToken.None));
        }

        [TestMethod]
        public async Task WriteThenRead_RoundTripsALargeMessage()
        {
            using var stream = new MemoryStream();
            string large = new string('x', 500_000);
            await PipeMessageFraming.WriteMessageAsync(stream, large, CancellationToken.None);

            stream.Position = 0;
            string? read = await PipeMessageFraming.ReadMessageAsync(stream, CancellationToken.None);

            Assert.AreEqual(large, read);
        }

        [TestMethod]
        public async Task ReadMessageAsync_ReturnsNull_OnCleanEofAtMessageBoundary()
        {
            using var stream = new MemoryStream(); // Nothing written: EOF immediately.
            string? read = await PipeMessageFraming.ReadMessageAsync(stream, CancellationToken.None);

            Assert.IsNull(read);
        }

        [TestMethod]
        public async Task ReadMessageAsync_Throws_OnTruncatedHeader()
        {
            using var stream = new MemoryStream(new byte[] { 1, 2 }); // Fewer than 4 header bytes.

            await Assert.ThrowsExactlyAsync<EndOfStreamException>(
                () => PipeMessageFraming.ReadMessageAsync(stream, CancellationToken.None));
        }

        [TestMethod]
        public async Task ReadMessageAsync_Throws_OnTruncatedPayload()
        {
            using var stream = new MemoryStream();
            byte[] header = BitConverter.GetBytes(100); // Claims 100 bytes of payload.
            stream.Write(header, 0, header.Length);
            stream.Write(Encoding.UTF8.GetBytes("too short"), 0, 9);
            stream.Position = 0;

            await Assert.ThrowsExactlyAsync<EndOfStreamException>(
                () => PipeMessageFraming.ReadMessageAsync(stream, CancellationToken.None));
        }

        [TestMethod]
        public async Task ReadMessageAsync_Throws_OnNegativeLength()
        {
            using var stream = new MemoryStream();
            stream.Write(BitConverter.GetBytes(-1), 0, 4);
            stream.Position = 0;

            await Assert.ThrowsExactlyAsync<InvalidDataException>(
                () => PipeMessageFraming.ReadMessageAsync(stream, CancellationToken.None));
        }

        [TestMethod]
        public async Task ReadMessageAsync_Throws_OnLengthAboveMax()
        {
            using var stream = new MemoryStream();
            stream.Write(BitConverter.GetBytes(PipeMessageFraming.MaxMessageLength + 1), 0, 4);
            stream.Position = 0;

            await Assert.ThrowsExactlyAsync<InvalidDataException>(
                () => PipeMessageFraming.ReadMessageAsync(stream, CancellationToken.None));
        }
    }
}
