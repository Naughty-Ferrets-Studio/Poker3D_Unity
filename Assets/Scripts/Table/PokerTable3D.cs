using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Holdem;

namespace HoldemTable
{
    /// <summary>
    /// Builds a poker table at runtime and plays the engine's DealEvents as a 3D dealing animation.
    /// Add this to ONE empty GameObject. No other scene setup is needed except the card PNGs in
    /// Assets/Resources/Cards/. This step plays hands automatically (bots decide for every seat, including "You");
    /// player buttons come in the next step.
    /// </summary>
    public class PokerTable3D : MonoBehaviour
    {
        [Header("Table")]
        [Range(2, 9)] public int seatCount = GameEngine.DefaultPlayers;
        public bool autoNextHand = true;
        public bool autoPlayHuman = false; // true: a bot also plays your seat (quick testing)

        [Header("Timing (seconds)")]
        public float dealGap = 0.14f;      // pause between cards leaving the deck
        public float cardTravel = 0.35f;   // flight time of one card
        public float thinkTime = 0.7f;     // pause before each player action

        // ------------------------------------------------------------ layout
        private const float RingX = 4.2f;
        // Front-to-back radius of the seat ring. Bigger tables need a rounder ring so neighbouring seats' cards never overlap
        // (checked for every seat count: 2 to 9).
        private static readonly float[] RingZBySeats = { 0f, 0f, 2.2f, 2.2f, 2.2f, 2.2f, 2.35f, 2.7f, 3.0f, 3.35f };
        private const float CardY = 0.02f;
        private const float BoardZ = 0.35f, BoardSpacing = 0.85f;
        private static readonly Vector3 DeckSpawn = new Vector3(0f, 0.26f, -0.9f);
        private static readonly Vector3 BurnPos = new Vector3(1.3f, CardY, -0.9f);
        private static readonly Vector3 ButtonOffset = new Vector3(1.15f, 0f, 0.1f); // just beside a seat's two cards

        // ------------------------------------------------------------ state
        private GameEngine _engine;
        private readonly System.Random _rng = new System.Random();
        private readonly List<CardObject> _all = new List<CardObject>();
        private readonly List<List<CardObject>> _hole = new List<List<CardObject>>();
        private readonly List<CardObject> _board = new List<CardObject>();
        private readonly List<string> _log = new List<string>();
        private Vector3[] _seatPos = new Vector3[0];
        private Quaternion[] _seatYaw = new Quaternion[0];
        private GameObject _dealerButton, _felt, _rim;
        private Camera _cam;
        private bool _busy, _showAll;
        private int _animated, _burned;
        private string _banner = "";

        // your turn: the buttons set these, PlayHand picks them up
        private bool _awaitHuman, _hasChoice;
        private PlayerAction _choiceAction;
        private int _choiceRaiseTo;
        private float _raiseTo;

        // looking at your cards: hold the left mouse button on a card, or hold H for both
        private CardObject _holdCard;
        private bool _holdKey;

        private GUIStyle _label, _labelActive, _small, _bannerStyle, _button, _toggle;
        private float _styleScale;

        // ============================================================ setup
        private void Start()
        {
            BuildTable();
            SetupCamera();
            NewGame(seatCount);
            StartCoroutine(Run());
        }

