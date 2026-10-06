# BookBuddies Unity server (a new Cloudflare Worker)

This is a **new, separate Worker** for the Unity game. It has its own **D1 database** for players and its own **Durable Objects** for the live town. It doesn't touch the website's Worker or its database.

| What | Where |
|---|---|
| Accounts, sign-in, recovery codes | `src/index.js` |
| Admin tools for accounts | `src/admin.js` |
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
5. Check it: open `https://bookbuddies-unity.<you>.workers.dev/api/health` in a browser. You should see `"ok":true`.
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

### Make the first admin

Admins get **Admin tools** in the game (Settings → Account, and a Moderation section on each player's card in town). Nobody is an admin at first. Hatch your own buddy in the game, then run this once with your buddy's name:

```
npx wrangler d1 execute bookbuddies-unity --remote --command "UPDATE players SET is_admin = 1 WHERE name = 'Your Buddy Name'"
```

Restart the game (or sign out and back in) so it picks up the change. After that you can make other admins from Admin tools in the game, and you won't need this command again.

### Admin tools in the game

Search players by name (or paste an id), then from their card:

- **Chat:** pause their chat for 15 minutes, an hour or a day, or unpause it. It takes effect at once, even mid-visit.
- **Town:** send them home now, give them a break from town (an hour, a day, a week, or for good), or end a break. A player on a break can't sign in or join town until it ends.
- **Account:** rename them, set their coins, share or take away admin rights, or delete the account.

You can't use these on yourself, and you can't mute, pause, send home or delete another admin until you take their admin rights away. Every change is written to the `admin_log` table, and the **Recent actions** tab shows the latest ones.

### By hand

Run these with `npx wrangler d1 execute bookbuddies-unity --remote --command "…"`. To change your local test database instead, leave out `--remote`.

| To | Command |
|---|---|
| Find a player | `SELECT id, name, created_at FROM players WHERE name LIKE '%Pip%'` |
| See someone's coins | `SELECT kind, amount, day FROM coin_tx WHERE player_id = 'their id' ORDER BY id DESC LIMIT 20` (the balance is `SUM(amount)`) |
| Make someone an admin | `UPDATE players SET is_admin = 1 WHERE name = 'Their Name'` |
| See recent admin actions | `SELECT datetime(at / 1000, 'unixepoch'), admin_name, action, target_name, detail FROM admin_log ORDER BY id DESC LIMIT 20` |
| Pause someone's chat for an hour | `UPDATE players SET mute_until = (strftime('%s','now') + 3600) * 1000 WHERE name = 'Their Name'` |
| Give someone a day's break from town | `UPDATE players SET ban_until = (strftime('%s','now') + 86400) * 1000 WHERE name = 'Their Name'` |
| Block an extra word in chat and names | `INSERT INTO blocked_words (word) VALUES ('example')` |
| Count players | `SELECT COUNT(*) FROM players` |

A mute or a break made by hand starts the next time that player joins town (the in-game tools apply at once).

## How the game talks to it

The routes and live messages match the website's, so the game works with this server or with bookbuddies.pet. Only this server can make new accounts, which happens when a player hatches an egg in the game.

| Step | Route |
|---|---|
| Hatch an egg (new account) | `POST /api/register {name, pet}` → `token` and a recovery code `BB-XXXXX-XXXXX` |
| Sign in on another device | `POST /api/link/claim {code}` |
| Your pet and name | `GET /api/me`, change them with `PATCH /api/me` |
| Your pets (up to 6) | `GET /api/me/pets` → `pets` (`id`, `name`, `look`) and the `active` pet's id (`GET /api/me` includes them too) |
| Hatch another pet | `POST /api/me/pets {name, look}`; the new pet becomes the active one |
| Reroll or rename a pet | `PATCH /api/me/pets/<id> {look, name}` (either one) |
| Switch the active pet | `POST /api/me/pets/<id>/active`; its look becomes your `pet` |
| Show the recovery code again | `GET /api/me/recovery` |
| Delete the account | `POST /api/me/delete` |
| Your coins | `GET /api/wallet` → `balance`, Book Fair tickets `fair`, today's `gift` `{claimed, run}`, today's counts per kind `used`, the last 20 coin rows `recent`, the `day` (US Eastern) and the Book Fair counters. A new account gets 1000 coins the first time. |
| Daily gift | `POST /api/wallet/earn {kind: 'gift'}` → `granted` and `run` (200, 250, 300, 400, 500, 600, 1000 for days in a row) |
| Buy something | `POST /api/wallet/spend {kind: 'shop', item, ref}` → `paid`; the server prices `item` from `src/economy.json` (`decor:<id>`, `move:<class>:<move>`, `unlock:<class>`, `meta:<upgrade>:<level>`, `stock:<day>:<town>:<i>` with `amount`, `satchel:<town>`). `{kind: 'fair', n}` trades coins for 1, 5, 12 or 30 Book Fair tickets. |
| Live ticket | `GET /api/plaza/world/ticket?s=1-6&town=pawtopia` (or a town on Bramble Road such as `romance`, a road link `road1` … `road17`, or `caves`) → a 60-second ticket and a 15-minute pass |
| Live town | WebSocket `/api/world/live?ticket=…` into the Durable Object `<town>:<room>` |
| Admin: find players | `GET /api/admin/players?q=part of a name` (`&exact=1` for the whole name) |
| Admin: one player and their history | `GET /api/admin/players/<id>` |
| Admin: change a player | `POST /api/admin/players/<id>/<action>`: `mute {minutes}`, `unmute`, `ban {hours}` or `ban {permanent: true}`, `unban`, `kick`, `rename {name}`, `coins {coins}`, `admin {on}`, `delete` |
| Admin: recent actions | `GET /api/admin/log` |

Admin routes answer `403` unless the signed-in player is an admin.

Each room holds up to 300 pets. In Pawtopia six villagers wander, chat and sit while anyone is there, and in every town (Pawtopia and the 17 on Bramble Road) coins, coin bags and gift boxes turn up. The road links and the caves have neither. Each player can collect up to 25 finds a day (US Eastern days, like the website); the coins go straight into their wallet. A room stops ticking when the last pet leaves, so an empty town costs nothing.

Tested locally with `wrangler dev`: signing up, signing in with a recovery code, renaming, deleting an account, tickets and passes, joining rooms, walking, chat filtering, emotes, hugs, ping, collecting coins, and signing in elsewhere; the wallet (starter coins, the daily gift and its run, purchases, refusals, find limits, admin changes and moving old coin totals into the ledger).

Coins are a ledger (`coin_tx`): every coin earned or spent is one row, and a balance is the sum, so there's no total to edit by mistake. Prices and rewards live in `src/economy.json`, made from the website by `tools/export_econ.js` (the game reads the same file). Coins from before v0.4 (`players.coins`) move into the ledger once, by themselves, the first time the new Worker starts.
