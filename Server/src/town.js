// One live copy of Pawtopia: a Durable Object that holds everyone's WebSocket and passes walking, chat,
// emotes and tricks between them. Villagers wander between the same spots the website uses, and coins,
// coin bags and gift boxes turn up around town for whoever reaches them first.

import { textProblem, cleanText, personalDetails } from './safety.js';
import { ensureSchema, blockedWords, today } from './db.js';

const TOWN_W = 80, TOWN_H = 64, CAP = 300;
const TICK_MS = 3000;
const FINDS_PER_DAY = 25;
const ITEM_COINS = { coin: 5, bag: 15, gift: 30 };
const EMOTES = ['❤️', '😂', '👋', '🎉', '😮', '📚', '✨', '💤'];
const ACTS = ['hop', 'spin', 'wave', 'dance', 'nap', 'sip', 'read', 'hug', 'boop', 'play', 'snack', 'highpaw'];

// Spots the villagers walk between: [x, y, 1 if it's a seat]. Same as the website's Pawtopia.
const SPOTS = [[35,30,0],[44,31,0],[39,37,0],[32,28,1],[33,28,1],[46,28,1],[47,28,1],[32,35,1],[46,35,1],[43,51,1],[45,51,1],[47,51,1],[49,51,1],[51,51,1],[53,51,1],
  [63,47,1],[69,47,1],[63,50,1],[69,50,1],[66,45,1],[66,52,1],[73,45,1],[14,30,0],[20,29,0],[11,36,0],[14,19,0],[60,19,0],[35,57,0],[45,57,0],[24,57,1],[25,57,1],[16,50,0],[71,36,0],[33,15,0],[48,40,0],[28,40,0]];

const VILLAGERS = [
  { n: 'Mayor Biscuit', look: { h: 32, s: 3, o: {}, sh: 'round', z: 1, f: 'cute', e: 'bear', pt: 'belly', bg: 'meadow' } },
  { n: 'Professor Inkwell', look: { h: 262, s: 3, o: {}, sh: 'tall', z: 1, f: 'cool', e: 'cat', pt: 'none', bg: 'library' } },
  { n: 'Pip the Postpet', look: { h: 200, s: 2, o: {}, sh: 'bean', z: 0, f: 'goofy', e: 'bunny', pt: 'spots', bg: 'meadow' } },
  { n: 'Marigold', look: { h: 48, s: 3, o: {}, sh: 'mochi', z: 1, f: 'cute', e: 'mouse', pt: 'stripes', bg: 'meadow' } },
  { n: 'Barista Beans', look: { h: 18, s: 3, o: {}, sh: 'pear', z: 1, f: 'cute', e: 'bear', pt: 'belly', bg: 'meadow' } },
  { n: 'Captain Pages', look: { h: 190, s: 3, o: {}, sh: 'dino', z: 2, f: 'derp', e: 'none', pt: 'spots', bg: 'beach' } },
];

const VILLAGER_LINES = ['Anyone reading something good? 📖', 'The café has fresh cocoa today ☕', 'I found a coin by the pond earlier! 🪙', 'Who wants to start a Tale with me?',
  'The garden looks lovely today 🌷', 'Just one more chapter… 🌙', 'Have you tried the Prism Pool? 🌈', 'I love a cozy reading corner', 'Spoilers are against town rules 🤫',
  'The Wheel of Wonder is spinning! 🎡', 'Hello neighbor! 👋', 'The beach is perfect for reading ⛱️'];

const pick = (list) => list[Math.floor(Math.random() * list.length)];
const clampX = (v) => Math.max(0, Math.min(TOWN_W - 1, v | 0));
const clampY = (v) => Math.max(0, Math.min(TOWN_H - 1, v | 0));

export class TownRoom {
  constructor(state, env) {
    this.state = state;
    this.env = env;
    this.players = new Map();   // connection id -> player
    this.lastSpot = new Map();  // player id -> where they were when they left
    this.villagers = new Map();
    this.items = new Map();
    this.seq = 0;
    this.timer = null;
  }

  // ---- joining ----