        private void BuildTable()
        {
            // floor
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            Destroy(floor.GetComponent<Collider>());
            floor.transform.SetParent(transform, false);
            floor.transform.localPosition = new Vector3(0f, -0.4f, 0f);
            floor.transform.localScale = new Vector3(6f, 1f, 6f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = TableMaterials.Lit(new Color(0.10f, 0.11f, 0.13f), 0.05f);

            // wooden rim
            GameObject rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rim.name = "Rim";
            Destroy(rim.GetComponent<Collider>());
            rim.transform.SetParent(transform, false);
            rim.transform.localPosition = new Vector3(0f, -0.07f, 0f);
            _rim = rim;
            rim.GetComponent<MeshRenderer>().sharedMaterial = TableMaterials.Lit(new Color(0.27f, 0.15f, 0.07f), 0.3f);

            // felt (cylinder: scale x/z = diameters, scale y = half the thickness; top surface sits at y = 0)
            GameObject felt = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            felt.name = "Felt";
            Destroy(felt.GetComponent<Collider>());
            felt.transform.SetParent(transform, false);
            felt.transform.localPosition = new Vector3(0f, -0.05f, 0f);
            _felt = felt;
            felt.GetComponent<MeshRenderer>().sharedMaterial = TableMaterials.Lit(new Color(0.07f, 0.38f, 0.22f), 0.1f);

            // the deck
            GameObject deck = GameObject.CreatePrimitive(PrimitiveType.Cube);
            deck.name = "Deck";
            Destroy(deck.GetComponent<Collider>());
            deck.transform.SetParent(transform, false);
            deck.transform.localScale = new Vector3(CardObject.Width, 0.2f, CardObject.Height);
            deck.transform.localPosition = new Vector3(DeckSpawn.x, 0.1f, DeckSpawn.z);
            deck.GetComponent<MeshRenderer>().sharedMaterial = TableMaterials.Lit(new Color(0.55f, 0.65f, 0.85f), 0.1f);

            // dealer button
            _dealerButton = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _dealerButton.name = "Dealer Button";
            Destroy(_dealerButton.GetComponent<Collider>());
            _dealerButton.transform.SetParent(transform, false);
            _dealerButton.transform.localScale = new Vector3(0.4f, 0.015f, 0.4f);
            _dealerButton.GetComponent<MeshRenderer>().sharedMaterial = TableMaterials.Lit(new Color(0.95f, 0.95f, 0.9f), 0.4f);
            _dealerButton.SetActive(false);
        }

        private void SetupCamera()
        {
            _cam = Camera.main;
            if (_cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                _cam = go.AddComponent<Camera>();
            }
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.04f, 0.05f, 0.07f);
            _cam.fieldOfView = 50f;
            _cam.transform.position = new Vector3(0f, 8.2f, -6.2f);
            _cam.transform.LookAt(new Vector3(0f, 0f, 0.1f));
        }

        /// <summary>Seat 0 ("You") sits at the bottom; seats then go clockwise as seen from above.</summary>
        private void LayoutSeats(int n)
        {
            float ringZ = RingZBySeats[Mathf.Clamp(n, 2, 9)];
            _seatPos = new Vector3[n];
            _seatYaw = new Quaternion[n];
            for (int i = 0; i < n; i++)
            {
                float a = i * 2f * Mathf.PI / n;
                var p = new Vector3(-RingX * Mathf.Sin(a), 0f, -ringZ * Mathf.Cos(a));
                _seatPos[i] = p;
                _seatYaw[i] = Quaternion.LookRotation(-p.normalized, Vector3.up); // card tops point at the table centre
            }

            // the table grows with the ring (cylinder scale: x/z = diameters, y = half the thickness)
            float feltX = 2f * (RingX + 1.25f), feltZ = 2f * (ringZ + 1.0f);
            if (_felt != null) _felt.transform.localScale = new Vector3(feltX, 0.05f, feltZ);
            if (_rim != null) _rim.transform.localScale = new Vector3(feltX + 0.8f, 0.04f, feltZ + 0.8f);

            // pull the camera back a little for the bigger tables
            if (_cam != null)
            {
                float f = Mathf.Max(1f, (ringZ + 1.0f) / 3.3f);
                _cam.transform.position = new Vector3(0f, 8.2f * f, -6.2f * f);
                _cam.transform.LookAt(new Vector3(0f, 0f, 0.1f));
            }
        }

        private void NewGame(int seats)
        {
            StopAllCoroutines();
            if (_engine != null) _engine.Logged -= OnLog;

            seatCount = Mathf.Clamp(seats, 2, GameEngine.MaxPlayers);
            _engine = new GameEngine();
            _engine.NewGame(seatCount - 1);
            _engine.Logged += OnLog;

            LayoutSeats(seatCount);
            _hole.Clear();
            for (int i = 0; i < seatCount; i++) _hole.Add(new List<CardObject>());
            ClearCards();

            _log.Clear();
            _banner = "Press \"Deal hand\"";
            _busy = false;
            _awaitHuman = false; _hasChoice = false;
            if (_dealerButton != null) _dealerButton.SetActive(false);
        }

