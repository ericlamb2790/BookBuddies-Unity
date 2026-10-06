# BookBuddies for Unity (v0.2)

Your pet, Pawtopia and the live Plaza, ported from website build 530 to Unity, with an intro, a title screen with your egg or pet, egg hatching, loading screens, Settings, a town menu, a minimap and photo mode. It comes with its own Cloudflare Worker (D1 and Durable Objects).

**Start with [SETUP.md](SETUP.md).** It covers the project, the server, the controls, Settings, sounds and swapping art.

```
Assets/BookBuddies/
  Scripts/    C# (Core, Net, Live, World, Pets, UI)
  Editor/     art import settings
  Resources/BookBuddies/
    Data/     server address (config.json), town layout, art index, pet parts
    Town/     ground, buildings, animations, plants, items (PNG)
    UI/       emoji images (PNG)
    Fonts/    Fredoka, Fraunces (Open Font License)
Server/       the new Cloudflare Worker (see Server/README.md)
```
