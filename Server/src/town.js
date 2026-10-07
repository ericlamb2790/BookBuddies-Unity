// One live room of a place (Pawtopia, a town on Bramble Road, a road link or the Inkwell Caves): a Durable Object
// that holds everyone's WebSocket and passes walking, chat, emotes and tricks between them. In every town, coins,
// coin bags and gift boxes turn up for whoever reaches them first (paid into the coin ledger, see wallet.js), and in
// Pawtopia villagers wander between the same spots the website uses.
// The road and the caves are quieter: just the readers (their foes live on each player's own device).
// The admin tools reach a room through POST https://room/admin (kick, mute, unmute).
// A shared Tale's live party is a room of this class too: tales_room.js runs it.

import { textProblem, cleanText, personalDetails } from './safety.js';
import { ensureSchema, blockedWords, GENRE_TOWNS } from './db.js';
import { credit, balance, todayCount, etDay } from './wallet.js';
import ECONOMY from './economy.json';
import { questJoin } from './tales_room.js';

const CAP = 300;
// each place's size in tiles (the site's road rooms were stuck at 64×46 and pinned anyone past x 63 to the edge);
// finds: coins and gifts turn up there (every town); life: villagers wander there (Pawtopia)
const PLACES = { pawtopia: { w: 80, h: 64, life: true, finds: true }, caves: { w: 56, h: 40 } };
GENRE_TOWNS.forEach((k, i) => { PLACES[k] = { w: 56, h: 44, finds: true }; PLACES['road' + (i + 1)] = { w: 112, h: 52 }; });
const TICK_MS = 3000;
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

/**
 * Which place a connection is for. index.js names the room "<town>:<shard>" on the signed ticket (already checked
 * there), so it's read back from x-bb-room when set, else from the ticket's last field; Pawtopia otherwise.
 */