        private void OnLog(string line)
        {
            _log.Add(line);
            if (_log.Count > 40) _log.RemoveAt(0);
        }

        private void ClearCards()
        {
            foreach (CardObject c in _all)
                if (c != null) Destroy(c.gameObject);
            _all.Clear();
            _board.Clear();
            foreach (List<CardObject> h in _hole) h.Clear();
            _animated = 0;
            _burned = 0;
        }

        // ============================================================ positions
        private Vector3 HolePos(int seat, int index)
        {
            var local = new Vector3(index == 0 ? -0.4f : 0.4f, CardY + index * 0.004f, 0f);
            return _seatPos[seat] + _seatYaw[seat] * local;
        }

        private static Vector3 BoardPos(int index)
        {
            return new Vector3((index - 2) * BoardSpacing, CardY, BoardZ);
        }

        // ============================================================ hand flow
        /// <summary>Plays hands one after another (or just one when autoNextHand is off).</summary>
        private IEnumerator Run()
        {
            _busy = true;
            do
            {
                if (_engine.IsGameOver) break;
                yield return StartCoroutine(PlayHand());
                if (autoNextHand && !_engine.IsGameOver) yield return new WaitForSeconds(2.5f);
            }
            while (autoNextHand && !_engine.IsGameOver);

            _banner = _engine.IsGameOver ? "Game over - press \"New game\"" : "Press \"Deal hand\"";
            _busy = false;
        }

        private IEnumerator PlayHand()
        {
            ClearCards();
            _banner = "";
            _engine.StartHand();
            _animated = 0;
            _burned = 0;

            int n = _engine.Players.Count;
            var wasFolded = new bool[n];
            for (int i = 0; i < n; i++) wasFolded[i] = _engine.Players[i].Folded;

            PlaceDealerButton();
            yield return StartCoroutine(PlayNewEvents());

            int guard = 0;
            while (!_engine.HandOver && guard++ < 600)
            {
                Player p = _engine.Players[_engine.Current];
                if (p.IsHuman && !autoPlayHuman)
                {
                    // your turn: wait for a button press (no time limit)
                    _raiseTo = _engine.MinRaiseTo;
                    _hasChoice = false;
                    _awaitHuman = true;
                    while (!_hasChoice) yield return null;
                    _awaitHuman = false;

                    ActResult res = _engine.TryAct(_engine.Current, _choiceAction, _choiceRaiseTo);
                    if (res != ActResult.Ok)
                    {
                        _banner = "That move is not allowed right now (" + res + ")";
                        continue; // ask again
                    }
                    _banner = "";
                }
                else
                {
                    yield return new WaitForSeconds(thinkTime);
                    var decision = Bot.Decide(_engine, p, _rng);
                    _engine.Act(decision.Item1, decision.Item2);
                }
                MuckNewFolds(wasFolded);
                yield return StartCoroutine(PlayNewEvents());
            }

            if (_engine.ShowdownReached)
            {
                for (int i = 0; i < n; i++)
                {
                    if (_engine.Players[i].Folded || _engine.Players[i].IsOut) continue;
                    foreach (CardObject c in _hole[i])
                        if (c != null && !c.FaceUp) c.StartCoroutine(c.Flip(true, 0.45f));
                }
                yield return new WaitForSeconds(0.7f);
            }
        }

        private void PlaceDealerButton()
        {
            int d = _engine.Dealer;
            if (_dealerButton == null || d < 0 || d >= _seatPos.Length) return;
            _dealerButton.SetActive(true);
            _dealerButton.transform.position = _seatPos[d] + _seatYaw[d] * (ButtonOffset + new Vector3(0f, 0.03f, 0f));
        }

        /// <summary>Folded players' cards slide back to the deck and disappear.</summary>
        private void MuckNewFolds(bool[] wasFolded)
        {
            for (int i = 0; i < wasFolded.Length; i++)
            {
                bool folded = _engine.Players[i].Folded;
                if (folded && !wasFolded[i])
                {
                    foreach (CardObject c in _hole[i])
                        if (c != null) c.StartCoroutine(c.MoveAndDestroy(DeckSpawn, 0.45f));
                    _hole[i].Clear();
                }
                wasFolded[i] = folded;
            }
        }

