using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Kubuno.Core.Mcp.Bridge.PipeProtocol
{
    /// <summary>
    /// Wire framing for one JSON document per message over a <see cref="Stream"/> (a
    /// <c>NamedPipeServerStream</c>/<c>NamedPipeClientStream</c> pair in production, any duplex
    /// stream in tests): a 4-byte little-endian message length, followed by that many UTF-8 bytes.
    /// </summary>
    /// <remarks>
    /// Named pipes in byte mode don't preserve message boundaries the way
    /// <see cref="System.IO.Pipes.PipeTransmissionMode.Message"/> would on Windows-only code, and a
    /// length prefix works identically on every <see cref="Stream"/> (including the
    /// <see cref="System.IO.Pipelines.Pipe"/>-backed streams used by the protocol round-trip
    /// tests), so it is used unconditionally rather than relying on message-mode pipes.
    /// </remarks>
    public static class PipeMessageFraming
    {
        /// <summary>
        /// Refuses to allocate more than this for a single message. Generous for any of this
        /// bridge's DTOs (even a large Error List or output pane dump), but bounds a
        /// corrupted/malicious length prefix.
        /// </summary>
        public const int MaxMessageLength = 64 * 1024 * 1024;

        public static async Task WriteMessageAsync(Stream stream, string json, CancellationToken cancellationToken)
        {
            if (stream is null) throw new ArgumentNullException(nameof(stream));
            if (json is null) throw new ArgumentNullException(nameof(json));

            byte[] payload = Encoding.UTF8.GetBytes(json);
            if (payload.Length > MaxMessageLength)
            {
                throw new InvalidOperationException(
                    $"Refusing to write a {payload.Length}-byte pipe message (max {MaxMessageLength}).");
            }

            byte[] header = BitConverter.GetBytes(payload.Length);
#if NET48
            await stream.WriteAsync(header, 0, header.Length, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(payload, 0, payload.Length, cancellationToken).ConfigureAwait(false);
#else
            await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
#endif
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the next framed message, or returns <see langword="null"/> if the stream was
        /// closed cleanly before any bytes of a new message arrived (end of conversation - not an
        /// error).
        /// </summary>
        public static async Task<string?> ReadMessageAsync(Stream stream, CancellationToken cancellationToken)
        {
            if (stream is null) throw new ArgumentNullException(nameof(stream));

            byte[] header = new byte[4];
            int headerRead = await ReadExactAsync(stream, header, 0, header.Length, cancellationToken).ConfigureAwait(false);
            if (headerRead == 0)
            {
                // Clean EOF right at a message boundary: the other side hung up.
                return null;
            }
            if (headerRead != header.Length)
            {
                throw new EndOfStreamException("Pipe closed mid-header while reading a framed message.");
            }

            int length = BitConverter.ToInt32(header, 0);
            if (length < 0 || length > MaxMessageLength)
            {
                throw new InvalidDataException($"Framed pipe message length {length} is out of bounds (0..{MaxMessageLength}).");
            }

            if (length == 0)
            {
                return string.Empty;
            }

            byte[] payload = new byte[length];
            int payloadRead = await ReadExactAsync(stream, payload, 0, payload.Length, cancellationToken).ConfigureAwait(false);
            if (payloadRead != payload.Length)
            {
                throw new EndOfStreamException("Pipe closed mid-payload while reading a framed message.");
            }

            return Encoding.UTF8.GetString(payload);
        }

        /// <summary>
        /// <see cref="Stream.ReadAsync(byte[],int,int,CancellationToken)"/> may return fewer bytes
        /// than requested for a pipe even mid-message; this loops until <paramref name="count"/>
        /// bytes are read or the stream ends. Returns the number of bytes actually read, which is
        /// either <paramref name="count"/> or (only if EOF hits on the very first read) 0.
        /// </summary>
        private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
#if NET48
                int read = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead, cancellationToken).ConfigureAwait(false);
#else
                int read = await stream.ReadAsync(buffer.AsMemory(offset + totalRead, count - totalRead), cancellationToken).ConfigureAwait(false);
#endif
                if (read == 0)
                {
                    return totalRead;
                }
                totalRead += read;
            }
            return totalRead;
        }
    }
}