function placeOf(request) {
  let room = request.headers.get('x-bb-room') || '';
  if (!room) {
    try {
      const ticket = new URL(request.url).searchParams.get('ticket') || '';
      const body = atob(ticket.split('.')[0].replace(/-/g, '+').replace(/_/g, '/'));
      room = body.split('|').pop();
    } catch { room = ''; }
  }
  const town = room.split(':')[0];
  return PLACES[town] ? town : 'pawtopia';
}

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
    this.town = 'pawtopia';
  }

  get place() { return PLACES[this.town]; }
  clampX(v) { return Math.max(0, Math.min(this.place.w - 1, v | 0)); }
  clampY(v) { return Math.max(0, Math.min(this.place.h - 1, v | 0)); }

  // ---- joining ----

  async fetch(request) {
    if (new URL(request.url).pathname === '/admin' && request.method === 'POST') return this.admin(request);
    if (request.headers.get('x-bb-kind') === 'quest') return questJoin(this, request);
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

    if (!this.players.size) this.town = placeOf(request);
    if (!this.timer) this.wakeUp();
    const id = (++this.seq).toString(36);
    const last = this.lastSpot.get(pid) || { x: -1, y: -1 };
    const pl = { id, pid, ws, name: String(me.name).slice(0, 20), look: '', x: last.x, y: last.y, tx: last.x, ty: last.y, sit: 0,
      admin: me.is_admin ? 1 : 0, mute: me.mute_until || 0, saidAt: 0, cuteAt: 0, taleAt: 0, burst: [] };
    this.players.set(id, pl);
    ws.addEventListener('message', (e) => this.onMessage(pl, e.data).catch(() => {}));
    const bye = () => this.leave(pl);
    ws.addEventListener('close', bye);
    ws.addEventListener('error', bye);

    ws.send(JSON.stringify({
      t: 'w', id, now: Date.now(), cap: CAP, town: this.town, adm: pl.admin, theme: '', th: '',
      roster: [...this.players.values()].filter((o) => o !== pl && o.look).map((o) => info(o, pl.admin))
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

  // ---- the admin tools (index.js / admin.js call this for every room, so it reaches the player wherever they are) ----

  /** {action: 'kick' | 'mute' | 'unmute', pid, msg?, until?}: kick sends {t:'kicked', msg} and closes the player's sockets. */
  async admin(request) {
    let m;
    try { m = await request.json(); } catch { return new Response('Bad request', { status: 400 }); }
    const mine = [...this.players.values()].filter((p) => p.pid === String(m.pid || ''));
    if (m.action === 'kick') {
      const msg = String(m.msg || 'An admin sent you home from town for now. 💛').slice(0, 300);
      for (const p of mine) {
        this.send(p, { t: 'kicked', msg });
        try { p.ws.close(4003, 'break'); } catch {}
        this.leave(p);
      }
    } else if (m.action === 'mute') for (const p of mine) p.mute = +m.until || 0;
    else if (m.action === 'unmute') for (const p of mine) p.mute = 0;
    else return new Response('Unknown action', { status: 400 });
    return new Response(JSON.stringify({ ok: true, found: mine.length }), { headers: { 'content-type': 'application/json' } });
  }

  // The room only ticks while someone is in it, and only in towns (finds); only Pawtopia has villagers.
  wakeUp() {
    this.villagers = new Map();
    if (!this.place.finds) return;
    if (this.place.life) VILLAGERS.forEach((v, i) => {
      const [x, y] = SPOTS[(i * 7 + Math.floor(Math.random() * SPOTS.length)) % SPOTS.length];
      this.villagers.set('b' + i, { id: 'b' + i, name: v.n, look: JSON.stringify(v.look), x, y, tx: x, ty: y, sit: 0, next: Date.now() + 2000 + Math.random() * 8000 });
    });
    for (let k = 0; k < 10; k++) this.dropItem(Date.now());   // a few finds are waiting when the first pet arrives
    this.timer = setInterval(() => this.tick(), TICK_MS);
  }

  sleep() {
    if (this.timer) clearInterval(this.timer);
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
    const { w, h } = this.place;
    const it = { id: 'i' + (++this.seq).toString(36), k, x: 3 + Math.floor(Math.random() * (w - 6)), y: 4 + Math.floor(Math.random() * (h - 10)), exp: now + (8 + Math.random() * 6) * 60e3 };
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
      case 'tale': return this.invite(pl, m, now);
    }
  }

  hello(pl, m) {
    const first = !pl.look;
    pl.look = String(m.look || '').slice(0, 1500);
    pl.x = pl.tx = this.clampX(m.x);
    pl.y = pl.ty = this.clampY(m.y);
    if (!first) return this.broadcast({ t: 'look', id: pl.id, look: pl.look }, pl);
    for (const p of this.players.values()) if (p !== pl) this.send(p, { t: 'join', p: info(pl, p.admin) });
  }

  walk(pl, m, now) {
    const tenth = (v, max) => Math.max(0, Math.min(max - 1, Math.round((+v || 0) * 10) / 10));
    pl.x = tenth(m.x, this.place.w); pl.y = tenth(m.y, this.place.h);
    pl.tx = this.clampX(m.tx); pl.ty = this.clampY(m.ty); pl.sit = 0;
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

  /** Whoever reaches a coin, bag or gift first keeps it: paid into the coin ledger, up to 25 finds a day. "bal" is the new balance. */
  async claim(pl, m) {
    const it = this.items.get(String(m.id || ''));
    if (!it || Math.abs(pl.tx - it.x) > 1.5 || Math.abs(pl.ty - it.y) > 1.5) return;
    this.items.delete(it.id);
    this.broadcast({ t: 'ix', id: it.id, by: pl.id });
    const { finds } = ECONOMY;
    let coins = 0;
    if ((await todayCount(this.env, pl.pid, 'find')).n < finds.perDay) {
      coins = finds.coins[it.k] || finds.coins.coin;
      if (!(await credit(this.env, pl.pid, 'find', `${etDay()}:${it.id}:${Math.floor(it.exp)}`, coins))) coins = 0;
    }
    this.send(pl, { t: 'got', k: it.k, coins, bal: await balance(this.env, pl.pid) });
  }

  /** "Invite to my tale": {t:'tale', to: conn id, id, title} reaches only that player as {t:'tale', from, id, title}; one every 10 s. */
  async invite(pl, m, now) {
    const to = this.players.get(String(m.to || '')), id = String(m.id || ''), title = cleanText(m.title, 60);
    if (!to || to === pl || !/^[\w-]{8}$/.test(id) || now - pl.taleAt < 10e3 || this.isMuted(pl, now)) return;
    if (textProblem(title, await blockedWords(this.env))) return this.error(pl, 'Please keep it kind. That word or phrase isn’t allowed on BookBuddies.');
    pl.taleAt = now;
    this.send(to, { t: 'tale', from: pl.name, id, title });
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

// What others see of a pet. Only admins get the account id (u), so the town card's admin tools reach the right account.
const info = (o, forAdmin) => ({ id: o.id, n: o.name, u: forAdmin && o.pid ? o.pid : '', c: '', look: o.look, x: o.x, y: o.y, tx: o.tx, ty: o.ty, sit: o.sit || 0 });