        // ============================================================ dealing animation
        /// <summary>Plays every DealEvent the engine has produced that we have not animated yet.</summary>
        private IEnumerator PlayNewEvents()
        {
            IReadOnlyList<DealEvent> events = _engine.DealEvents;
            bool any = false;
            while (_animated < events.Count)
            {
                DealCard(events[_animated]);
                _animated++;
                any = true;
                yield return new WaitForSeconds(dealGap);
            }
            if (any) yield return new WaitForSeconds(cardTravel);
        }

        private void DealCard(DealEvent e)
        {
            CardObject card = CardObject.Create(e.Card, transform);
            _all.Add(card);
            card.SetImmediate(DeckSpawn, Quaternion.identity, false);

            Vector3 target;
            Quaternion yaw = Quaternion.identity;
            bool faceUp = false;

            switch (e.Kind)
            {
                case DealKind.Hole:
                    int index = _hole[e.Seat].Count;
                    _hole[e.Seat].Add(card);
                    target = HolePos(e.Seat, index);
                    yaw = _seatYaw[e.Seat];
                    faceUp = _showAll && !_engine.Players[e.Seat].IsHuman; // every hole card starts face-down; click your own cards to look at them
                    break;
                case DealKind.Burn:
                    target = BurnPos + Vector3.up * (_burned * 0.004f);
                    _burned++;
                    break;
                default: // Flop, Turn, River
                    target = BoardPos(_board.Count);
                    _board.Add(card);
                    faceUp = true;
                    break;
            }
            card.StartCoroutine(card.MoveTo(target, yaw, faceUp, cardTravel));
        }

        private void SetShowAll(bool on)
        {
            _showAll = on;
            for (int i = 0; i < _hole.Count && i < _engine.Players.Count; i++)
            {
                if (_engine.Players[i].IsHuman) continue;
                foreach (CardObject c in _hole[i])
                    if (c != null && c.FaceUp != on) c.StartCoroutine(c.Flip(on, 0.35f));
            }
        }

        // ============================================================ on-screen text and buttons
        private void EnsureStyles(float k)
        {
            if (_label != null && Mathf.Abs(_styleScale - k) < 0.01f) return;
            _styleScale = k;
            _label = new GUIStyle(GUI.skin.label);
            _label.fontSize = Mathf.RoundToInt(14f * k);
            _label.alignment = TextAnchor.MiddleCenter;
            _label.fontStyle = FontStyle.Bold;
            _label.normal.textColor = Color.white;

            _labelActive = new GUIStyle(_label);
            _labelActive.normal.textColor = new Color(1f, 0.85f, 0.2f);

            _small = new GUIStyle(GUI.skin.label);
            _small.fontSize = Mathf.RoundToInt(13f * k);
            _small.normal.textColor = Color.white;

            _bannerStyle = new GUIStyle(_label);
            _bannerStyle.fontSize = Mathf.RoundToInt(20f * k);

            _button = new GUIStyle(GUI.skin.button);
            _button.fontSize = Mathf.RoundToInt(14f * k);

            _toggle = new GUIStyle(GUI.skin.toggle);
            _toggle.fontSize = Mathf.RoundToInt(13f * k);
            _toggle.normal.textColor = Color.white;
            _toggle.onNormal.textColor = Color.white;
            _toggle.hover.textColor = Color.white;
            _toggle.onHover.textColor = Color.white;
        }

        private void ShadowLabel(Rect r, string text, GUIStyle style)
        {
            Color c = style.normal.textColor;
            style.normal.textColor = Color.black;
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, style);
            style.normal.textColor = c;
            GUI.Label(r, text, style);
        }

