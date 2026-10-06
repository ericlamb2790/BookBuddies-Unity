// Keeps chat kind for young readers. The word lists are the website's own (stored in rot13 so the
// source doesn't spell them out). Add your own words to the blocked_words table in D1.

const rot13 = (x) => x.replace(/[a-z]/g, (c) => String.fromCharCode((c.charCodeAt(0) - 97 + 13) % 26 + 97));
const BAD_WORDS = ['shpx', 'shpxre', 'shpxva', 'shpxvat', 'zbgureshpxre', 'fuvg', 'fuvggl', 'ohyyfuvg', 'ovgpu', 'ovgpurf', 'phag', 'gjng', 'jnaxre', 'onfgneq', 'nffubyr', 'nefrubyr', 'qvpxurnq', 'cevpx', 'chffl', 'fyhg', 'juber', 'qvyqb', 'wvmm', 'cbea', 'xxx', 'xlf', 'ergneq', 'ergneqrq', 'fcvp', 'puvax', 'tbbx', 'xvxr', 'jrgonpx', 'pbba', 'cnxv', 'enturnq', 'gbjryurnq', 'genaal', 'qlxr', 'snt', 'snttbg', 'avttre', 'avttn', 'avttref', 'avttnf'].map(rot13);
const BAD_INSIDE = ['zbgureshpxre', 'snttbg', 'avttre', 'avttn', 'jrgonpx', 'enturnq', 'gbjryurnq', 'shpx', 'fuvg'].map(rot13); // also caught inside other letters ("xxfuckxx", "f u c k")
const BAD_PHRASES = ['urvy uvgyre', 'juvgr cbjre', 'xvyy nyy', 'fvrt urvy', 'tb xvyy lbhefrys', 'xvyy lbhefrys', 'juvgr cevqr', 'enpr jne'].map(rot13).concat(['1488']);
const LEET = { 0: 'o', 1: 'i', 3: 'e', 4: 'a', 5: 's', 7: 't', 8: 'b', 9: 'g', '@': 'a', $: 's', '!': 'i', '|': 'i', '+': 't', '€': 'e' };
const normText = (x) => String(x || '').normalize('NFKD').replace(/[\u0300-\u036f]/g, '').toLowerCase().replace(/[0-9@$!|+€]/g, (c) => LEET[c] || c).replace(/[^a-z]+/g, ' ').replace(/(.)\1{2,}/g, '$1$1').trim();

/** The first word or phrase that isn't allowed, or null when the text is fine. */
export function textProblem(txt, extra = []) {
  const raw = String(txt || '').toLowerCase(); if (!raw.trim()) return null;
  const n = ' ' + normText(txt) + ' ', squashed = n.replace(/ /g, ''), dedup = (w) => w.replace(/(.)\1+/g, '$1');
  const nd = ' ' + n.trim().split(' ').map(dedup).join(' ') + ' ';
  for (const w of BAD_WORDS) { const d = dedup(w); if (n.includes(' ' + w + ' ') || nd.includes(' ' + d + ' ') || nd.includes(' ' + d + 's ') || nd.includes(' ' + d + 'es ') || nd.includes(' ' + d + 'ed ')) return w; }
  for (const w of BAD_INSIDE) if (squashed.includes(w) || (w.length > 4 && squashed.replace(/(.)\1+/g, '$1').includes(dedup(w)))) return w;
  for (const p of BAD_PHRASES) if (raw.includes(p) || n.includes(' ' + p + ' ')) return p;
  for (const p of extra) { const q = normText(p); if (!q) continue; if (raw.includes(String(p).toLowerCase()) || n.includes(' ' + q + ' ') || (q.length >= 5 && squashed.includes(q.replace(/ /g, '')))) return p; }
  return null;
}

/** Tidies a chat line: no control characters or angle brackets, single spaces, and no links. */
export const cleanText = (v, max) => String(v == null ? '' : v).replace(/[\u0000-\u001f<>]/g, ' ').replace(/\s+/g, ' ').trim().slice(0, max)
  .replace(/\b(https?:\/\/|www\.)\S+|\b[\w-]+\.(com|net|org|io|gg|co|me|app|tv|xyz|ly|gl)\b\S*/gi, '[link removed]');

/** Phone numbers and e-mail addresses are personal details kids shouldn't share in town chat. */
export const personalDetails = (t) => /[\w.+-]+@[\w-]+\.[\w.]+/.test(t) || /(\d[\s().-]?){7,}/.test(t);
