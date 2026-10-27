using System.Collections.Generic;
using UnityEngine;
using Holdem;
using Holdem.PowerUps;

namespace HoldemTable
{
    /// <summary>
    /// Developer mode. Press Cmd+Shift+D (Mac) or Ctrl+Shift+D (Windows/Linux) to open or close it.
    /// Pick a power-up and a tier, give it to yourself, fill in the target / card, and press Use.
    /// "Ignore timing" lets you use it on any turn and from any street, so you can test without playing a whole game.
    /// Added automatically by PokerTable3D. Strip it from release builds with a scripting define if you like.
    /// </summary>
    public class DevPanel : MonoBehaviour
    {
        private PokerTable3D _table;
        private bool _open;

        private int _pu;            // index in PowerUpRegistry.All
        private int _tier;          // Tier as int
        private int _target = 1;    // target seat
        private int _cardIndex;     // hole or board card index
        private int _suit;          // Suit as int
        private string _exact = "As";
        private int _roll;          // 0 = real roll, 1..6 = forced
        private string _message = "";
        private Vector2 _scroll;

        private static readonly string[] SuitNames = { "Clubs", "Diamonds", "Hearts", "Spades" };
        private static readonly string[] TierNames = { "Common", "Rare", "Epic", "Mythic", "Legendary" };
        private static readonly string[] RollNames = { "Real", "1", "2", "3", "4", "5", "6" };

        private void Awake() { _table = GetComponent<PokerTable3D>(); }

        private void OnGUI()
        {
            if (_table == null || _table.Engine == null) return;

            Event ev = Event.current;
            if (ev.type == EventType.KeyDown && ev.keyCode == KeyCode.D && ev.shift && (ev.command || ev.control))
            {
                _open = !_open;
                ev.Use();
            }
            if (!_open) return;

            GUI.depth = -100; // draw on top of the table UI
            float k = Mathf.Max(1f, Screen.height / 720f);
            float w = 380f * k;
            Rect r = new Rect(12f * k, 12f * k, w, Screen.height - 24f * k);
            GUI.Box(r, "DEVELOPER MODE  (Cmd/Ctrl + Shift + D to close)");

            GUILayout.BeginArea(new Rect(r.x + 8f * k, r.y + 24f * k, r.width - 16f * k, r.height - 32f * k));
            _scroll = GUILayout.BeginScrollView(_scroll);

            PowerUpSystem sys = _table.PowerUps;
            GameEngine eng = _table.Engine;
            int me = _table.MySeat;
            if (sys == null || me < 0) { GUILayout.Label("No game running."); GUILayout.EndScrollView(); GUILayout.EndArea(); return; }

            // --- give
            GUILayout.Label("1. Pick a power-up");
            var names = new List<string>();
            foreach (var d in PowerUpRegistry.All) names.Add(d.Name);
            _pu = GUILayout.SelectionGrid(Mathf.Clamp(_pu, 0, names.Count - 1), names.ToArray(), 2);
            GUILayout.Label("2. Pick a tier");
            _tier = GUILayout.SelectionGrid(_tier, TierNames, 3);
            PowerUpDefinition def = PowerUpRegistry.All[_pu];
            GUILayout.Label(def.Describe((Tier)_tier));

            if (GUILayout.Button("Give to me"))
                _message = sys.Give(me, def.Id, (Tier)_tier) ? "Gave " + def.Name + " (" + (Tier)_tier + ")." : "Your inventory is full (max " + PowerUpInventory.MaxSlots + ").";
            if (GUILayout.Button("Clear my power-ups")) { sys.Inventory(me).Clear(); _message = "Cleared."; }
            sys.IgnoreTiming = GUILayout.Toggle(sys.IgnoreTiming, " Ignore timing (use any turn, any street)");

            // --- options for Use
            GUILayout.Space(6f * k);
            GUILayout.Label("3. Options (only what the power-up needs is read)");
            GUILayout.Label("Target seat (0 = you): " + _target);
            _target = Mathf.RoundToInt(GUILayout.HorizontalSlider(_target, 0, Mathf.Max(1, eng.Players.Count - 1)));
            GUILayout.Label("Card index (hole 0-1, board 0-4): " + _cardIndex);
            _cardIndex = Mathf.RoundToInt(GUILayout.HorizontalSlider(_cardIndex, 0, 4));
            GUILayout.Label("Suit (Bumba II)");
            _suit = GUILayout.SelectionGrid(_suit, SuitNames, 4);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Exact card, like As or 7h (Bumba III+)", GUILayout.Width(250f * k));
            _exact = GUILayout.TextField(_exact, 2);
            GUILayout.EndHorizontal();
            GUILayout.Label("Die roll (Dice)");
            _roll = GUILayout.SelectionGrid(_roll, RollNames, 7);

            // --- use
            GUILayout.Space(6f * k);
            GUILayout.Label("4. Your power-ups (Street: " + (eng.HandOver ? "no hand" : eng.Street.ToString()) + ", turn: " + (eng.Current >= 0 ? eng.Players[eng.Current].Name : "-") + ")");
            var items = new List<PowerUpInstance>(sys.Inventory(me).Items);
            if (items.Count == 0) GUILayout.Label("(none)");
            foreach (PowerUpInstance item in items)
            {
                GUILayout.Label(item.ToString());
                string why = sys.CanUse(me, item);
                if (why != null) GUILayout.Label("   can't use now: " + why);
                if (GUILayout.Button("Use " + item.Definition.Name))
                {
                    UseResult res = sys.Use(me, item, BuildRequest());
                    _message = (res.Ok ? "OK: " : "No: ") + res.Message;
                    if (res.Secret.Count > 0) _message += "  You see: " + string.Join(" ", res.Secret);
                }
            }

            GUILayout.Space(6f * k);
            GUILayout.Label(_message);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private UseRequest BuildRequest()
        {
            var req = new UseRequest
            {
                TargetSeat = _target,
                CardIndex = _cardIndex,
                ChosenSuit = (Suit)_suit,
                ForcedRoll = _roll == 0 ? (int?)null : _roll,
            };
            if (TryParseCard(_exact, out Card c)) req.ChosenCard = c;
            return req;
        }

        /// <summary>"As" = ace of spades, "Td" = ten of diamonds, "7h" = seven of hearts.</summary>
        private static bool TryParseCard(string s, out Card card)
        {
            card = default;
            if (string.IsNullOrEmpty(s) || s.Length != 2) return false;
            Rank rank;
            switch (char.ToUpperInvariant(s[0]))
            {
                case 'A': rank = Rank.Ace; break;
                case 'K': rank = Rank.King; break;
                case 'Q': rank = Rank.Queen; break;
                case 'J': rank = Rank.Jack; break;
                case 'T': rank = Rank.Ten; break;
                default:
                    if (s[0] < '2' || s[0] > '9') return false;
                    rank = (Rank)(s[0] - '0');
                    break;
            }
            Suit suit;
            switch (char.ToLowerInvariant(s[1]))
            {
                case 'c': suit = Suit.Clubs; break;
                case 'd': suit = Suit.Diamonds; break;
                case 'h': suit = Suit.Hearts; break;
                case 's': suit = Suit.Spades; break;
                default: return false;
            }
            card = new Card(rank, suit);
            return true;
        }
    }
}
