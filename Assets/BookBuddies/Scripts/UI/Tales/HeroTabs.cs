using System;
using BookBuddies.Economy;
using BookBuddies.UI;
using BookBuddies.World;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.Tales
{
    // The hero card's shop tabs (the site's Tales shop): the move compendium, classes, and library upgrades, plus the
    // renown milestone on the left. Every purchase goes through ShopKit.Buy (the Wallet; the server prices it).
    public sealed partial class HeroCard
    {
        // the library upgrades that do something in Unity's fights (Lucky Bookmark and Bookshop Discount only work in chapter tales)
        static readonly string[] LibraryKeys = { "vit", "atk", "ink", "rev" };

        // the renown track's next stop: "Next at Pet Lv 6: a 4th move slot, a new move to learn"
        void Milestone()
        {
            var (petLv, opens) = HeroFactory.NextMilestone(save);
            var box = UiKit.Panel(leftContent, "next", Palette.Cream, 12);
            box.raycastTarget = false;
            UiKit.Row(box.rectTransform, 10, new RectOffset(12, 12, 8, 8));
            UiKit.Icon(box.transform, "🎯", 28);
            var words = UiKit.Node("words", box.transform);
            UiKit.Column(words, 0, null, TextAnchor.MiddleLeft);
            UiKit.Size(words, -1, -1, 1);
            UiKit.Label(words, $"Next at Pet Lv {petLv}", UiKit.SmallSize + 1, Palette.Ink, UiKit.Bold);
            string what = string.Join(", ", opens);
            UiKit.Label(words, char.ToUpperInvariant(what[0]) + what.Substring(1), UiKit.SmallSize, Palette.InkSoft);
        }

        // a row that's new: the New tag, and it flips over the first time it's shown
        void Fresh(RectTransform row, string key)
        {
            if (!save.Unseen.Contains(key)) return;
            ShopKit.NewTag(row);
            shownNew.Add(key);
            if (flipped.Add(key)) ShopKit.Reveal(row, .3f);
        }

        // ---- Moves: the compendium ----

        void FillCompendium(BattleUnit hero)
        {
            var h = hero.Hero;
            var C = TalesData.Current.Class(h.Cls);
            var (owned, seals, hidden) = HeroFactory.Compendium(save, h.Cls);
            int slots = HeroFactory.Slots(h.Rn);
            Section($"{C.Name} moves", $"{TalesUi.PetName} starts every fight with the moves it has equipped: {h.Kit.Count - 2} of {slots} slots"
                + (h.Rn < 12 ? $" · next slot at Pet Lv {(h.Rn < 5 ? 6 : 13)}" : ""));
            foreach (var p in owned) if (!p.Move.Ult) MoveRow(hero, p.Key);
            Section("Ultimates", "Pick one. It fires when the ink is full.");
            foreach (var p in owned) if (p.Move.Ult) MoveRow(hero, p.Key);

            if (seals.Count > 0) Section("Sealed pages", "Grow, read and save coins to open them.");
            foreach (var p in seals) SealRow(hero, p);
            if (hidden > 0) UiKit.Label(rightContent, $"{hidden} more {(hidden == 1 ? "page" : "pages")} to discover as {TalesUi.PetName} grows.", UiKit.SmallSize + 1, Palette.InkSoft, null, TextAnchor.MiddleCenter);
            if (save.Me.Kit.ContainsKey(h.Cls))
            {
                var row = UiKit.Node("reset", rightContent);
                UiKit.Row(row, 0, new RectOffset(0, 0, 8, 0)).childForceExpandWidth = false;
                UiKit.Secondary(row, "Reset to starting moves", () => { HeroFactory.ResetKit(save, h.Cls); AutoSave.Now("moves"); flash.Show("Loadout reset"); Fill(); }, null, 44);
            }
        }

        void MoveRow(BattleUnit hero, string k)
        {
            var h = hero.Hero;
            var m = HeroFactory.Move(h.Cls, k);
            var row = Row(HeroFactory.MoveIcon(hero, k), HeroFactory.MoveName(hero, k),
                HeroFactory.MoveDesc(hero, k) + (m.Ult ? "" : m.Cost > 0 ? $" · {m.Cost} ink" : " · free"));
            Fresh(row, $"move:{h.Cls}:{k}");
            if (k == "strike") { UiKit.Badge(row, "Always", UiKit.SkyInk); return; }
            if (h.Kit.Contains(k)) UiKit.Primary(row, "Equipped", () => Equip(h.Cls, k), null, 44);
            else UiKit.Secondary(row, "Equip", () => Equip(h.Cls, k), null, 44);
        }

        void Equip(string cls, string k)
        {
            string why = HeroFactory.ToggleEquip(save, cls, k);
            if (why != null) { Sound.Play("boop"); flash.Show(why); return; }
            AutoSave.Now("moves");
            Sound.Play("pop");
            Fill();
        }

        // a sealed page: what it is stays hidden, only how to open it shows
        void SealRow(BattleUnit hero, MovePage p)
        {
            var d = TalesData.Current;
            bool ready = HeroFactory.CanLearn(save, p);
            string hint = p.Tome ? $"A genre tome. Finish a {d.TomeLabel[p.Key]} book to open it."
                : ready ? (p.Move.Ult ? "A second ultimate, ready to learn." : "A new move, ready to learn.")
                : $"Reach Pet Lv {p.PetLv} to learn it.";
            var row = Row(p.Tome ? "📕" : "🔒", "???", hint);
            Silhouette(row);
            if (!ready) return;
            string cls = hero.Hero.Cls, item = $"move:{cls}:{p.Key}";
            ShopKit.PriceButton(row, Wallet.Price(item), () => ShopKit.Buy(item, $"{item}:{Shop.PetKey}", "✨", p.Move.Ult ? "the sealed ultimate" : "the sealed move", _ =>
            {
                if (!this) return;
                HeroFactory.Learn(save, cls, p.Key);
                flash.Show($"✨ {HeroFactory.MoveName(hero, p.Key)} learned!");
                Fill();
            }, flash.Show), "Learn");
        }

        // a sealed row's picture as a dark shape on an ink plate
        static void Silhouette(RectTransform row)
        {
            row.Find("icon").GetComponent<Image>().color = Palette.Ink.WithAlpha(.85f);
            row.Find("icon/icon").GetComponent<Image>().color = new Color(0, 0, 0, .55f);
        }

        // ---- Class ----

        void FillClasses(BattleUnit hero)
        {
            var d = TalesData.Current;
            var now = d.Class(hero.Hero.Cls);
            int petLv = hero.Hero.Rn + 1;
            Section("Your class", null);
            var current = Row(now.Icon, HeroFactory.EvoName(now.Key, petLv), $"{now.Name} · {now.Role} · Pet Lv {petLv}");
            UiKit.Badge(current, "Current", UiKit.LeafInk);

            Section("Classes you can be", "Each class keeps its own Pet Lv and moves. Changing class takes you back to your lore stone.");
            string natural = d.PersonalityClass.TryGetValue(save.Personality ?? "sunny", out var n) ? n : "sleuth";
            foreach (var c in HeroFactory.OpenClasses(save))
            {
                if (c.Key == now.Key) continue;
                bool played = save.Me.ClassRxp.ContainsKey(c.Key);
                var row = Row(c.Icon, c.Name, $"{c.Role} · " + (played ? $"Pet Lv {HeroFactory.PetLvIn(save, c.Key)}" : "Not played yet") + (c.Key == natural ? " · its natural class" : ""));
                Fresh(row, "cls:" + c.Key);
                UiKit.Secondary(row, "Become", () => AskBecome(now, c), null, 44);
            }

            var near = HeroFactory.NearClasses(save, out int hidden);
            if (near.Count > 0) Section("Classes to discover", "Keep exploring and they open. Or open one early with coins.");
            var metrics = HeroFactory.ClassMetrics(save);
            foreach (var c in near)
            {
                var (value, goal) = HeroFactory.ClassProgress(metrics, c);
                var row = Row(c.Icon, "???", $"{c.UnlockLabel} · {JsMath.Num(value)} of {JsMath.Num(goal)}");
                Silhouette(row);
                Bar(row.Find("words"), (float)(value / goal));
                string item = "unlock:" + c.Key;
                ShopKit.PriceButton(row, Wallet.Price(item), () => ShopKit.Buy(item, null, "🔓", "this class early", _ =>
                {
                    if (!this) return;
                    HeroFactory.UnlockClass(save, c.Key);
                    save.Touch();
                    flash.Show($"🔓 {c.Name} unlocked!");
                    Fill();
                }, flash.Show), "Open");
            }
            if (hidden > 0) UiKit.Label(rightContent, $"{hidden} more {(hidden == 1 ? "class" : "classes")} to discover as your journey goes on.", UiKit.SmallSize + 1, Palette.InkSoft, null, TextAnchor.MiddleCenter);
        }

        static void Bar(Transform parent, float share)
        {
            var track = UiKit.Panel(parent, "track", Palette.Ink.WithAlpha(.1f), 4);
            track.raycastTarget = false;
            UiKit.Size(track, -1, 8);
            var fill = UiKit.Panel(track.transform, "fill", Palette.Amber, 4).rectTransform;
            fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(Mathf.Clamp01(share), 1);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
        }

        // "Become a Spine Mage?": switch, then off to your lore stone
        void AskBecome(ClassDef from, ClassDef to)
        {
            var home = TownBook.Current.Get(TownProgress.Home ?? Boot.TownKey);
            ConfirmCard.Open(to.Icon, $"Become a {to.Name}?",
                $"{TalesUi.PetName} keeps its {from.Name} progress for when it comes back. You’ll head back to your lore stone in {home.Name}.",
                $"Become a {to.Name}", () => Become(to, home));
        }

        void Become(ClassDef to, TownBook.Town home)
        {
            if (!HeroFactory.SetClass(save, to.Key)) return;
            AutoSave.Now("class");
            if (save.Unseen.Remove("cls:" + to.Key)) save.Touch();
            Close();
            int lv = HeroFactory.PetLvIn(save, to.Key);
            string note = $"🎭 {TalesUi.PetName} is now a {to.Name}" + (lv > 1 ? $", back at Pet Lv {lv}" : ", starting at Pet Lv 1");
            var at = new Vector2Int(home.Stone[0], home.Stone[1]);
            var w = Boot.World;
            if (w != null && w.Map.Key == home.Key) { w.PoofTo(at, home.Landmark); w.Notify(note); }
            else Boot.Travel(home.Key, at, note, true);
        }

        // ---- Library ----

        void FillLibrary()
        {
            Section("Library upgrades", $"They help {TalesUi.PetName} in every fight, whatever its class. Gear lives in your bag.");
            foreach (var (key, name, icon, desc) in TalesData.Current.Library)
            {
                if (Array.IndexOf(LibraryKeys, key) < 0) continue;
                int level = HeroFactory.Library(save, key);
                var row = Row(icon, level > 0 ? $"{name} · Lv {level}" : name, desc);
                string item = $"meta:{key}:{level + 1}";
                int price = Wallet.Price(item);
                if (price == 0) { UiKit.Badge(row, "Maxed", UiKit.LeafInk); continue; }
                ShopKit.PriceButton(row, price, () => ShopKit.Buy(item, null, icon, $"{name} level {level + 1}", _ =>
                {
                    if (!this) return;
                    save.Meta[key] = Math.Max(level + 1, HeroFactory.Library(save, key));
                    save.Touch();
                    flash.Show($"{icon} {name} is now level {level + 1}");
                    Fill();
                }, flash.Show));
            }
        }
    }
}