        private void OnGUI()
        {
            if (_engine == null || _cam == null) return;
            float k = Mathf.Max(1f, Screen.height / 720f);
            EnsureStyles(k);

            // seat labels
            for (int i = 0; i < _engine.Players.Count && i < _seatPos.Length; i++)
            {
                Player p = _engine.Players[i];
                Vector3 world = _seatPos[i] + _seatYaw[i] * new Vector3(0f, 0f, -1.05f);
                Vector3 sp = _cam.WorldToScreenPoint(world);
                if (sp.z <= 0f) continue;

                string status = p.IsOut ? "OUT" : p.Folded ? "folded" : p.AllIn ? "ALL-IN" : (p.Bet > 0 ? "bet " + p.Bet : "");
                string text = p.Name + "\n$" + p.Chips + (status.Length > 0 ? "\n" + status : "");
                if (p.IsHuman && !p.Folded && !p.IsOut && !_engine.HandOver && i < _hole.Count && _hole[i].Count > 0)
                    text += "\n(hold click on a card, or hold H, to look)";
                bool active = !_engine.HandOver && i == _engine.Current;
                float w = 150f * k, h = 64f * k;
                ShadowLabel(new Rect(sp.x - w / 2f, Screen.height - sp.y - h / 2f, w, h), text, active ? _labelActive : _label);
            }

            // pot and street
            if (!_engine.HandOver || _animated > 0)
            {
                Vector3 sp = _cam.WorldToScreenPoint(new Vector3(0f, 0f, BoardZ + 1.0f));
                if (sp.z > 0f)
                {
                    string street = _engine.HandOver ? "Hand over" : _engine.Street.ToString();
                    float w = 220f * k, h = 40f * k;
                    ShadowLabel(new Rect(sp.x - w / 2f, Screen.height - sp.y - h / 2f, w, h),
                                "Pot $" + _engine.Pot + "   (" + street + ")", _label);
                }
            }

            // banner
            if (_banner.Length > 0)
                ShadowLabel(new Rect(0f, 10f * k, Screen.width, 36f * k), _banner, _bannerStyle);

            // controls
            float x = 12f * k, y = 12f * k, bw = 170f * k, bh = 30f * k, gap = 6f * k;

            GUI.enabled = !_busy;
            if (GUI.Button(new Rect(x, y, bw, bh), _engine.IsGameOver ? "New game" : "Deal hand", _button))
            {
                if (_engine.IsGameOver) NewGame(seatCount);
                else StartCoroutine(Run());
            }
            GUI.enabled = true;
            y += bh + gap;

            if (GUI.Button(new Rect(x, y, bw, bh), "New game", _button)) NewGame(seatCount);
            y += bh + gap;

            GUI.Label(new Rect(x, y, bw * 0.5f, bh), "Players: " + seatCount, _small);
            if (GUI.Button(new Rect(x + bw * 0.55f, y, bw * 0.2f, bh), "-", _button)) NewGame(seatCount - 1);
            if (GUI.Button(new Rect(x + bw * 0.78f, y, bw * 0.2f, bh), "+", _button)) NewGame(seatCount + 1);
            y += bh + gap;

            bool show = GUI.Toggle(new Rect(x, y, bw, bh), _showAll, " Show all cards", _toggle);
            if (show != _showAll) SetShowAll(show);
            y += bh;

            autoNextHand = GUI.Toggle(new Rect(x, y, bw, bh), autoNextHand, " Auto next hand", _toggle);
            y += bh;

            autoPlayHuman = GUI.Toggle(new Rect(x, y, bw, bh), autoPlayHuman, " Auto-play me", _toggle);

            // your Fold / Call / Raise buttons
            if (_awaitHuman && !_hasChoice && !_engine.HandOver && _engine.Current >= 0 && _engine.Players[_engine.Current].IsHuman)
                DrawActionBar(k);

            // log
            int lines = Mathf.Min(7, _log.Count);
            float lh = 20f * k;
            float ly = Screen.height - lines * lh - 12f * k;
            for (int i = 0; i < lines; i++)
                ShadowLabel(new Rect(12f * k, ly + i * lh, 600f * k, lh), _log[_log.Count - lines + i], _small);

            // look at your cards: hold the left mouse button on one of them, or hold H to see both
            Event ev = Event.current;
            if (ev.type == EventType.MouseDown && ev.button == 0)
            {
                CardObject c = OwnCardAt(ev.mousePosition);
                if (c != null) { _holdCard = c; ev.Use(); }
            }
            else if (ev.rawType == EventType.MouseUp && ev.button == 0) _holdCard = null;
            else if (ev.type == EventType.KeyDown && ev.keyCode == KeyCode.H) _holdKey = true;
            else if (ev.rawType == EventType.KeyUp && ev.keyCode == KeyCode.H) _holdKey = false;
        }

