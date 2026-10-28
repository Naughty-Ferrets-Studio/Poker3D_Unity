#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Holdem;
using Holdem.PowerUps;
using HoldemTable;

namespace HoldemEditor
{
    /// <summary>
    /// Power-up developer window. Open it from the menu Window > Poker > Power-Up Dev Panel,
    /// or press Cmd+Shift+D (Mac) / Ctrl+Shift+D (Windows). Drag its tab out to float it or dock it anywhere, like Test Runner.
    /// It works while the game is in Play mode.
    /// </summary>
    public class PowerUpDevWindow : EditorWindow
    {
        private int _pu, _version, _target = 1, _cardIndex, _roll;
        private Suit _suit = Suit.Hearts;
        private string _exact = "As";
        private string _message = "";
        private Vector2 _scroll;

        private static readonly string[] RollNames = { "Real roll", "1", "2", "3", "4", "5", "6" };

        [MenuItem("Window/Poker/Power-Up Dev Panel %#d")]
        public static void Open()
        {
            var w = GetWindow<PowerUpDevWindow>("Power-Up Dev");
            w.minSize = new Vector2(320f, 400f);
            w.Show();
        }

        // keep the window live while the game runs (hands change, turns change)
        private void OnInspectorUpdate() { if (Application.isPlaying) Repaint(); }

        private void OnGUI()
        {
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Press Play first. This window controls the running game.", MessageType.Info);
                return;
            }
            var table = Object.FindFirstObjectByType<PokerTable3D>();
            if (table == null || table.Engine == null || table.PowerUps == null || table.MySeat < 0)
            {
                EditorGUILayout.HelpBox("No poker table is running.", MessageType.Warning);
                return;
            }
            GameEngine eng = table.Engine;
            PowerUpSystem sys = table.PowerUps;
            int me = table.MySeat;

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            // ---- 1. give a power-up
            EditorGUILayout.LabelField("1. Give yourself a power-up", EditorStyles.boldLabel);
            var names = new List<string>();
            foreach (var d in PowerUpRegistry.All) names.Add(d.Name);
            _pu = EditorGUILayout.Popup("Power-up", Mathf.Clamp(_pu, 0, names.Count - 1), names.ToArray());
            PowerUpDefinition def = PowerUpRegistry.All[_pu];

            var versions = new List<string>();
            foreach (Tier t in def.Tiers) versions.Add(def.VersionName(t) + " (" + t + ")");
            _version = EditorGUILayout.Popup("Version", Mathf.Clamp(_version, 0, versions.Count - 1), versions.ToArray());
            Tier tier = def.Tiers[_version];
            EditorGUILayout.HelpBox(def.Describe(tier), MessageType.None);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Give to me"))
                _message = sys.Give(me, def.Id, tier)
                    ? "Gave " + def.VersionName(tier) + " (" + tier + ")."
                    : "Your inventory is full (max " + PowerUpInventory.MaxSlots + ").";
            if (GUILayout.Button("Clear my power-ups")) { sys.Inventory(me).Clear(); _message = "Cleared."; }
            EditorGUILayout.EndHorizontal();

            sys.IgnoreTiming = EditorGUILayout.ToggleLeft("Ignore timing (use on any turn, any street)", sys.IgnoreTiming);

            // ---- 2. options
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("2. Options (each power-up reads only what it needs)", EditorStyles.boldLabel);
            var seatNames = new List<string>();
            for (int i = 0; i < eng.Players.Count; i++) seatNames.Add(i + ": " + eng.Players[i].Name);
            _target = EditorGUILayout.Popup("Target seat", Mathf.Clamp(_target, 0, seatNames.Count - 1), seatNames.ToArray());
            _cardIndex = EditorGUILayout.IntSlider("Card index", _cardIndex, 0, 4);
            EditorGUILayout.LabelField("   hole cards: 0 or 1.   board cards (Dice roll 6): 0 to 4", EditorStyles.miniLabel);
            _suit = (Suit)EditorGUILayout.EnumPopup("Suit (Bumba II)", _suit);
            _exact = EditorGUILayout.TextField("Exact card (Bumba III), like As or 7h", _exact);
            _roll = EditorGUILayout.Popup("Die roll (Dice)", _roll, RollNames);

            // ---- 3. use
            EditorGUILayout.Space();
            string street = eng.HandOver ? "no hand" : eng.Street.ToString();
            string turn = eng.Current >= 0 ? eng.Players[eng.Current].Name : "-";
            EditorGUILayout.LabelField("3. Your power-ups   (street: " + street + ", turn: " + turn + ")", EditorStyles.boldLabel);

            var items = new List<PowerUpInstance>(sys.Inventory(me).Items);
            if (items.Count == 0) EditorGUILayout.LabelField("(none)");
            foreach (PowerUpInstance item in items)
            {
                EditorGUILayout.LabelField(item.ToString(), EditorStyles.boldLabel);
                EditorGUILayout.LabelField(item.Description, EditorStyles.wordWrappedMiniLabel);
                string why = sys.CanUse(me, item);
                if (why != null) EditorGUILayout.HelpBox("Can't use now: " + why, MessageType.None);
                if (GUILayout.Button("Use " + item.Definition.VersionName(item.Tier)))
                {
                    UseResult res = sys.Use(me, item, BuildRequest());
                    _message = (res.Ok ? "OK: " : "No: ") + res.Message;
                    if (res.Secret.Count > 0) _message += "   You see: " + string.Join(" ", res.Secret);
                }
            }

            if (_message.Length > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(_message, MessageType.Info);
            }
            EditorGUILayout.EndScrollView();
        }

        private UseRequest BuildRequest()
        {
            var req = new UseRequest
            {
                TargetSeat = _target,
                CardIndex = _cardIndex,
                ChosenSuit = _suit,
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
#endif
