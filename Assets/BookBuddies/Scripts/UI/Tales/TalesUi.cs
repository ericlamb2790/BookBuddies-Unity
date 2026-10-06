using BookBuddies.Pets;
using System.Collections.Generic;
using BookBuddies.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.Tales
{
    /// <summary>Entry points for the bag, gear and hero screens and the loot reveal.</summary>
    public static class TalesUi
    {
        const int FirstOrder = 80; // above the HUD, road cards, settings and the battle screen

        static readonly List<TalesScreen> open = new List<TalesScreen>();
        static bool lockedBefore;
        static int lastOrder;

        /// <summary>True while the bag, the hero card or a reveal is open.</summary>
        public static bool AnyOpen => open.Count > 0;

        /// <summary>The bag and gear screen (equip, compare, salvage), optionally highlighting one item (its item string).</summary>
        public static void OpenBag(string highlight = null) => BagScreen.Open(highlight);

        /// <summary>The hero card: class, Pet Lv, stats and tactics, and its shop tabs ("moves", "class", "library").</summary>
        public static void OpenHero(string tab = null) => HeroCard.Open(tab);

        /// <summary>A short "you found…" reveal for new items (stash, shrine, chest), then optional bag button.</summary>
        public static void Reveal(IList<string> items, string title, System.Action closed = null) => RevealScreen.Open(items, title, closed);

        // ---- shared by the screens ----

        /// <summary>A screen opened: walking stops, and it gets a canvas order above everything already open.</summary>
        internal static int Opened(TalesScreen s)
        {
            if (open.Count == 0) { lockedBefore = PlazaInput.Locked; lastOrder = FirstOrder; }
            open.Add(s);
            PlazaInput.Locked = true;
            return lastOrder += 2;
        }

        /// <summary>A screen closed: the last one gives walking back (unless a battle still holds it).</summary>
        internal static void Closed(TalesScreen s)
        {
            if (!open.Remove(s) || open.Count > 0) return;
            PlazaInput.Locked = lockedBefore && BattleScreen.Open;
        }

        internal static T Find<T>() where T : TalesScreen => open.Find(s => s is T) as T;

        /// <summary>
        /// A shortcut pressed this frame for an open screen. PlazaInput.Down answers only Back while Locked (it guards the
        /// town), so the screen asks with the lock lifted for that one read.
        /// </summary>
        internal static bool Pressed(PlazaAction a)
        {
            bool was = PlazaInput.Locked;
            PlazaInput.Locked = false;
            bool down = PlazaInput.Down(a);
            PlazaInput.Locked = was;
            if (down) PlazaInput.Consume(a);
            return down;
        }

        /// <summary>The rarity's own colour (frames, glows, names on ink).</summary>
        internal static Color RarityColor(int tier) => Palette.Hex(Loot.RarityColor(tier));

        /// <summary>"Your buddy" until it has a name.</summary>
        internal static string PetName => MyPets.ActiveName;
    }

    /// <summary>
    /// A full-screen Tales card: a dimmed scrim over the town, a cream card that rises in, on the UiStack while open
    /// (B and Esc back out) and walking locked. Subclasses build into Card and size it in Layout.
    /// </summary>
    public abstract class TalesScreen : MonoBehaviour
    {
        protected RectTransform Root, Card;
        protected CanvasGroup Group;
        protected Vector2 MaxSize = new Vector2(1400, 840);

        Vector2 lastRoot;
        float shownAt, closingAt = -1;

        /// <summary>Makes the canvas, scrim and card for a new screen (paper = false leaves the card see-through, for reveals).</summary>
        protected static T Create<T>(string name, bool tapOutsideCloses, bool paper = true) where T : TalesScreen
        {
            UiKit.EnsureEventSystem();
            var canvas = UiKit.MakeCanvas(name, 0);
            var s = canvas.gameObject.AddComponent<T>();
            canvas.sortingOrder = TalesUi.Opened(s);
            s.Root = (RectTransform)canvas.transform;
            s.Group = canvas.gameObject.AddComponent<CanvasGroup>();
            var scrim = UiKit.Cover(s.Root, "scrim", Palette.Ink.WithAlpha(.58f), null, true);
            if (tapOutsideCloses)
            {
                var dismiss = scrim.gameObject.AddComponent<Button>();
                dismiss.transition = Selectable.Transition.None;
                dismiss.navigation = new Navigation { mode = Navigation.Mode.None };
                dismiss.onClick.AddListener(s.Close);
            }
            s.Card = (paper ? UiKit.Panel(s.Root, "card", Palette.Cream, 18).rectTransform : UiKit.Node("card", s.Root)).Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(600, 400));
            if (paper) UiKit.Shadow(s.Card, 18, 30, 10, .35f);
            UiStack.Push(s, s.Close);
            s.shownAt = Time.unscaledTime;
            s.Group.alpha = 0;
            Sound.Play("open");
            return s;
        }

        /// <summary>Closes the screen (fades out, then it's gone).</summary>
        public void Close()
        {
            if (closingAt >= 0) return;
            closingAt = Time.unscaledTime;
            Group.blocksRaycasts = false;
            UiStack.Remove(this);
            TalesUi.Closed(this);
            Sound.Play("close");
            OnClosed();
        }

        protected virtual void OnClosed() { }

        /// <summary>Called when the window size changes, with the card's new size.</summary>
        protected abstract void Layout(Vector2 card);

        protected virtual void Update()
        {
            var root = Root.rect.size;
            if (root != lastRoot)
            {
                lastRoot = root;
                Card.sizeDelta = new Vector2(Mathf.Min(MaxSize.x, root.x - 32), Mathf.Min(MaxSize.y, root.y - 32));
                Layout(Card.sizeDelta);
            }
            if (closingAt >= 0)
            {
                float k = (Time.unscaledTime - closingAt) / .14f;
                Group.alpha = 1 - Mathf.Clamp01(k);
                if (k >= 1) Destroy(gameObject);
                return;
            }
            float e = UiKit.EaseOut((Time.unscaledTime - shownAt) / .2f);
            Group.alpha = e;
            float s = GameSettings.ReduceMotion ? 1 : Mathf.Lerp(.97f, 1, e);
            Card.localScale = new Vector3(s, s, 1);
        }

        protected virtual void OnDestroy()
        {
            if (closingAt < 0) { UiStack.Remove(this); TalesUi.Closed(this); }
        }
    }
}