        private void OnApplicationFocus(bool focus)
        {
            if (!focus) { _holdCard = null; _holdKey = false; } // never get stuck "holding" after alt-tab
        }

        private void Update()
        {
            if (_engine == null) return;
            if (_engine.HandOver && _engine.ShowdownReached) return; // showdown turns the cards over by itself

            int seat = HumanSeat();
            if (seat < 0 || seat >= _hole.Count) return;
            foreach (CardObject c in _hole[seat])
            {
                if (c == null) continue;
                bool want = _holdKey || c == _holdCard; // face-up only while held
                if (c.FaceUp != want) c.StartCoroutine(c.Flip(want, 0.2f));
            }
        }

        // ============================================================ your turn
        private void DrawActionBar(float k)
        {
            Player me = _engine.Players[_engine.Current];
            int toCall = Mathf.Min(_engine.CurrentBet - me.Bet, me.Chips);
            bool canRaise = me.Chips > _engine.CurrentBet - me.Bet && me.CanRaise;
            int maxTo = _engine.MaxRaiseTo;
            int minTo = Mathf.Min(_engine.MinRaiseTo, maxTo);

            float bw = 130f * k, bh = 40f * k, gap = 8f * k;
            float x = Screen.width - (3f * bw + 2f * gap) - 12f * k;
            float y = Screen.height - bh - 12f * k;

            if (canRaise && maxTo > minTo)
            {
                _raiseTo = Mathf.Clamp(_raiseTo, minTo, maxTo);
                _raiseTo = GUI.HorizontalSlider(new Rect(x, y - 26f * k, 3f * bw + 2f * gap, 20f * k), _raiseTo, minTo, maxTo);
            }
            int raiseTo = Mathf.Clamp(Mathf.RoundToInt(_raiseTo), minTo, maxTo);

            if (GUI.Button(new Rect(x, y, bw, bh), "Fold", _button)) Submit(PlayerAction.Fold, 0);

            string callText = toCall > 0 ? "Call " + toCall : "Check";
            if (GUI.Button(new Rect(x + bw + gap, y, bw, bh), callText, _button)) Submit(PlayerAction.CheckCall, 0);

            GUI.enabled = canRaise;
            string raiseText = raiseTo >= maxTo ? "All-in " + maxTo : (_engine.CurrentBet == 0 ? "Bet " : "Raise to ") + raiseTo;
            if (GUI.Button(new Rect(x + 2f * (bw + gap), y, bw, bh), raiseText, _button)) Submit(PlayerAction.Raise, raiseTo);
            GUI.enabled = true;
        }

        private void Submit(PlayerAction action, int raiseTo)
        {
            _choiceAction = action;
            _choiceRaiseTo = raiseTo;
            _hasChoice = true;
        }

        // ============================================================ look at your cards (hold)
        private int HumanSeat()
        {
            for (int i = 0; i < _engine.Players.Count; i++)
                if (_engine.Players[i].IsHuman) return i;
            return -1;
        }

        /// <summary>Returns the card under the mouse if it is one of your own hole cards, otherwise null.</summary>
        private CardObject OwnCardAt(Vector2 guiMouse)
        {
            int seat = HumanSeat();
            if (seat < 0 || seat >= _hole.Count || _cam == null) return null;

            Ray ray = _cam.ScreenPointToRay(new Vector3(guiMouse.x, Screen.height - guiMouse.y, 0f));
            if (!Physics.Raycast(ray, out RaycastHit hit, 100f)) return null;

            CardObject card = hit.collider.GetComponentInParent<CardObject>();
            return card != null && _hole[seat].Contains(card) ? card : null;
        }
    }
}