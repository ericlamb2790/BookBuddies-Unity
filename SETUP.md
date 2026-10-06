# BookBuddies in Unity: setup

Unity port v0.2, made from website build 530. It has your pet, Pawtopia and the live Plaza, plus an intro, a title screen with your egg or pet, egg hatching, loading screens, Settings, a town menu, a minimap and photo mode.

It's made for **Unity 6 (6000.3)** with the **Input System** package, and also works with 2022.3 LTS and the old input setting.

## 1. The Unity project

Your project at `D:\AI\BB-Unity\BookBuddies Unity` is already set up. To update it from v0.1, close Unity, copy everything in this zip (`Assets`, `Packages`, `Server` and the two docs) into that folder, choose **Replace** for files that already exist, and reopen the project.

`Packages/manifest.json` removes Unity's **Engineering** feature, whose Code Coverage, Editor Coroutines, Profile Analyzer and Settings Manager packages showed *invalid signature* errors. The game uses none of them. Visual Studio support from that feature is kept. If you'd rather keep the feature, skip that file and instead delete the project's `Library\PackageCache` folder with Unity closed, so Unity downloads fresh copies.

For a fresh project:

1. In **Unity Hub**, click **New project**, pick **Unity 6**, and choose **3D (Built-In Render Pipeline)** (*Universal 3D* should work too, but don't use a 2D template).
2. Save it in its own folder, outside `D:\AI\BB` (that's the website's repo).
3. Copy `Assets/BookBuddies` into the project's `Assets` folder and wait while Unity imports it.
4. Open **Window → Package Manager**, click **+ → Add package by name…**, and add `com.unity.vectorgraphics`. Without it, pets show as round placeholders.
5. For builds only: in **Edit → Project Settings → Graphics → Always Included Shaders**, add `Unlit/Vector` and `Unlit/VectorGradient`.

## 2. Set up the server (once)

v0.2 has its own Cloudflare Worker in the `Server` folder, with a new D1 database and Durable Objects for the live town. It's separate from the website's Worker and doesn't change it. Follow **`Server/README.md`**. In short:

```
cd Server
npm install
npx wrangler login
npx wrangler d1 create bookbuddies-unity     (paste the database_id into wrangler.toml)
npx wrangler deploy
```

Then open `Assets/BookBuddies/Resources/BookBuddies/Data/config.json` and set `"server"` to the address the deploy printed, such as `https://bookbuddies-unity.<you>.workers.dev`. Players can also change it in **Settings → Account → Server**.

Until you do this, the game points at `bookbuddies.pet`. You can still sign in there with a website recovery code and join the website's live town, but new eggs hatch on this device only, because the website's Worker can't make accounts for Unity.

## 3. Press Play

Open any scene (the default `SampleScene` is fine) and press **Play**. The game adds itself, so the scene doesn't need anything in it.

1. **Loading screen:** your egg or pet bounces on a stack of books while the town is painted.
2. **Intro** (first launch only): a short cinematic tour of Pawtopia. Press Esc, Space, Enter or a gamepad button to skip. Watch it again from the title screen.
3. **Title screen:** your egg or hatched pet sits in the middle while the camera drifts over the town. Tap it to say hi.
   - **Hatch your egg** (it says **Play** once you have a buddy) leads to **Hatch my egg**, which asks for a nickname, hatches the egg (cracks, a burst, confetti) and shows your **recovery code**. Save it, because it's the only way back to this pet on another device. **Just look around** visits the town without a pet of your own.
   - **I have a recovery code** signs in with a code like `BB-XXXXX-XXXXX`.
4. **Arriving:** the camera cranes down from the sky to your pet, and the HUD fades in.

## Controls

| | Mouse and touch | Keyboard | Gamepad |
|---|---|---|---|
| Walk | tap the ground | WASD or arrows | left stick or d-pad |
| Say hi, play | tap a pet | E next to a pet | A next to a pet |
| Sit | tap a bench or chair | E next to it | A next to it |
| Hop | tap your pet | Space | |
| Emotes | ❤️ button | Q, or 1 to 8 | X |
| Tricks | 🐾 button | F | Y |
| Chat | chat box | Enter or T | Select (View) |
| Zoom | scroll or pinch | + and − | LB and RB |
| Town map | tap the minimap | M | right stick click |
| Hide the HUD | two-finger tap | H | |
| Photo | Menu → Photo mode | P or F12 | |
| Town menu | Menu button | Esc | Start (Menu) |
| Close / back | tap outside it | Esc | B |

In Settings, LB and RB switch tabs. A tap on a touch screen does exactly what a mouse click does. Photos are saved as PNG files in the game's data folder under `Photos` (on Windows: `%USERPROFILE%\AppData\LocalLow\<company>\<product>\Photos`).

## Settings

Everything is saved on the device:

- **Game:** name tags, chat bubbles, minimap, cinematics, reduce motion and UI size.
- **Sound:** music and effects volume.
- **Graphics:** quality, frame rate, VSync, fullscreen and butterflies.
- **Controls:** the list above.
- **Account:** show your recovery code, sign out, delete your pet and account, and the server address.

## Sound and music

The game makes its own sounds and gentle music in code, using the website's pet sounds, so there are no audio files to add. To use a recording, drop a clip into `Assets/BookBuddies/Resources/BookBuddies/Sounds/` (make the folder) named after the sound, and it replaces the made-up one:

- **Sounds:** `boop`, `happy`, `chat`, `munch`, `hug`, `splash`, `coin`, `crack`, `hatch`, `yawn`, `pop`, `gacha`, `rare`, `tap`, `open`, `close`, `shutter`, `whoosh`.
- **Music:** `music_home` (title and loading), `music_pawtopia` (town).

## Swapping art

Everything you see is a normal PNG in `Assets/BookBuddies/Resources/BookBuddies`. To reskin something, save a new PNG over the old one with **the same name**. Any resolution works, because size and placement come from `Data/art_index.json`.

| What | Where | Notes |
|---|---|---|
| Buildings and props | `Town/Objects/<name>.png` | In `art_index.json` → `objects` → `<name>`: `size` is in tiles, `pivot` is where it touches the ground, `shadows` are the soft shadows |
| Fountain, wheel, carousel | `Town/Anim/<name>_00.png` to `_23.png` | One PNG per frame. `frames` and `seconds` are in `art_index.json` → `anim` |
| Ground | `Town/Ground/pawtopia_<x>_<y>.png` | Each piece covers 16×16 tiles |
| Garden plants | `Town/Plants/<seed>_<stage>.png` | Stages 0 to 3 |
| Coins and gifts | `Town/Items/coin.png`, `bag.png`, `gift.png` | |
| Emoji and emotes | `UI/Emotes/<name>.png` | `art_index.json` → `emotes` maps each emoji to its file |
| Fonts | `Fonts/` | Fredoka for the game, Fraunces for titles (Open Font License) |
| Colours | `Scripts/Core/Palette.cs` | |
| Pets and eggs | `Data/pet_parts.json` | The website's own pet parts |
| Town layout | `Data/town_pawtopia.json` | Tiles, buildings, seats, places and area names |
| Villagers | `Scripts/World/Townsfolk.cs` | Names and looks of the six villagers |
| Intro shots and captions | `Scripts/UI/Cinema.cs` | `IntroShots` |
| Loading tips | `Scripts/UI/LoadingScreen.cs` | `Tips` |

If art looks stretched or blurry after a swap, right-click `Resources/BookBuddies` and choose **Reimport**.

## Where the code is

| Folder | What's in it |
|---|---|
| `Scripts/Core` | Startup and screen flow (`Boot.cs`), input for every device, settings, sound and music, your buddy, art loading, colours, JSON |
| `Scripts/Net` | The Worker's HTTP API and the live WebSocket |
| `Scripts/Live` | The live town: connection, players, chat, emotes, tricks and interactions |
| `Scripts/World` | The town map, camera, villagers and ambient life |
| `Scripts/Pets` | Pet looks, the pet drawing (a port of the site's `petSVG`) and pets walking around |
| `Scripts/UI` | Title, intro and arrival cinematics, hatching, loading, Settings, HUD, minimap, menus, name tags |
| `Editor` | Import settings for the art |
| `Server` (next to `Assets`) | The new Cloudflare Worker |

## Not in this version yet

- **Live games:** one per build, starting with whichever you pick.
- **Town places:** the board, café orders, shops, Tale Hall and the train show a card that says they open later.
- **Other features:** garden tending, gatherings, friends lists, other towns and Bramble Road, holiday decorations, and Tales gear.
- **Emoji in chat text** only show when they come first in a message, because Unity's built-in text can't draw colour emoji.
- **WebGL** would need a browser WebSocket bridge. Desktop, Android and iOS work.

This version was checked by compiling it against Unity's libraries for both input settings, and the server passed 20 local tests. It hasn't been run inside the Unity editor yet, so if something looks off, send a screenshot or the Console error.
