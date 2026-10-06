# BookBuddies Unity server (a new Cloudflare Worker)

This is a **new, separate Worker** for the Unity game. It has its own **D1 database** for players and its own **Durable Objects** for the live town. It doesn't touch the website's Worker or its database.

| What | Where |
|---|---|
| Accounts, sign-in, recovery codes | `src/index.js` |
| Live town rooms (one Durable Object per copy of Pawtopia) | `src/town.js` |
| Tables and daily limits (D1) | `src/db.js`, also as plain SQL in `schema.sql` |
| Kind-chat filter (the website's word lists) | `src/safety.js` |
| Cloudflare settings | `wrangler.toml` |

## Set it up (once)

You need a free Cloudflare account and **Node.js 20 or newer** (from nodejs.org, the LTS download).

1. This `Server` folder sits next to `Assets` in the Unity project (Unity ignores it). You can keep it there or move it anywhere outside the website's repo.
2. Open a terminal in that folder and run:
   ```
   npm install
   npx wrangler login
   ```
   A browser window opens. Allow access to your Cloudflare account.
3. Make the database:
   ```
   npx wrangler d1 create bookbuddies-unity
   ```
   It prints a block with `database_id = "…"`. Copy that id into `wrangler.toml`, replacing `PASTE-YOUR-DATABASE-ID-HERE`.
4. Put it online:
   ```
   npx wrangler deploy
   ```
   The first deploy also creates the `TownRoom` Durable Object class (the `[[migrations]]` block in `wrangler.toml`). It prints your address, such as `https://bookbuddies-unity.<you>.workers.dev`.
5. Check it: open `https://bookbuddies-unity.elam2790.workers.dev/api/health` in a browser. You should see `"ok":true`.
6. Point the game at it: in Unity, open `Assets/BookBuddies/Resources/BookBuddies/Data/config.json` and set `"server"` to that address. Players can also change it in **Settings → Account → Server**.

The tables are created automatically the first time the Worker runs. You can also create them by hand with `npm run db:schema`, but you don't need to.

### Optional

- **Your own signing key.** Live-town tickets are signed with a random key saved in the database. To use your own key instead, run `npx wrangler secret put TICKET_SECRET` and paste any long random text.
- **Your own domain.** In the Cloudflare dashboard, open the Worker, then go to **Settings → Domains & Routes → Add → Custom domain** (for example `play.bookbuddies.pet`).
- **Browser builds.** To let only one website call the API from a browser, uncomment `ALLOWED_ORIGIN` in `wrangler.toml`. Desktop and phone builds don't need it.

## Try it on your own computer

```
npm run dev
```

This runs the Worker, a local D1 database and the Durable Objects at `http://localhost:8787`. To test without deploying, set the game's server to that address.

## Looking after players

Run these with `npx wrangler d1 execute bookbuddies-unity --remote --command "…"`. To change your local test database instead, leave out `--remote`.

| To | Command |
|---|---|
| Find a player | `SELECT id, name, coins, created_at FROM players WHERE name LIKE '%Pip%'` |
| Make someone an admin | `UPDATE players SET is_admin = 1 WHERE name = 'Their Name'` |
| Pause someone's chat for an hour | `UPDATE players SET mute_until = (strftime('%s','now') + 3600) * 1000 WHERE name = 'Their Name'` |
| Give someone a day's break from town | `UPDATE players SET ban_until = (strftime('%s','now') + 86400) * 1000 WHERE name = 'Their Name'` |
| Block an extra word in chat and names | `INSERT INTO blocked_words (word) VALUES ('example')` |
| Count players | `SELECT COUNT(*) FROM players` |

A mute or a break starts the next time that player joins town.

## How the game talks to it

The routes and live messages match the website's, so the game works with this server or with bookbuddies.pet. Only this server can make new accounts, which happens when a player hatches an egg in the game.

| Step | Route |
|---|---|
| Hatch an egg (new account) | `POST /api/register {name, pet}` → `token` and a recovery code `BB-XXXXX-XXXXX` |
| Sign in on another device | `POST /api/link/claim {code}` |
| Your pet and name | `GET /api/me`, change them with `PATCH /api/me` |
| Show the recovery code again | `GET /api/me/recovery` |
| Delete the account | `POST /api/me/delete` |
| Live ticket | `GET /api/plaza/world/ticket?s=1-6` → a 60-second ticket and a 15-minute pass |
| Live town | WebSocket `/api/world/live?ticket=…` into the Durable Object `pawtopia:<room>` |

Each room holds up to 300 pets. Six villagers wander, chat and sit while anyone is there, and coins, coin bags and gift boxes turn up around town. Each player can collect up to 25 finds a day. A room stops ticking when the last pet leaves, so an empty town costs nothing.

Tested locally with `wrangler dev`: signing up, signing in with a recovery code, renaming, deleting an account, tickets and passes, joining rooms, walking, chat filtering, emotes, hugs, ping, collecting coins, and signing in elsewhere.
