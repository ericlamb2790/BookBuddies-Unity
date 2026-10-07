// The hosting PC's end of one visitor's live-town WebSocket: RFC 6455's server side over the TCP stream LocalHost
// accepted, after the handshake. Only what town.js needs: masked text messages in, text messages out, ping and close.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BookBuddies.Local
{
    /// <summary>
    /// One visitor's WebSocket on the host. Run reads their frames on a background task and queues each text message in
    /// Inbox for LocalHost.Pump; SendText and Close work from any thread, and one background writer sends in order.
    /// A visitor gets text messages of up to 16 KB and must send something every 2 minutes, or the socket closes with
    /// the matching code (1009 too big, 1003 binary, 1001 gone quiet). Past 20 frames a second whole messages are
    /// dropped (a burst of clicks), and past 60 the socket closes with 1008.
    /// </summary>
    sealed class HostSocket
    {
        public const int MaxMessage = 16 * 1024;
        const int FramesPerSecond = 20, CloseAbove = 60;
        const int IdleMs = 120000;          // no frame for this long: closed
        const int CloseWaitMs = 5000;       // how long a close waits for the visitor's reply before hanging up
        const int MaxBacklog = 2 << 20;     // bytes waiting to go out before a visitor who stopped reading is dropped
        const int OpContinue = 0, OpText = 1, OpBinary = 2, OpClose = 8, OpPing = 9, OpPong = 10;
        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>Whole text messages from the visitor, oldest first, waiting for Pump.</summary>
        public readonly ConcurrentQueue<string> Inbox = new ConcurrentQueue<string>();

        readonly Stream stream;
        readonly Action hangUp;
        readonly byte[] early;  // bytes that arrived with the handshake
        int earlyAt;
        readonly ConcurrentQueue<byte[]> outbox = new ConcurrentQueue<byte[]>();
        readonly SemaphoreSlim waiting = new SemaphoreSlim(0);
        readonly Task writer;
        int backlog, closing, aborted;
        int windowAt, windowFrames; // this second's frames
        volatile bool ended;

        /// <summary>The connection is gone: Pump lets the visitor's link go.</summary>
        public bool Ended => ended;

        /// <summary>A socket on a stream that has just answered the handshake; "hangUp" closes the TCP connection.</summary>
        public HostSocket(Stream stream, Action hangUp, byte[] early)
        {
            this.stream = stream;
            this.hangUp = hangUp;
            this.early = early ?? new byte[0];
            windowAt = Environment.TickCount; // TickCount goes negative after 25 days up, so the window starts from it, not 0
            writer = Task.Run(Write);
        }

        /// <summary>Whether a close code may go in a close frame (1005, 1006 and 1015 only ever describe a close).</summary>
        public static bool Sendable(int code) =>
            (code >= 3000 && code <= 4999) || (code >= 1000 && code <= 1014 && code != 1004 && code != 1005 && code != 1006);

        /// <summary>Sends one text message; dropped once the socket is closing.</summary>
        public void SendText(string text)
        {
            if (Volatile.Read(ref closing) == 0) Queue(Frame(OpText, Encoding.UTF8.GetBytes(text)));
        }

        /// <summary>Starts the closing handshake with this code (the first close wins); hangs up if no reply comes in 5 s.</summary>
        public void Close(int code)
        {
            if (Interlocked.Exchange(ref closing, 1) == 1) return;
            Queue(Frame(OpClose, new[] { (byte)(code >> 8), (byte)code }));
            Task.Delay(CloseWaitMs).ContinueWith(_ => Abort(), TaskScheduler.Default);
        }

        /// <summary>Reads the visitor's frames until the connection ends. Never throws.</summary>
        public async Task Run()
        {
            var idle = new Timer(_ => Close(1001), null, IdleMs, Timeout.Infinite);
            var head = new byte[8];
            var message = new MemoryStream();
            bool inMessage = false; // a fragmented text message has begun
            try
            {
                while (true)
                {
                    await Fill(head, 2).ConfigureAwait(false);
                    bool fin = (head[0] & 0x80) != 0, rsv = (head[0] & 0x70) != 0, masked = (head[1] & 0x80) != 0;
                    int op = head[0] & 0x0F;
                    long length = head[1] & 0x7F;
                    if (length == 126)
                    {
                        await Fill(head, 2).ConfigureAwait(false);
                        length = head[0] << 8 | head[1];
                    }
                    else if (length == 127)
                    {
                        await Fill(head, 8).ConfigureAwait(false);
                        length = 0;
                        for (int k = 0; k < 8; k++) length = length << 8 | head[k]; // negative when the top bit is set: not allowed
                    }
                    if (length < 0) return;
                    idle.Change(IdleMs, Timeout.Infinite);

                    bool shut = Volatile.Read(ref closing) == 1;
                    int problem = shut ? 0
                        : rsv || !masked ? 1002
                        : op >= 8 ? (!fin || length > 125 || op > OpPong ? 1002 : 0)
                        : op == OpBinary ? 1003
                        : op != OpText && op != OpContinue ? 1002
                        : (op == OpContinue) != inMessage ? 1002
                        : message.Length + length > MaxMessage ? 1009
                        : 0;
                    bool drop = false;
                    if (!shut && problem == 0)
                    {
                        int n = FramesThisSecond();
                        if (n > CloseAbove) problem = 1008;
                        else drop = n > FramesPerSecond && fin && op == OpText && !inMessage; // a whole message in a burst
                    }
                    if (drop)
                    {
                        await Skip((masked ? 4 : 0) + length).ConfigureAwait(false);
                        continue;
                    }
                    if (shut || problem != 0)
                    {
                        // once closing, frames are skipped until the visitor's close arrives
                        if (problem != 0) Close(problem);
                        await Skip((masked ? 4 : 0) + length).ConfigureAwait(false);
                        if (op == OpClose) break; // their close: both sides have now sent one
                        continue;
                    }

                    var key = new byte[4];
                    await Fill(key, 4).ConfigureAwait(false);
                    var payload = new byte[length];
                    await Fill(payload, payload.Length).ConfigureAwait(false);
                    for (int k = 0; k < payload.Length; k++) payload[k] ^= key[k & 3];

                    if (op == OpClose)
                    {
                        int code = payload.Length >= 2 ? payload[0] << 8 | payload[1] : 1000;
                        Close(payload.Length == 1 || !Sendable(code) ? 1002 : code); // the reply to their close
                        break;
                    }
                    if (op == OpPing) Queue(Frame(OpPong, payload));
                    if (op >= 8) continue;

                    message.Write(payload, 0, payload.Length);
                    inMessage = !fin;
                    if (!fin) continue;
                    string text = null;
                    try { text = StrictUtf8.GetString(message.GetBuffer(), 0, (int)message.Length); }
                    catch (ArgumentException) { Close(1007); }
                    message.SetLength(0);
                    if (text != null) Inbox.Enqueue(text);
                }
                // both closes have been seen: let ours finish going out, then hang up
                await Task.WhenAny(writer, Task.Delay(CloseWaitMs)).ConfigureAwait(false);
            }
            catch (Exception) { } // the visitor hung up, or a timeout or Stop cut the connection
            finally
            {
                ended = true;
                idle.Dispose();
                Abort();
            }
        }

        /// <summary>Hangs up now, without a closing handshake.</summary>
        public void Abort()
        {
            if (Interlocked.Exchange(ref aborted, 1) == 1) return;
            hangUp();
            waiting.Release(); // wakes the writer so it can finish
        }

        // frames so far in this second, this one included
        int FramesThisSecond()
        {
            int now = Environment.TickCount;
            if (unchecked(now - windowAt) >= 1000)
            {
                windowAt = now;
                windowFrames = 0;
            }
            return ++windowFrames;
        }

        // ---- reading ----

        // exactly "count" bytes: the handshake's leftovers first, then the stream
        async Task Fill(byte[] into, int count)
        {
            for (int at = 0; at < count;)
            {
                int got;
                if (earlyAt < early.Length)
                {
                    got = Math.Min(count - at, early.Length - earlyAt);
                    Buffer.BlockCopy(early, earlyAt, into, at, got);
                    earlyAt += got;
                }
                else got = await stream.ReadAsync(into, at, count - at).ConfigureAwait(false);
                if (got == 0) throw new EndOfStreamException();
                at += got;
            }
        }

        async Task Skip(long count)
        {
            var scratch = new byte[4096];
            for (; count > 0; count -= scratch.Length)
                await Fill(scratch, (int)Math.Min(count, scratch.Length)).ConfigureAwait(false);
        }

        // ---- writing ----

        void Queue(byte[] frame)
        {
            if (Interlocked.Add(ref backlog, frame.Length) > MaxBacklog) { Abort(); return; } // they stopped reading
            outbox.Enqueue(frame);
            waiting.Release();
        }

        // sends the queued frames in order; stops after the close frame
        async Task Write()
        {
            try
            {
                while (Volatile.Read(ref aborted) == 0)
                {
                    await waiting.WaitAsync().ConfigureAwait(false);
                    if (!outbox.TryDequeue(out var frame)) continue;
                    Interlocked.Add(ref backlog, -frame.Length);
                    await stream.WriteAsync(frame, 0, frame.Length).ConfigureAwait(false);
                    if ((frame[0] & 0x0F) == OpClose) return;
                }
            }
            catch (Exception) { Abort(); }
        }

        // one unmasked, unfragmented frame
        static byte[] Frame(int op, byte[] payload)
        {
            int n = payload.Length, at = n < 126 ? 2 : n < 65536 ? 4 : 10;
            var frame = new byte[at + n];
            frame[0] = (byte)(0x80 | op);
            if (n < 126) frame[1] = (byte)n;
            else if (n < 65536)
            {
                frame[1] = 126;
                frame[2] = (byte)(n >> 8);
                frame[3] = (byte)n;
            }
            else
            {
                frame[1] = 127;
                for (int k = 0; k < 8; k++) frame[2 + k] = (byte)((long)n >> (56 - 8 * k));
            }
            Buffer.BlockCopy(payload, 0, frame, at, n);
            return frame;
        }
    }
}