  async fetch(request) {
    await ensureSchema(this.env);
    const pid = request.headers.get('x-bb-pid') || '';
    const me = pid && await this.env.DB.prepare('SELECT name, is_admin, mute_until, ban_until FROM players WHERE id = ?1').bind(pid).first();
    if (!me) return new Response('Who are you?', { status: 401 });

    for (const other of this.players.values()) if (other.pid === pid) this.dropOldConnection(other);
    if (this.players.size >= CAP) return new Response('Town is full', { status: 503 });

    const [client, ws] = Object.values(new WebSocketPair());
    ws.accept();
    if (me.ban_until > Date.now()) {
      ws.send(JSON.stringify({ t: 'kicked', msg: 'An admin asked you to take a short break from town.' }));
      ws.close(4003, 'break');
      return new Response(null, { status: 101, webSocket: client });
    }

    if (!this.timer) this.wakeUp();
    const id = (++this.seq).toString(36);
    const last = this.lastSpot.get(pid) || { x: -1, y: -1 };
    const pl = { id, pid, ws, name: String(me.name).slice(0, 20), look: '', x: last.x, y: last.y, tx: last.x, ty: last.y, sit: 0,
      admin: me.is_admin ? 1 : 0, mute: me.mute_until || 0, saidAt: 0, cuteAt: 0, burst: [] };
    this.players.set(id, pl);
    ws.addEventListener('message', (e) => this.onMessage(pl, e.data).catch(() => {}));
    const bye = () => this.leave(pl);
    ws.addEventListener('close', bye);
    ws.addEventListener('error', bye);

    ws.send(JSON.stringify({
      t: 'w', id, now: Date.now(), cap: CAP, town: 'pawtopia', adm: pl.admin, theme: '', th: '',
      roster: [...this.players.values()].filter((o) => o !== pl && o.look).map(info)
        .concat([...this.villagers.values()].map((v) => ({ ...info(v), b: 1 }))),
      items: [...this.items.values()], garden: [], notes: [], me: { x: pl.x, y: pl.y },
    }));
    return new Response(null, { status: 101, webSocket: client });
  }

  dropOldConnection(other) {
    try { other.ws.close(4000, 'joined again'); } catch {}
    this.players.delete(other.id);
    this.broadcast({ t: 'bye', id: other.id });
  }

  leave(pl) {
    if (this.players.get(pl.id) !== pl) return;
    this.players.delete(pl.id);
    this.lastSpot.set(pl.pid, { x: pl.tx, y: pl.ty });
    this.broadcast({ t: 'bye', id: pl.id });
    if (!this.players.size) this.sleep();
  }

  // The room only ticks while someone is in it.
  wakeUp() {
    this.villagers = new Map();
    VILLAGERS.forEach((v, i) => {
      const [x, y] = SPOTS[(i * 7 + Math.floor(Math.random() * SPOTS.length)) % SPOTS.length];
      this.villagers.set('b' + i, { id: 'b' + i, name: v.n, look: JSON.stringify(v.look), x, y, tx: x, ty: y, sit: 0, next: Date.now() + 2000 + Math.random() * 8000 });
    });
    for (let k = 0; k < 10; k++) this.dropItem(Date.now());   // a few finds are waiting when the first pet arrives
    this.timer = setInterval(() => this.tick(), TICK_MS);
  }

  sleep() {
    clearInterval(this.timer);
    this.timer = null;
    this.villagers = new Map();
  }

  // ---- every few seconds: villagers wander and chat, finds appear and expire ----

  tick() {
    const now = Date.now();
    for (const v of this.villagers.values()) this.villagerTurn(v, now);
    for (const it of this.items.values()) if (now > it.exp) { this.items.delete(it.id); this.broadcast({ t: 'ix', id: it.id }); }
    const want = Math.min(30, 10 + this.players.size * 2);
    for (let k = 0; k < 2 && this.items.size < want; k++) this.dropItem(now);
  }

  villagerTurn(v, now) {
    if (v.sitAt && now >= v.sitAt) { v.sitAt = 0; v.sit = 1; this.broadcast({ t: 'sit', id: v.id, on: 1 }); }
    if (now < v.next) return;
    v.next = now + 7000 + Math.random() * 14000;
    const r = Math.random();
    if (r < .55) {
      const [x, y, seat] = pick(SPOTS);
      if ((x === v.tx && y === v.ty) || [...this.villagers.values()].some((o) => o !== v && o.tx === x && o.ty === y)) return;
      v.x = v.tx; v.y = v.ty; v.tx = x; v.ty = y; v.sit = 0;
      v.sitAt = seat ? now + (Math.hypot(v.tx - v.x, v.ty - v.y) / 3.2) * 1000 + 900 : 0;
      this.broadcast({ t: 'go', id: v.id, x: v.x, y: v.y, tx: v.tx, ty: v.ty, at: now });
    } else if (r < .72) this.broadcast({ t: 'emo', id: v.id, e: pick(EMOTES) });
    else if (r < .84) this.broadcast({ t: 'say', id: v.id, text: pick(VILLAGER_LINES) });
    else this.broadcast({ t: 'act', id: v.id, a: pick(['hop', 'spin', 'dance', 'wave']), to: '' });
  }

  dropItem(now) {
    const r = Math.random();
    const k = r < .7 ? 'coin' : r < .93 ? 'bag' : 'gift';
    const it = { id: 'i' + (++this.seq).toString(36), k, x: 3 + Math.floor(Math.random() * (TOWN_W - 6)), y: 4 + Math.floor(Math.random() * (TOWN_H - 10)), exp: now + (8 + Math.random() * 6) * 60e3 };
    this.items.set(it.id, it);
    this.broadcast({ t: 'item', it });
  }

  // ---- messages from players ----

