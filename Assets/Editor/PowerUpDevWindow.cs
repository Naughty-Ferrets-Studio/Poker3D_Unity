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

        // scenario
        private string _myCards = "", _boardCards = "";
        private string[] _oppCards = new string[0];
        private int _autoPlayTo;   // 0 off, 1 flop, 2 turn, 3 river
        private static readonly string[] AutoPlayNames = { "Off (play normally)", "Flop (3 cards)", "Turn (4 cards)", "River (5 cards)" };

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
            var table = PokerTable3D.Current;
            if (table == null || table.Engine == null || table.PowerUps == null || table.MySeat < 0)
            {
                EditorGUILayout.HelpBox("No poker table is running.", MessageType.Warning);
                return;
            }
            GameEngine eng = table.Engine;
            PowerUpSystem sys = table.PowerUps;
            int me = table.MySeat;

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawScenario(table, eng, me);
            EditorGUILayout.Space(10f);

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

        // ------------------------------------------------------------ scenario: deal an exact situation, skip ahead
        private void DrawScenario(PokerTable3D table, GameEngine eng, int me)
        {
            EditorGUILayout.LabelField("Scenario: deal an exact situation", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Cards like As Kd 7h (ranks 2-9 T J Q K A, suits c d h s). Leave blank = random.", EditorStyles.miniLabel);

            if (_oppCards.Length != eng.Players.Count) System.Array.Resize(ref _oppCards, eng.Players.Count);
            _myCards = EditorGUILayout.TextField("Your 2 cards", _myCards);
            _boardCards = EditorGUILayout.TextField("Board (up to 5)", _boardCards);
            for (int i = 0; i < eng.Players.Count; i++)
                if (i != me) _oppCards[i] = EditorGUILayout.TextField(eng.Players[i].Name + "'s cards", _oppCards[i] ?? "");
            _autoPlayTo = EditorGUILayout.Popup("Auto check/call until", _autoPlayTo, AutoPlayNames);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Deal this scenario")) DealScenario(table, eng, me);
            if (GUILayout.Button("Skip this hand ahead now"))
            {
                if (_autoPlayTo == 0) _message = "Pick Flop, Turn or River in 'Auto check/call until' first.";
                else { table.DevSkipToBoard(_autoPlayTo + 2); _message = "Checking / calling until the board has " + (_autoPlayTo + 2) + " cards."; }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(table.IsBusy ? "A hand is running now." : "No hand running: Deal this scenario starts one.", EditorStyles.miniLabel);
        }

        private void DealScenario(PokerTable3D table, GameEngine eng, int me)
        {
            var sc = new Scenario();
            string err = CardParser.TryParseList(_boardCards, out sc.Board);
            if (err != null) { _message = "Board: " + err; return; }

            for (int i = 0; i < eng.Players.Count; i++)
            {
                string text = i == me ? _myCards : _oppCards[i];
                err = CardParser.TryParseList(text, out List<Card> hole);
                if (err != null) { _message = eng.Players[i].Name + ": " + err; return; }
                if (hole.Count > 0) sc.Hole[i] = hole;
            }

            err = ScenarioBuilder.Apply(eng, sc);
            if (err != null) { _message = "Scenario not dealt: " + err; return; }

            table.DevSkipToBoard(_autoPlayTo == 0 ? 0 : _autoPlayTo + 2);
            if (table.DevDealNow()) _message = "Dealt your scenario.";
            else _message = "A hand is still running, so the scenario is waiting. It is used the next time a hand is dealt (press Deal hand, or New game first).";
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
            if (CardParser.TryParse(_exact, out Card c)) req.ChosenCard = c;
            return req;
        }
    }
}
#endif
