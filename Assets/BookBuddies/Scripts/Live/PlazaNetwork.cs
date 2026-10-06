using System;
using System.Collections.Generic;
using BookBuddies.Net;
using UnityEngine;

namespace BookBuddies.Live
{
    public enum LiveState { Offline, Connecting, Live, Busy, Reconnecting, Elsewhere, SentHome }

    /// <summary>
    /// Keeps you connected to the live town, the same way the website does:
    /// get a ticket for this place (Pawtopia, the road or the caves), open the room's WebSocket, try the next
    /// room (shard 1-6) when one is full, rejoin quickly with a 15-minute pass, back off between retries, and
    /// ping every 5 seconds to measure lag and line up with the server clock.
    /// </summary>
    public sealed class PlazaNetwork
    {
        const int MaxShard = 6;
        const float PingEvery = 5f;

        public LiveState State { get; private set; } = LiveState.Offline;
        public float RoundTripMs { get; private set; }
        public string MyId { get; private set; }
        public bool IsAdmin { get; private set; }
        /// <summary>Why you were sent home (the admin's message, or how long the break lasts), or null.</summary>
        public string Notice { get; private set; }
        public bool IsLive => State == LiveState.Live && live != null && live.Open;

        public event Action<Dictionary<string, object>> Welcome;
        public event Action<Dictionary<string, object>> Message;
        public event Action<LiveState> StateChanged;

        readonly string town;
        WorldSocket live, trying;
        int shard = 1, retries;
        float retryAt = -1, nextPing;
        string pass;
        double passExpires;
        double clockOffset; // server time minus local time, in ms
        bool running;

        public PlazaNetwork(string town) { this.town = town; }

        /// <summary>Server time now, in Unix milliseconds.</summary>
        public double ServerNow => LocalNow + clockOffset;
        static double LocalNow => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        public void Start()
        {
            running = true;
            Connect();
        }

        public void Stop()
        {
            running = false;
            Send(new Dictionary<string, object> { ["t"] = "leave" });
            live?.Dispose(); trying?.Dispose();
            live = trying = null;
            SetState(LiveState.Offline);
        }

        public void Send(Dictionary<string, object> message)
        {
            if (live != null && live.Open) live.Send(Json.Write(message));
        }

        /// <summary>Call every frame.</summary>
        public void Update()
        {
            trying?.Poll();
            if (live != null && live != trying) live.Poll();
            if (retryAt > 0 && Time.unscaledTime >= retryAt) { retryAt = -1; Connect(); }
            if (IsLive && Time.unscaledTime >= nextPing)
            {
                nextPing = Time.unscaledTime + PingEvery;
                Send(new Dictionary<string, object> { ["t"] = "ping", ["c"] = (double)(Time.realtimeSinceStartup * 1000) });
            }
        }

        async void Connect()
        {
            if (!running) return;
            if (!Settings.SignedIn) { SetState(LiveState.Offline); return; }
            SetState(LiveState.Connecting);

            string ticket;
            bool viaPass = pass != null && passExpires - LocalNow > 60000;
            if (viaPass) ticket = pass;
            else
            {
                Dictionary<string, object> t;
                try { t = await BBApi.WorldTicket(shard, town); }
                catch (Exception) { SetState(LiveState.Offline); RetryLater(); return; }
                if (!t.Truthy("live"))
                {
                    // on a break: the server says for how long, and there's no point retrying
                    bool onBreak = t.Str("reason") == "break";
                    if (onBreak) Notice = t.Str("msg", null);
                    SetState(onBreak ? LiveState.SentHome : LiveState.Offline);
                    return;
                }
                ticket = t.Str("ticket");
                if (t.Has("pass")) { pass = t.Str("pass"); passExpires = t.Num("passExp", LocalNow + 9e5); }
            }
            if (!running) return;

            var socket = new WorldSocket();
            bool welcomed = false;
            trying = socket;
            socket.OnMessage += text =>
            {
                Dictionary<string, object> m;
                try { m = Json.ParseObject(text); } catch (FormatException) { return; }
                if (m == null) return;
                if (m.Str("t") == "w") { welcomed = true; OnWelcome(socket, m); }
                else if (m.Str("t") == "kicked") Notice = m.Str("msg", null); // the socket closes with 4003 next
                else if (socket == live) OnMessage(m);
            };
            socket.OnClosed += code => OnClosed(socket, code, welcomed, viaPass);
            await socket.Connect(BBApi.LiveUrl(ticket, town, shard, viaPass));
        }

        void OnWelcome(WorldSocket socket, Dictionary<string, object> m)
        {
            if (live != null && live != socket) live.Dispose();
            live = socket;
            retries = 0;
            MyId = m.Str("id");
            IsAdmin = m.Truthy("adm");
            clockOffset = m.Num("now", LocalNow) - LocalNow;
            nextPing = 0;
            SetState(LiveState.Live);
            Welcome?.Invoke(m);
        }

        void OnMessage(Dictionary<string, object> m)
        {
            if (m.Str("t") == "pong")
            {
                float rtt = Time.realtimeSinceStartup * 1000 - (float)m.Num("c");
                if (rtt <= 0 || rtt >= 10000) return;
                RoundTripMs = RoundTripMs > 0 ? RoundTripMs * .7f + rtt * .3f : rtt;
                double offset = m.Num("now", LocalNow) + rtt / 2 - LocalNow;
                clockOffset = clockOffset * .8 + offset * .2;
                StateChanged?.Invoke(State); // refresh the lag badge
                return;
            }
            Message?.Invoke(m);
        }

        void OnClosed(WorldSocket socket, int code, bool welcomed, bool viaPass)
        {
            bool wasLive = socket == live;
            if (wasLive) live = null;
            if (socket == trying) trying = null;
            socket.Dispose();
            if (!running || (!wasLive && welcomed)) return;

            if (!welcomed && viaPass && code != 4000 && code != 4003) { pass = null; Connect(); return; }
            if (code == 4000) { SetState(LiveState.Elsewhere); return; }
            if (code == 4003) { SetState(LiveState.SentHome); return; }
            if (!welcomed && shard < MaxShard) { shard++; SetState(LiveState.Busy); Connect(); return; }
            SetState(welcomed ? LiveState.Reconnecting : LiveState.Offline);
            RetryLater();
        }

        void RetryLater()
        {
            retries = Math.Min(6, retries + 1);
            retryAt = Time.unscaledTime + 1.5f * Mathf.Pow(1.7f, retries);
        }

        void SetState(LiveState s)
        {
            State = s;
            StateChanged?.Invoke(s);
        }
    }
}
