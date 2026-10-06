using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BookBuddies.Net
{
    /// <summary>
    /// A WebSocket to the town's Durable Object room. Messages arrive on a background thread and are
    /// handed to the game on the main thread by Poll(), so game code never has to think about threads.
    /// (Works on desktop and mobile builds. WebGL would need a browser WebSocket bridge instead.)
    /// </summary>
    public sealed class WorldSocket : IDisposable
    {
        public event Action<string> OnMessage;
        public event Action<int> OnClosed; // close code: 4000 = signed in elsewhere, 4003 = sent home by an admin

        readonly ClientWebSocket ws = new ClientWebSocket();
        readonly CancellationTokenSource stop = new CancellationTokenSource();
        readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();
        readonly SemaphoreSlim sending = new SemaphoreSlim(1, 1);
        int closeCode = -1;
        bool closeReported;

        public bool Open => ws.State == WebSocketState.Open;

        public async Task Connect(string url)
        {
            try
            {
                await ws.ConnectAsync(new Uri(url), stop.Token);
                _ = ReceiveLoop();
            }
            catch (Exception)
            {
                closeCode = 1006;
            }
        }

        /// <summary>Call every frame: delivers waiting messages, then the close notice if the socket ended.</summary>
        public void Poll()
        {
            while (inbox.TryDequeue(out var text)) OnMessage?.Invoke(text);
            if (closeCode >= 0 && !closeReported) { closeReported = true; OnClosed?.Invoke(closeCode); }
        }

        public void Send(string json)
        {
            if (!Open) return;
            _ = SendAsync(json);
        }

        async Task SendAsync(string json)
        {
            await sending.WaitAsync();
            try { await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), WebSocketMessageType.Text, true, stop.Token); }
            catch (Exception) { /* the receive loop reports the close */ }
            finally { sending.Release(); }
        }

        async Task ReceiveLoop()
        {
            var buffer = new byte[16 * 1024];
            var message = new StringBuilder();
            try
            {
                while (ws.State == WebSocketState.Open)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), stop.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        closeCode = (int)(result.CloseStatus ?? WebSocketCloseStatus.NormalClosure);
                        return;
                    }
                    message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    if (!result.EndOfMessage) continue;
                    inbox.Enqueue(message.ToString());
                    message.Clear();
                }
            }
            catch (Exception) { }
            if (closeCode < 0) closeCode = 1006;
        }

        public void Dispose()
        {
            stop.Cancel();
            try { if (ws.State == WebSocketState.Open) ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch (Exception) { }
            ws.Dispose();
        }
    }
}
