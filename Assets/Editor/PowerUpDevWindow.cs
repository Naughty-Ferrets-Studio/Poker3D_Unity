#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
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
    /// It works while the game is in Play mode. You can act as ANY seat, so you can test accusations and bots too.
    /// </summary>
    public class PowerUpDevWindow : EditorWindow
    {
        private int _acting;                       // the seat you are acting as (0 = you)
        private int _pu, _version, _target = 1, _cardIndex, _targetCardIndex, _roll, _revealIndex;
        private Suit _suit = Suit.Hearts;
        private string _exact = "As";
        private int _accuseTarget = 1, _accuseName, _guess;   // _guess: 0 = lower, 1 = higher
        private string _message = "";
        private Vector2 _scroll;

        // scenario
        private string _myCards = "", _boardCards = "";
        private string[] _oppCards = new string[0];
        private int _autoPlayTo;
        private static readonly string[] AutoPlayNames = { "Off (play normally)", "Flop (3 cards)", "Turn (4 cards)", "River (5 cards)" };
        private static readonly string[] RollNames = { "Real roll", "1", "2", "3", "4", "5", "6" };
        private static readonly string[] AccuseNames = { "bumba", "disguised", "xray (wrong name, to test a false accusation)" };
        private static readonly string[] AccuseIds = { "bumba", "disguised", "xray" };
        private static readonly string[] GuessNames = { "Lower (or equal)", "Higher" };

        [MenuItem("Window/Poker/Power-Up Dev Panel %#d")]
        public static void Open()
        {
            var w = GetWindow<PowerUpDevWindow>("Power-Up Dev");
            w.minSize = new Vector2(340f, 420f);
            w.Show();
        }

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
            int n = eng.Players.Count;
            var seatNames = new List<string>();
            for (int i = 0; i < n; i++) seatNames.Add(i + ": " + eng.Players[i].Name);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawStatus(eng, table);
            DrawScenario(table, eng, table.MySeat);
            EditorGUILayout.Space(10f);

            // ---- who you are acting as
            EditorGUILayout.LabelField("Act as", EditorStyles.boldLabel);
            _acting = EditorGUILayout.Popup("Seat", Mathf.Clamp(_acting, 0, n - 1), seatNames.ToArray());
            int me = _acting;
            sys.IgnoreTiming = EditorGUILayout.ToggleLeft("Ignore timing (use on any turn, any street)", sys.IgnoreTiming);

            // ---- 1. give a power-up
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("1. Give a power-up to this seat", EditorStyles.boldLabel);
            var names = new List<string>();
            foreach (var d in PowerUpRegistry.All) names.Add(d.Name);
            _pu = EditorGUILayout.Popup("Power-up", Mathf.Clamp(_pu, 0, names.Count - 1), names.ToArray());
            PowerUpDefinition def = PowerUpRegistry.All[_pu];

            var versions = new List<string>();
            foreach (Tier t in def.Tiers) versions.Add(def.VersionName(t) + " (" + t + ")");
            _version = EditorGUILayout.Popup("Version", Mathf.Clamp(_version, 0, versions.Count - 1), versions.ToArray());
            Tier tier = def.Tiers[_version];
            EditorGUILayout.HelpBox(def.Describe(tier) + "\nWhen: " + WindowText(def.Window), MessageType.None);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Give"))
                _message = sys.Give(me, def.Id, tier)
                    ? "Gave " + def.VersionName(tier) + " (" + tier + ") to " + eng.Players[me].Name + "."
                    : "That inventory is full (max " + PowerUpInventory.MaxSlots + ").";
            if (GUILayout.Button("Clear this seat's power-ups")) { sys.Inventory(me).Clear(); _message = "Cleared."; }
            EditorGUILayout.EndHorizontal();

            // ---- 2. options
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("2. Options (each power-up reads only what it needs)", EditorStyles.boldLabel);
            _target = EditorGUILayout.Popup("Target seat", Mathf.Clamp(_target, 0, n - 1), seatNames.ToArray());
            _cardIndex = EditorGUILayout.IntSlider("Card index", _cardIndex, 0, 4);
            EditorGUILayout.LabelField("   your hole card 0-2 (Bumba swap, Disguised, Snatch gives it), or board card 0-8 (Dice roll 6)", EditorStyles.miniLabel);
            _targetCardIndex = EditorGUILayout.IntSlider("Their card (Snatch II)", _targetCardIndex, 0, 2);
            _suit = (Suit)EditorGUILayout.EnumPopup("Suit (Bumba II)", _suit);
            _exact = EditorGUILayout.TextField("Exact card (Bumba III, Disguised sticker)", _exact);
            _roll = EditorGUILayout.Popup("Die roll (Dice)", _roll, RollNames);

            // ---- 3. use
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("3. Power-ups held by " + eng.Players[me].Name, EditorStyles.boldLabel);
            var items = new List<PowerUpInstance>(sys.Inventory(me).Items);
            if (items.Count == 0) EditorGUILayout.LabelField("(none)");
            foreach (PowerUpInstance item in items)
            {
                EditorGUILayout.LabelField(item.ToString(), EditorStyles.boldLabel);
                EditorGUILayout.LabelField(item.Description, EditorStyles.wordWrappedMiniLabel);
                string why = sys.CanUse(me, item);
                if (why != null) EditorGUILayout.HelpBox("Can't use now: " + why, MessageType.None);
                if (GUILayout.Button("Use " + item.Definition.VersionName(item.Tier)))
                    Report(sys.Use(me, item, BuildRequest()));
            }

            // ---- active effects (Bumba: sit on a card, then sneak it in)
            var effects = sys.Effects(me);
            if (effects.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Effects on the table for " + eng.Players[me].Name, EditorStyles.boldLabel);
                foreach (var eff in effects)
                {
                    EditorGUILayout.LabelField(eff.Definition.VersionName(eff.Tier) + (eff.Definition.Accusable ? "  (can be accused)" : ""));
                    if (eff.Definition.FollowUpLabel != null && GUILayout.Button(eff.Definition.FollowUpLabel + "  (uses 'Card index' as the hole card to swap out)"))
                        Report(sys.UseFollowUp(me, eff, BuildRequest()));
                }
            }
            var held = eng.HeldCard(me);
            if (held.HasValue) EditorGUILayout.HelpBox("Sitting on: " + held.Value, MessageType.None);
            var dis = eng.GetDisguise(me);
            if (dis.HasValue) EditorGUILayout.HelpBox("Sticker " + dis.Value.Sticker + " covers the real " + dis.Value.Real, MessageType.None);

            DrawAccuse(eng, sys, seatNames, me);
            DrawPauses(eng, table, me);

            if (_message.Length > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(_message, MessageType.Info);
            }
            EditorGUILayout.EndScrollView();
        }

        // ------------------------------------------------------------ status line
        private static string WindowText(UseWindow w) =>
            w == UseWindow.BetweenHands ? "between hands (shapes the next hand)" :
            w == UseWindow.AllInRunout ? "when everyone left is all-in, before the rest of the board" : "during a hand";

        private void DrawStatus(GameEngine eng, PokerTable3D table)
        {
            string street = eng.HandOver ? "no hand running" : eng.Street.ToString();
            string turn = eng.Current >= 0 ? eng.Players[eng.Current].Name : "-";
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Street: " + street + "     Turn: " + turn + "     Board: " + eng.Community.Count + " cards");
            var r = eng.NextRules;
            var flags = new List<string>();
            if (r.BombPot) flags.Add("Bomb Pot");
            if (r.Joker) flags.Add("Joker");
            if (r.WildTwos) flags.Add("Wild Twos");
            if (r.Cross) flags.Add("The Cross");
            foreach (int s in r.ShanKoeMee) flags.Add("Shan Koe Mee (" + eng.Players[s].Name + ")");
            EditorGUILayout.LabelField("Next hand rules: " + (flags.Count == 0 ? "normal" : string.Join(", ", flags)));
            var cur = eng.Rules;
            var now = new List<string>();
            if (cur.BombPot) now.Add("Bomb Pot");
            if (cur.Joker) now.Add("Joker");
            if (cur.WildTwos) now.Add("Wild Twos");
            if (cur.Cross) now.Add("The Cross");
            if (eng.RunCount > 1) now.Add("run x" + eng.RunCount);
            if (!eng.HandOver && now.Count > 0) EditorGUILayout.LabelField("This hand: " + string.Join(", ", now));
        }

        // ------------------------------------------------------------ accusations
        private void DrawAccuse(GameEngine eng, PowerUpSystem sys, List<string> seatNames, int me)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("4. Accuse someone (acting seat is the accuser)", EditorStyles.boldLabel);
            _accuseTarget = EditorGUILayout.Popup("Accuse", Mathf.Clamp(_accuseTarget, 0, seatNames.Count - 1), seatNames.ToArray());
            _accuseName = EditorGUILayout.Popup("Name the power-up", _accuseName, AccuseNames);
            _guess = EditorGUILayout.Popup("Guess (Disguised): the real card is", _guess, GuessNames);
            _revealIndex = EditorGUILayout.IntSlider("Caught Bumba thief shows card", _revealIndex, 0, 2);
            if (GUILayout.Button("Accuse"))
                Report(sys.Accuse(new AccuseRequest
                {
                    Accuser = me, Target = _accuseTarget, NamedId = AccuseIds[_accuseName],
                    GuessHigher = _guess == 1, RevealIndex = _revealIndex,
                }));
            EditorGUILayout.LabelField(sys.HasAccused(me) ? "This seat has used its accusation this hand." : "This seat still has its accusation.", EditorStyles.miniLabel);
        }

        // ------------------------------------------------------------ waiting states
        private void DrawPauses(GameEngine eng, PokerTable3D table, int me)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Pauses", EditorStyles.boldLabel);
            table.devPauseAtRunout = EditorGUILayout.ToggleLeft("Pause when everyone is all-in (to test Run It Twice / Thrice)", table.devPauseAtRunout);
            if (eng.RunoutPending)
            {
                EditorGUILayout.HelpBox("Everyone left is all-in. Run It can be used now.", MessageType.Warning);
                if (GUILayout.Button("Continue: deal the rest of the board")) table.DevContinueRunout();
            }
            if (eng.AwaitingDiscard)
            {
                EditorGUILayout.HelpBox("A Pineapple discard is due.", MessageType.Warning);
                foreach (var kv in eng.PendingDiscards)
                {
                    if (kv.Value != eng.Street || eng.Players[kv.Key].Folded) continue;
                    var hole = eng.Players[kv.Key].Hole;
                    EditorGUILayout.BeginHorizontal();
                    for (int i = 0; i < hole.Count; i++)
                        if (GUILayout.Button(eng.Players[kv.Key].Name + " discards " + hole[i])) { eng.DiscardHole(kv.Key, i); _message = "Discarded."; break; }
                    EditorGUILayout.EndHorizontal();
                    break;
                }
            }
        }

        private void Report(UseResult res)
        {
            _message = (res.Ok ? "OK: " : "No: ") + res.Message;
            if (res.Secret.Count > 0) _message += "   You see: " + string.Join(" ", res.Secret);
        }

        // ------------------------------------------------------------ scenario: deal an exact situation, skip ahead
        private void DrawScenario(PokerTable3D table, GameEngine eng, int me)
        {
            EditorGUILayout.LabelField("Scenario: deal an exact situation", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Cards like As Kd 7h (ranks 2-9 T J Q K A, suits c d h s). Blank = random. Set next-hand rules first (Bomb Pot, The Cross...) by using those power-ups.", EditorStyles.wordWrappedMiniLabel);

            if (_oppCards.Length != eng.Players.Count) System.Array.Resize(ref _oppCards, eng.Players.Count);
            _myCards = EditorGUILayout.TextField("Your 2 cards", _myCards);
            _boardCards = EditorGUILayout.TextField(eng.NextRules.Cross ? "Board (up to 9, The Cross)" : "Board (up to 5)", _boardCards);
            for (int i = 0; i < eng.Players.Count; i++)
                if (i != me) _oppCards[i] = EditorGUILayout.TextField(eng.Players[i].Name + "'s cards", _oppCards[i] ?? "");
            _autoPlayTo = EditorGUILayout.Popup("Auto check/call until", _autoPlayTo, AutoPlayNames);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Deal this scenario")) DealScenario(table, eng, me);
            if (GUILayout.Button("Skip this hand ahead now"))
            {
                if (_autoPlayTo == 0) _message = "Pick Flop, Turn or River in 'Auto check/call until' first.";
                else { table.DevSkipToBoard(SkipCards(eng)); _message = "Checking / calling until the board has " + SkipCards(eng) + " cards."; }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(table.IsBusy ? "A hand is running now." : "No hand running: Deal this scenario starts one.", EditorStyles.miniLabel);
        }

        // board cards that mean "flop / turn / river" (the Cross deals 5 / 7 / 9)
        private int SkipCards(GameEngine eng)
        {
            bool cross = eng.Rules.Cross || (eng.HandOver && eng.NextRules.Cross);
            return cross ? 3 + _autoPlayTo * 2 : _autoPlayTo + 2;   // Cross: 5 / 7 / 9 cards, normal: 3 / 4 / 5
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

            table.DevSkipToBoard(_autoPlayTo == 0 ? 0 : SkipCards(eng));
            if (table.DevDealNow()) _message = "Dealt your scenario.";
            else _message = "A hand is still running, so the scenario is waiting. It is used the next time a hand is dealt (press Deal hand, or New game first).";
        }

        private UseRequest BuildRequest()
        {
            var req = new UseRequest
            {
                TargetSeat = _target,
                CardIndex = _cardIndex,
                TargetCardIndex = _targetCardIndex,
                ChosenSuit = _suit,
                ForcedRoll = _roll == 0 ? (int?)null : _roll,
                // for Bumba the caught thief's shown card uses the accuse slider, not this one
            };
            if (CardParser.TryParse(_exact, out Card c)) req.ChosenCard = c;
            return req;
        }
    }
}
#endif
