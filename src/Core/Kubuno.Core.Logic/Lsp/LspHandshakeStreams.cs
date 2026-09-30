using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Kubuno.VisualStudio.Core.IntelliSense
{
    /// <summary>
    /// Rewrites the <c>initialize</c> handshake between Visual Studio's LSP client and a language server, then
    /// steps aside. Visual Studio builds the <c>initialize</c> request itself (it never goes through a middle
    /// layer) and reads the server's capabilities from the answer, so the only place a VSIX can adjust either
    /// is the byte stream: <see cref="ClientToServer"/> wraps the server's standard input (what Visual Studio
    /// writes), <see cref="ServerToClient"/> its standard output (what Visual Studio reads). Each side parses
    /// LSP frames only until its half of the handshake has gone through, and is a plain pass-through after that.
    /// </summary>
    public sealed class LspHandshakeStreams
    {
        private readonly Func<string, string?> _rewriteInitializeParams;
        private readonly Func<string, string?> _rewriteInitializeResult;
        private string? _initializeId;
        private volatile bool _requestDone;
        private volatile bool _responseDone;

        /// <param name="serverInput">The server's standard input.</param>
        /// <param name="serverOutput">The server's standard output.</param>
        /// <param name="rewriteInitializeRequest">The whole <c>initialize</c> request message (JSON) → its replacement, or null to keep it.</param>
        /// <param name="rewriteInitializeResponse">The whole answer to <c>initialize</c> (JSON) → its replacement, or null to keep it.</param>
        public LspHandshakeStreams(Stream serverInput, Stream serverOutput, Func<string, string?> rewriteInitializeRequest, Func<string, string?> rewriteInitializeResponse)
        {
            _rewriteInitializeParams = rewriteInitializeRequest ?? throw new ArgumentNullException(nameof(rewriteInitializeRequest));
            _rewriteInitializeResult = rewriteInitializeResponse ?? throw new ArgumentNullException(nameof(rewriteInitializeResponse));
            ClientToServer = new FramedWriteStream(serverInput ?? throw new ArgumentNullException(nameof(serverInput)), OnClientMessage, () => _requestDone);
            ServerToClient = new FramedReadStream(serverOutput ?? throw new ArgumentNullException(nameof(serverOutput)), OnServerMessage, () => _responseDone);
        }

        /// <summary>The stream Visual Studio writes to (hand it to the <c>Connection</c> as its writer).</summary>
        public Stream ClientToServer { get; }

        /// <summary>The stream Visual Studio reads from (hand it to the <c>Connection</c> as its reader).</summary>
        public Stream ServerToClient { get; }

        /// <summary>True once both halves of the handshake went through (both streams are then pass-throughs).</summary>
        public bool IsHandshakeDone => _requestDone && _responseDone;

        /// <summary>Called with any exception a rewrite threw (the original message is then forwarded unchanged).</summary>
        public Action<Exception>? OnRewriteError { get; set; }

        private string OnClientMessage(string json)
        {
            if (_requestDone || !LspJson.IsRequest(json, "initialize", out var id))
            {
                return json;
            }

            _initializeId = id;
            _requestDone = true;
            return Rewrite(_rewriteInitializeParams, json);
        }

        private string OnServerMessage(string json)
        {
            if (_responseDone || _initializeId is null || !LspJson.IsResponse(json, _initializeId))
            {
                return json;
            }

            _responseDone = true;
            return Rewrite(_rewriteInitializeResult, json);
        }

        private string Rewrite(Func<string, string?> rewrite, string json)
        {
            try
            {
                return rewrite(json) ?? json;
            }
            catch (Exception exception)
            {
                OnRewriteError?.Invoke(exception);
                return json;
            }
        }

        /// <summary>Encodes one LSP frame (<c>Content-Length</c> header + UTF-8 body).</summary>
        public static byte[] Frame(string json)
        {
            var body = Encoding.UTF8.GetBytes(json);
            var header = Encoding.ASCII.GetBytes("Content-Length: " + body.Length + "\r\n\r\n");
            var frame = new byte[header.Length + body.Length];
            Buffer.BlockCopy(header, 0, frame, 0, header.Length);
            Buffer.BlockCopy(body, 0, frame, header.Length, body.Length);
            return frame;
        }

        /// <summary>
        /// Takes the first complete frame out of <paramref name="buffer"/>: its body, or null when the buffer does
        /// not hold a whole frame yet. A header without a usable <c>Content-Length</c> throws (the stream is corrupt).
        /// </summary>
        internal static string? TryTakeFrame(List<byte> buffer)
        {
            int headerEnd = -1;
            for (int i = 3; i < buffer.Count; i++)
            {
                if (buffer[i - 3] == '\r' && buffer[i - 2] == '\n' && buffer[i - 1] == '\r' && buffer[i] == '\n')
                {
                    headerEnd = i + 1;
                    break;
                }
            }

            if (headerEnd < 0)
            {
                return null;
            }

            var header = Encoding.ASCII.GetString(buffer.GetRange(0, headerEnd).ToArray());
            int length = -1;
            foreach (var line in header.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = line.IndexOf(':');
                if (colon > 0 && string.Equals(line.Substring(0, colon).Trim(), "Content-Length", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(line.Substring(colon + 1).Trim(), out var parsed))
                {
                    length = parsed;
                }
            }

            if (length < 0)
            {
                throw new InvalidDataException("LSP frame without a Content-Length header.");
            }

            if (buffer.Count < headerEnd + length)
            {
                return null;
            }

            var body = Encoding.UTF8.GetString(buffer.GetRange(headerEnd, length).ToArray());
            buffer.RemoveRange(0, headerEnd + length);
            return body;
        }

        /// <summary>What Visual Studio writes: frames go through the rewrite until <c>passThrough</c> is true.</summary>
        private sealed class FramedWriteStream : Stream
        {
            private readonly Stream _inner;
            private readonly Func<string, string> _rewrite;
            private readonly Func<bool> _passThrough;
            private readonly List<byte> _pending = new List<byte>();
            private readonly object _gate = new object();

            public FramedWriteStream(Stream inner, Func<string, string> rewrite, Func<bool> passThrough)
            {
                _inner = inner;
                _rewrite = rewrite;
                _passThrough = passThrough;
            }

            public override bool CanRead => false;

            public override bool CanSeek => false;

            public override bool CanWrite => true;

            public override long Length => throw new NotSupportedException();

            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public override void Write(byte[] buffer, int offset, int count)
            {
                lock (_gate)
                {
                    if (_pending.Count == 0 && _passThrough())
                    {
                        _inner.Write(buffer, offset, count);
                        return;
                    }

                    for (int i = 0; i < count; i++)
                    {
                        _pending.Add(buffer[offset + i]);
                    }

                    while (!_passThrough())
                    {
                        var body = TryTakeFrame(_pending);
                        if (body is null)
                        {
                            return;
                        }

                        var frame = Frame(_rewrite(body));
                        _inner.Write(frame, 0, frame.Length);
                    }

                    // The handshake is over: whatever is left (possibly half a frame) goes out as is.
                    if (_pending.Count > 0)
                    {
                        var rest = _pending.ToArray();
                        _pending.Clear();
                        _inner.Write(rest, 0, rest.Length);
                    }
                }
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Write(buffer, offset, count);
                return Task.CompletedTask;
            }

            public override void Flush()
            {
                lock (_gate)
                {
                    _inner.Flush();
                }
            }

            public override Task FlushAsync(CancellationToken cancellationToken)
            {
                Flush();
                return Task.CompletedTask;
            }

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _inner.Dispose();
                }

                base.Dispose(disposing);
            }
        }

        /// <summary>What Visual Studio reads: frames go through the rewrite until <c>passThrough</c> is true.</summary>
        private sealed class FramedReadStream : Stream
        {
            private readonly Stream _inner;
            private readonly Func<string, string> _rewrite;
            private readonly Func<bool> _passThrough;
            private readonly List<byte> _raw = new List<byte>();
            private byte[] _ready = Array.Empty<byte>();
            private int _readyOffset;
            private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

            public FramedReadStream(Stream inner, Func<string, string> rewrite, Func<bool> passThrough)
            {
                _inner = inner;
                _rewrite = rewrite;
                _passThrough = passThrough;
            }

            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotSupportedException();

            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public override int Read(byte[] buffer, int offset, int count)
            {
                _gate.Wait();
                try
                {
                    return ReadCore(buffer, offset, count, CancellationToken.None, sync: true).GetAwaiter().GetResult();
                }
                finally
                {
                    _gate.Release();
                }
            }

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    return await ReadCore(buffer, offset, count, cancellationToken, sync: false).ConfigureAwait(false);
                }
                finally
                {
                    _gate.Release();
                }
            }

            private async Task<int> ReadCore(byte[] buffer, int offset, int count, CancellationToken cancellationToken, bool sync)
            {
                if (count == 0)
                {
                    return 0;
                }

                while (true)
                {
                    if (_readyOffset < _ready.Length)
                    {
                        int n = Math.Min(count, _ready.Length - _readyOffset);
                        Buffer.BlockCopy(_ready, _readyOffset, buffer, offset, n);
                        _readyOffset += n;
                        return n;
                    }

                    if (_passThrough())
                    {
                        if (_raw.Count > 0)
                        {
                            // Bytes read past the handshake's last frame: hand them out before reading more.
                            _ready = _raw.ToArray();
                            _readyOffset = 0;
                            _raw.Clear();
                            continue;
                        }

                        return sync ? _inner.Read(buffer, offset, count) : await _inner.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
                    }

                    var body = TryTakeFrame(_raw);
                    if (body != null)
                    {
                        _ready = Frame(_rewrite(body));
                        _readyOffset = 0;
                        continue;
                    }

                    var chunk = new byte[8192];
                    int read = sync ? _inner.Read(chunk, 0, chunk.Length) : await _inner.ReadAsync(chunk, 0, chunk.Length, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        // End of stream: flush whatever is left, then report the end.
                        if (_raw.Count == 0)
                        {
                            return 0;
                        }

                        _ready = _raw.ToArray();
                        _readyOffset = 0;
                        _raw.Clear();
                        continue;
                    }

                    for (int i = 0; i < read; i++)
                    {
                        _raw.Add(chunk[i]);
                    }
                }
            }

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _inner.Dispose();
                }

                base.Dispose(disposing);
            }
        }
    }
}