  async onMessage(pl, raw) {
    if (typeof raw !== 'string' || raw.length > 4000) return;
    let m;
    try { m = JSON.parse(raw); } catch { return; }
    const now = Date.now();

    if (m.t === 'ping') return this.send(pl, { t: 'pong', c: typeof m.c === 'number' ? m.c : 0, now });
    if (m.t === 'leave') return this.broadcast({ t: 'leave', id: pl.id }, pl);
    if (m.t === 'hi') return this.hello(pl, m);
    if (!pl.look) return;

    pl.burst = pl.burst.filter((at) => now - at < 10e3);   // at most 40 messages in 10 seconds
    if (pl.burst.length > 40) return;
    pl.burst.push(now);

    switch (m.t) {
      case 'go': return this.walk(pl, m, now);
      case 'sit': pl.sit = m.on ? 1 : 0; return this.broadcast({ t: 'sit', id: pl.id, on: pl.sit, f: m.f ? 1 : 0 }, pl);
      case 'emo': if (EMOTES.includes(m.e) && !this.isMuted(pl, now)) this.broadcast({ t: 'emo', id: pl.id, e: m.e }, pl); return;
      case 'act': return this.act(pl, m, now);
      case 'say': return this.say(pl, m, now);
      case 'claim': return this.claim(pl, m);
    }
  }

  hello(pl, m) {
    const first = !pl.look;
    pl.look = String(m.look || '').slice(0, 1500);
    pl.x = pl.tx = clampX(m.x);
    pl.y = pl.ty = clampY(m.y);
    this.broadcast(first ? { t: 'join', p: info(pl) } : { t: 'look', id: pl.id, look: pl.look }, pl);
  }

  walk(pl, m, now) {
    const tenth = (v, max) => Math.max(0, Math.min(max - 1, Math.round((+v || 0) * 10) / 10));
    pl.x = tenth(m.x, TOWN_W); pl.y = tenth(m.y, TOWN_H);
    pl.tx = clampX(m.tx); pl.ty = clampY(m.ty); pl.sit = 0;
    this.broadcast({ t: 'go', id: pl.id, x: pl.x, y: pl.y, tx: pl.tx, ty: pl.ty, at: now }, pl);
  }

  act(pl, m, now) {
    if (!ACTS.includes(m.a)) return;
    const to = typeof m.to === 'string' && m.to.length < 13 ? m.to : '';
    if (to) {                                   // hugs and boops: one every 1.5 seconds
      if (this.isMuted(pl, now) || now - pl.cuteAt < 1500) return;
      pl.cuteAt = now;
    }
    this.broadcast({ t: 'act', id: pl.id, a: m.a, to }, pl);
  }

  async say(pl, m, now) {
    if (this.isMuted(pl, now)) return;
    if (now - pl.saidAt < 1200) return this.error(pl, 'Slow down a little 🐢');
    const text = cleanText(m.text, 140);
    if (!text) return;
    if (textProblem(text, await blockedWords(this.env))) return this.error(pl, 'Please keep it kind. That word or phrase isn’t allowed on BookBuddies.');
    if (personalDetails(text)) return this.error(pl, 'Keep phone numbers and e-mail addresses private 💛');
    pl.saidAt = now;
    this.broadcast({ t: 'say', id: pl.id, text });
  }

  /** Whoever reaches a coin, bag or gift first keeps it (up to 25 finds a day). */
  async claim(pl, m) {
    const it = this.items.get(String(m.id || ''));
    if (!it || Math.abs(pl.tx - it.x) > 1.5 || Math.abs(pl.ty - it.y) > 1.5) return;
    this.items.delete(it.id);
    this.broadcast({ t: 'ix', id: it.id, by: pl.id });
    const day = today();
    const found = await this.env.DB.prepare('SELECT n FROM finds WHERE player_id = ?1 AND day = ?2').bind(pl.pid, day).first();
    let coins = 0;
    if (!found || found.n < FINDS_PER_DAY) {
      coins = ITEM_COINS[it.k] || 5;
      await this.env.DB.batch([
        this.env.DB.prepare('INSERT INTO finds (player_id, day, n) VALUES (?1, ?2, 1) ON CONFLICT (player_id, day) DO UPDATE SET n = n + 1').bind(pl.pid, day),
        this.env.DB.prepare('UPDATE players SET coins = coins + ?2 WHERE id = ?1').bind(pl.pid, coins),
      ]);
    }
    this.send(pl, { t: 'got', k: it.k, coins });
  }

  isMuted(pl, now) {
    if (pl.mute <= now) return false;
    const mins = Math.ceil((pl.mute - now) / 60e3);
    this.error(pl, `An admin paused your chat for ${mins < 60 ? mins + ' more min' : Math.ceil(mins / 60) + ' more hours'}.`);
    return true;
  }

  // ---- sending ----

  send(pl, o) { try { pl.ws.send(JSON.stringify(o)); } catch {} }
  error(pl, msg) { this.send(pl, { t: 'err', msg }); }

  broadcast(o, skip) {
    const text = JSON.stringify(o);
    for (const p of this.players.values()) if (p !== skip) { try { p.ws.send(text); } catch {} }
  }
}

const info = (o) => ({ id: o.id, n: o.name, u: '', c: '', look: o.look, x: o.x, y: o.y, tx: o.tx, ty: o.ty, sit: o.sit || 0 });
