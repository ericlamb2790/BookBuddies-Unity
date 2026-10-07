# Unity port notes

## Who does what
- **Cloud Unity-port thread:** does all the coding, including the Tales of Pages port and the UI, controller, combat, bag and gear rework. It can't write to this folder.
- **Session on Damp's PC:** writes the files the cloud thread sends (or pulls them from `origin/main`), handles git, and reports compile results from `%LOCALAPPDATA%\Unity\Editor\Editor.log`.
- `D:\AI\BB` (the website repo) is read-only for this project. Nothing gets deployed from here.

## Repo
- GitHub: https://github.com/ericlamb2790/BookBuddies-Unity, branch `main`.
- `.gitignore` keeps out Library, Temp, Logs, UserSettings, the .sln and .csproj files, .vs and secrets (plus `Server/node_modules` and `Server/.wrangler`, from the old Worker).
- Unity settings: Force Text serialization and visible .meta files.

## Done
- v0.2 imported.
- **Walking:** releasing a direction key or the stick stops the pet on the tile it's stepping into (the path is trimmed and `SendGo` is called). `KeyboardStride` is 2 (it was 3). See `PlazaWorld.HandleKeys`.
- **F11 fullscreen Game view:** `Assets/Editor/FullscreenGameView.cs` opens a borderless, toolbar-free Game view on the main monitor. It's also under Window > General > Game Fullscreen. It closes on F11 again or when you leave Play mode.

## Open
- **Known bug:** `Hud.Refresh` (Hud.cs:465) throws MissingReferenceException after the HUD's text is destroyed. It's called from `PlazaWorld.OnStateChanged` (PlazaWorld.cs:523). Fix by unsubscribing in OnDestroy or null-checking.
- **Tales of Pages port** (cloud thread): Brambles combat, gear and basic gameplay. The random-combat end screen in the Brambles should auto-close after a few seconds.
- **UI rework** (cloud thread):
  - Controller support for menus: a virtual cursor that emulates the mouse; A interacts like a left click, B backs out.
  - Admin support for accounts.
  - Overall UI scaling polish for fullscreen.
  - Combat screen polish, scaled for bigger fights.
  - Bag and gear menus reworked.
  - Better walking and clearer interactables.
