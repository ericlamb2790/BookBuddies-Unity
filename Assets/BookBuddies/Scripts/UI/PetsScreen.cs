using System.Collections.Generic;
using System.Threading.Tasks;
using BookBuddies.Net;
using BookBuddies.Pets;
using BookBuddies.Tales;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    /// <summary>
    /// Your pets, like the site's pets tab: a card for each (picture, name, body and evolution form) with "Make active",
    /// a mystery egg that hatches a surprise pet (up to six), the Pet Inn where pets past the nest of six nap (after joining
    /// accounts), then a DNA reroll (it asks first) and a rename box for the active pet, who is your buddy in town, in Tales
    /// and on the title screen. Pictures come from PetSprites' cache;
    /// any it hasn't drawn yet are drawn one per frame, so the screen opens without a stall.
    /// </summary>
    public sealed class PetsScreen : TalesScreen
    {
        const float Pad = 24, HeadHeight = 52, PictureSize = 92;

        PetActor me;         // your pet in town: it changes on the spot with the active pet
        System.Action closed; // the title screen redraws its buddy once the screen is gone
        RectTransform list;
        Text count, status;
        string nextLook;     // the pet waiting in the mystery egg (the egg wears its colour)
        bool busy, focused;
        readonly Queue<(Image image, string look)> toDraw = new Queue<(Image, string)>();

        static PetParts Parts => PetSprites.Parts;

        /// <summary>
        /// Opens the pets screen. Give your pet in town so it changes at once when you switch, hatch or reroll;
        /// closed runs when the screen goes away (the title screen uses it to show the new buddy).
        /// </summary>
        public static void Open(PetActor me = null, System.Action closed = null)
        {
            var s = Create<PetsScreen>("Pets", true);
            s.MaxSize = new Vector2(760, 940);
            s.me = me;
            s.closed = closed;
            s.nextLook = Parts.Dna.NewLook(Buddy.Roll);
            s.Build();
            s.Fill();
            _ = s.Run(MyPets.Refresh, null, "Checking your pets…");
        }

        void Build()
        {
            var head = UiKit.Node("header", Card);
            head.anchorMin = new Vector2(0, 1); head.anchorMax = Vector2.one; head.pivot = new Vector2(.5f, 1);
            head.offsetMin = new Vector2(Pad, -Pad - HeadHeight); head.offsetMax = new Vector2(-Pad, -Pad);
            UiKit.Row(head, 10);
            UiKit.Icon(head, "🐾", 34);
            UiKit.Size(UiKit.Label(head, "Your pets", UiKit.TitleSize, Palette.Ink, UiKit.Title), -1, 44, 1);
            count = UiKit.Label(head, "", UiKit.SmallSize + 1, Palette.InkSoft, UiKit.Bold);
            count.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.CloseButton(head, Close);

            var body = UiKit.Node("pets", Card);
            body.anchorMin = Vector2.zero; body.anchorMax = Vector2.one;
            body.offsetMin = new Vector2(Pad - 4, Pad + 30); body.offsetMax = new Vector2(-Pad + 8, -Pad - HeadHeight - 10);
            list = UiKit.ScrollColumn(body, 12, new RectOffset(4, 12, 4, 16), out _);
            ((RectTransform)list.parent).Fill();

            status = UiKit.Label(Card, "", UiKit.SmallSize + 1, UiKit.EmberInk, UiKit.Bold);
            ((RectTransform)status.transform).Pin(Vector2.zero, new Vector2(Pad, Pad - 8), new Vector2(480, 32));
            UiKit.PadHints(Card, ("A", "Select"), ("B", "Back"));
        }

        protected override void OnClosed() => closed?.Invoke();

        protected override void Layout(Vector2 card)
        {
            if (focused) return;
            focused = true;
            VirtualCursor.FocusFirst(list.GetComponentInChildren<Selectable>());
        }

        // ---- the list ----

        void Fill()
        {
            for (int i = list.childCount - 1; i >= 0; i--) { var c = list.GetChild(i).gameObject; c.SetActive(false); Destroy(c); }
            toDraw.Clear();
            var pets = MyPets.All;
            var active = MyPets.Active;
            foreach (var pet in pets) PetCard(pet, pet == active);
            if (pets.Count < MyPets.Max) EggCard();
            if (MyPets.Resting.Count > 0) Inn(pets.Count >= MyPets.Max);
            if (active != null) { DnaBox(active); RenameBox(active); }
            count.text = $"{pets.Count} of {MyPets.Max}";
        }

        // a nest pet: Make active, and Rest while others nap at the Pet Inn (the active one stays)
        void PetCard(MyPets.Pet pet, bool active)
        {
            var row = PetLine(pet, active);
            if (active)
            {
                UiKit.Icon(row, "⭐", 26);
                UiKit.Badge(row, "Active", UiKit.EmberInk);
                return;
            }
            UiKit.Secondary(row, "Make active", () => _ = Run(() => MyPets.MakeActive(pet), $"{pet.Name} is your buddy now"), null, 44);
            if (MyPets.Resting.Count > 0) UiKit.Secondary(row, "Rest", () => _ = Run(() => MyPets.Rest(pet), $"{pet.Name} is napping at the Pet Inn"), "💤", 44);
        }

        // a pet's picture, name, body and evolution form, in a row of the list
        RectTransform PetLine(MyPets.Pet pet, bool active)
        {
            var look = PetLook.Parse(pet.Look, Parts) ?? new PetLook();
            var row = Row(pet.Name, active);
            Picture(row, pet.Look);
            var words = Words(row);
            UiKit.Label(words, pet.Name, UiKit.HeadingSize, Palette.Ink, UiKit.Bold);
            var body = Parts.ShapeNames.TryGetValue(look.Shape, out var n) ? n : (name: look.Shape, icon: "");
            IconLine(words, body.icon, look.Shiny ? body.name + " · Shiny" : body.name);
            string form = look.Stage == 0 ? "Still an egg" : $"Form {look.Evo} of 3 · {Parts.Evolution.NameOf(look.Shape, look.Evo, Parts)}";
            UiKit.Label(words, form, UiKit.SmallSize, UiKit.EmberInk, UiKit.Bold);
            return row;
        }

        // pets past the nest of six (after joining accounts) nap here, each one coming home when the nest has room
        void Inn(bool full)
        {
            var box = Box("Pet Inn");
            IconLine(box, "🛌", "Resting at the Pet Inn", UiKit.BodySize + 1, Palette.Ink, UiKit.Bold);
            UiKit.Label(box, "Pets who don’t fit in your nest of 6 nap here. Nobody is ever left behind.", UiKit.SmallSize + 1, Palette.InkSoft);
            if (full) UiKit.Label(box, "Your nest is full. Let a pet rest first.", UiKit.SmallSize + 1, UiKit.EmberInk, UiKit.Bold);
            foreach (var pet in MyPets.Resting)
                UiKit.Secondary(PetLine(pet, false), "Bring home", () => _ = Run(() => MyPets.Wake(pet), $"{pet.Name} is home in your nest"), "🏡", 44)
                    .interactable = !full;
        }

        void EggCard()
        {
            var row = Row("mystery egg");
            Picture(row, "{\"h\":" + Hue(nextLook) + ",\"s\":0}");
            var words = Words(row);
            UiKit.Label(words, "Mystery egg", UiKit.HeadingSize, Palette.Ink, UiKit.Bold);
            UiKit.Label(words, "Hatches a surprise pet", UiKit.SmallSize + 1, Palette.InkSoft);
            UiKit.Primary(row, "Hatch", Hatch, "🥚", 44);
        }

        void DnaBox(MyPets.Pet pet)
        {
            var box = Box("DNA reroll");
            IconLine(box, "🎲", "DNA reroll", UiKit.BodySize + 1, Palette.Ink, UiKit.Bold);
            UiKit.Label(box, $"Scramble {pet.Name}'s birth traits at random: body, face, ears, mane, tail, fur, pattern and color. " +
                $"Anything can happen, and the new body changes how {pet.Name} evolves!", UiKit.SmallSize + 1, Palette.InkSoft);
            UiKit.ConfirmButton(box, "Reroll DNA", "Sure? Tap again to reroll", () => _ = Run(() => MyPets.Reroll(pet), $"{pet.Name} has brand-new DNA!"));
        }

        void RenameBox(MyPets.Pet pet)
        {
            var box = Box("rename");
            IconLine(box, "✏️", $"Rename {pet.Name}", UiKit.BodySize + 1, Palette.Ink, UiKit.Bold);
            var row = UiKit.Node("name", box);
            UiKit.Row(row, 8);
            var field = UiKit.Input(row, "Pet name");
            field.characterLimit = 14;
            field.text = pet.Name;
            UiKit.Size(field, -1, UiKit.ButtonHeight, 1);
            UiKit.Secondary(row, "Save", () => Rename(pet, field.text));
            UiKit.Label(box, "Your active pet is your buddy in town, in Tales of Pages and on the title screen.", UiKit.SmallSize, Palette.InkSoft);
        }

        // ---- changes ----

        async void Hatch()
        {
            string name = MyPets.NewName(), look = nextLook;
            MyPets.Pet pet = null;
            if (!await Run(async () => pet = await MyPets.Hatch(name, look), null, "Warming up…") || pet == null) return;
            var town = me;
            Close();
            bool locked = PlazaInput.Locked;
            PlazaInput.Locked = true; // no walking off while the egg hatches
            HatchScene.Meet(Hue(look), pet.Look, pet.Name, () => { PlazaInput.Locked = locked; Open(town); });
        }

        void Rename(MyPets.Pet pet, string name)
        {
            name = name.Trim();
            if (name.Length < 2) { Say("Pick a name with at least 2 letters."); return; }
            if (name != pet.Name) _ = Run(() => MyPets.Rename(pet, name), "Saved");
        }

        // one change at a time: says what's happening, then redraws. False when it failed (the reason shows) or the screen closed.
        async Task<bool> Run(System.Func<Task> change, string done, string working = "Working…")
        {
            if (busy) return false;
            busy = true;
            Say(working);
            try { await change(); }
            catch (BBApi.ApiError e)
            {
                busy = false;
                if (this) Say(e.Message);
                return false;
            }
            busy = false;
            AutoSave.Now("pets");
            if (!this) return false;
            if (me && me.Look != Buddy.Look) me.SetLook(Buddy.Look);
            if (done != null) Sound.Play("happy");
            Say(done ?? "");
            Fill();
            return true;
        }

        void Say(string text) => status.text = text;

        // ---- pictures: cached ones at once, new ones one per frame (tessellating a pet takes a few milliseconds) ----

        void Picture(RectTransform row, string look)
        {
            var img = UiKit.Node("picture", row).gameObject.AddComponent<Image>();
            UiKit.Size(img, PictureSize, PictureSize);
            img.preserveAspect = true;
            img.raycastTarget = false;
            if (PetSprites.Has(look)) img.sprite = PetSprites.For(look);
            else { img.enabled = false; toDraw.Enqueue((img, look)); }
        }

        protected override void Update()
        {
            base.Update();
            if (ReferenceEquals(UiStack.Top, this) && TalesUi.Pressed(PlazaAction.Pets)) Close(); // Select opened it, Select closes it
            while (toDraw.Count > 0)
            {
                var (img, look) = toDraw.Dequeue();
                if (!img) continue;
                img.sprite = PetSprites.For(look);
                img.enabled = true;
                break;
            }
        }

        // ---- pieces ----

        RectTransform Row(string name, bool on = false)
        {
            var panel = UiKit.Panel(list, name, on ? Palette.Amber.WithAlpha(.16f) : Palette.Paper, 14);
            UiKit.Outline(panel, on ? Palette.Amber : Palette.Ink.WithAlpha(.1f), 14, on ? 2 : 1);
            UiKit.Size(panel, -1, PictureSize + 16);
            UiKit.Row(panel.rectTransform, 14, new RectOffset(12, 16, 8, 8));
            return panel.rectTransform;
        }

        RectTransform Box(string name)
        {
            var panel = UiKit.Panel(list, name, Palette.Paper, 14);
            UiKit.Outline(panel, Palette.Ink.WithAlpha(.1f), 14, 1);
            UiKit.Column(panel.rectTransform, 8, new RectOffset(16, 16, 14, 16));
            return panel.rectTransform;
        }

        static RectTransform Words(RectTransform row)
        {
            var words = UiKit.Node("words", row);
            UiKit.Column(words, 2, null, TextAnchor.MiddleLeft);
            UiKit.Size(words, -1, -1, 1);
            return words;
        }

        // a line of text with an emoji in front (left out when there's no art for it)
        static void IconLine(Transform parent, string icon, string text, int size = UiKit.SmallSize + 1, Color? color = null, Font font = null)
        {
            var line = UiKit.Node("line", parent);
            UiKit.Row(line, 6);
            if (!string.IsNullOrEmpty(icon) && Art.Emote(icon) != null) UiKit.Icon(line, icon, size + 6);
            UiKit.Size(UiKit.Label(line, text, size, color ?? Palette.InkSoft, font), -1, -1, 1);
        }

        static int Hue(string look) => (int)(PetLook.Parse(look, Parts)?.Hue ?? 0);
    }
}
