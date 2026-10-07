// A live Tales party (worker.js:7556-7585): one room per shared tale, "qs:<id>", run by the TownRoom class (town.js
// hands it every socket index.js marks x-bb-kind: quest). The first player in leads: their game runs the story and
// streams it to the rest, whose inputs go only to the leader. When the leader leaves or hides the app, a visible
// member takes over.

import { cleanSeed } from './quest.js';

const CAP = 12;

/** Joins a player to the tale's room (kept on room.quest) and answers with the WebSocket. */
export function questJoin(room, request) {
  const q = room.quest || (room.quest = { p: new Map(), lead: null, s: null, seq: 0 });
  const pid = request.headers.get('x-bb-pid') || '';
  for (const o of q.p.values()) if (o.pid === pid) {          // the same player again: the old socket goes quietly
    try { o.ws.close(4000, 'joined again'); } catch {}
    q.p.delete(o.id);
  }
  if (q.p.size >= CAP) return new Response('This party is full', { status: 503 });

  const [client, ws] = Object.values(new WebSocketPair());
  ws.accept();
  const id = (++q.seq).toString(36);
  const pl = { id, pid, ws, name: decodeURIComponent(request.headers.get('x-bb-name') || 'Reader').slice(0, 20), pet: null, vis: true };
  q.p.set(id, pl);
  if (!q.p.has(q.lead)) q.lead = id;
  ws.addEventListener('message', (e) => onMessage(q, pl, e.data));
  const bye = () => {
    if (q.p.get(id) !== pl) return;
    q.p.delete(id);
    if (q.lead === id) { const next = pick(q); q.lead = next ? next.id : null; }
    all(q, { t: 'bye', id, pid, lead: leadPid(q) });
  };
  ws.addEventListener('close', bye);
  ws.addEventListener('error', bye);
  send(pl, { t: 'w', id, lead: leadPid(q), s: q.s, who: [...q.p.values()].filter((o) => o !== pl).map((o) => ({ id: o.id, pid: o.pid, n: o.name, pet: o.pet })) });
  return new Response(null, { status: 101, webSocket: client });
}

function onMessage(q, pl, raw) {
  if (typeof raw !== 'string' || raw.length > 400000) return;
  let m;
  try { m = JSON.parse(raw); } catch { return; }
  if (!m || typeof m !== 'object') return;
  const lead = q.p.get(q.lead), leading = lead === pl;
  const toLead = (o) => { if (lead && !leading) send(lead, { ...o, from: pl.pid, n: pl.name }); };

  switch (m.t) {
    case 'ping': return send(pl, { t: 'pong' });
    case 'hi': pl.pet = cleanSeed(m.pet, 8000); return all(q, { t: 'join', id: pl.id, pid: pl.pid, n: pl.name, pet: pl.pet }, pl);
    // the leader's story: the whole run (kept for whoever joins next) and each battle step
    case 'state': if (leading) { q.s = m.s; all(q, { t: 'state', s: m.s }, pl); } return;
    case 'ev': if (leading) all(q, { t: 'ev', b: m.b, i: m.i, e: m.e, u: m.u }, pl); return;
    // a member's choices reach the leader only
    case 'rdy': return toLead({ t: 'rdy', k: ['boss', 'go', 'quit'].includes(m.k) ? m.k : 'boss', on: m.on ? 1 : 0 });
    case 'vote': case 'ult': case 'hold': case 'card': return toLead({ t: m.t, i: m.i | 0, k: m.k === 'book' ? 'book' : 'menu', on: m.on ? 1 : 0 });
    case 'tac': return toLead({ t: 'tac', o: Array.isArray(m.o) ? m.o.filter((k) => typeof k === 'string').slice(0, 14).map((k) => k.slice(0, 24)) : null,
      ua: m.ua !== false, pos: ['l', 'c', 'r'].includes(m.pos) ? m.pos : 'c' });
    case 'wreq': return toLead({ t: 'wreq' });
    case 'emo': return all(q, { t: 'emo', k: Math.max(0, Math.min(4, m.k | 0)), from: pl.pid, n: pl.name }, pl);
    // a leader whose app went to the background hands the story to someone watching
    case 'vis': {
      pl.vis = !!m.v;
      const next = !pl.vis && leading && pick(q, pl);
      if (next && next.vis) { q.lead = next.id; all(q, { t: 'lead', pid: next.pid }); }
    }
  }
}

// the first visible member other than "not", else anyone
const pick = (q, not) => { const c = [...q.p.values()].filter((o) => o !== not); return c.find((o) => o.vis) || c[0] || null; };
const leadPid = (q) => { const L = q.p.get(q.lead); return L ? L.pid : null; };
const send = (pl, o) => { try { pl.ws.send(JSON.stringify(o)); } catch {} };
const all = (q, o, skip) => { const t = JSON.stringify(o); for (const p of q.p.values()) if (p !== skip) { try { p.ws.send(t); } catch {} } };
