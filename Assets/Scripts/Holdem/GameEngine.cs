using System;
using System.Collections.Generic;
using System.Linq;

namespace Holdem
{

public enum Street { Preflop, Flop, Turn, River, Showdown }

public enum PlayerAction { Fold, CheckCall, Raise }

// FIX 3: result type for the strict TryAct API
public enum ActResult { Ok, NoActiveHand, NotYourTurn, InvalidAction, IllegalRaise }

/// <summary>Pure game logic - no UI code, so it can be reused in Console/Unity/etc.</summary>
public class GameEngine
{
    public const int SmallBlind = 10, BigBlind = 20, StartingChips = 1000;

    public List<Player> Players { get; } = new List<Player>();
    public List<Card> Community { get; } = new List<Card>();
    public Street Street { get; private set; }
    public int Dealer { get; private set; } = -1;
    public int Current { get; private set; } = -1;
    public int CurrentBet { get; private set; }
    public int MinRaise { get; private set; } = BigBlind;
    public bool HandOver { get; private set; } = true;
    public bool ShowdownReached { get; private set; }
    public Player PreflopAggressor { get; private set; } // last player to make a full raise preflop (null if none)

    public event Action<string> Logged;

    private readonly CardDealer _dealer = new CardDealer();

    /// <summary>Every card movement of the current hand, in order (burns, hole cards, board). Use it to drive 3D animation.</summary>
    public IReadOnlyList<DealEvent> DealEvents => _dealer.Events;

    /// <summary>Cards still in the deck. For power-ups only; never show this to players.</summary>
    public IReadOnlyList<Card> DeckCards => _dealer.DeckCards;

    /// <summary>Shuffled order of the current hand's deck. Save it to replay the hand exactly.</summary>
    public IReadOnlyList<Card> DeckOrder => _dealer.DeckOrder;

    /// <summary>Start the next hand from a specific deck (replay / tests). Consumed by the next StartHand().</summary>
    public Deck NextDeckOverride { get; set; }

    // FIX 1: secure by default; replace with a fixed function in tests to get a known deck order.
    // Must return a value in [0, max).
    public Func<int, int> NextInt { get; set; } = SecureRng.Next;

    public int Pot => Players.Sum(p => p.Contrib);
    public int MinRaiseTo => CurrentBet + MinRaise;
    public int MaxRaiseTo => Current >= 0 ? Players[Current].Bet + Players[Current].Chips : 0;

    // FIX 5: no longer hardcodes Players[0]. Game ends when fewer than 2 players have chips,
    // or when every human is out (keeps the old single-player behaviour: you bust -> game over).
    private bool HumansAllOut => Players.Any(p => p.IsHuman) && Players.Where(p => p.IsHuman).All(p => p.Chips <= 0);
    public bool IsGameOver => Players.Count == 0 || HumansAllOut || Players.Count(p => p.Chips > 0) < 2;

    private void Log(string s) => Logged?.Invoke(s);

    /// <summary>Most seats a table can have (you + 8 bots).</summary>
    public const int MaxPlayers = 9;

    /// <summary>Normal table size (you + 5 bots).</summary>
    public const int DefaultPlayers = 6;

    /// <summary>Starts a new game: you plus botCount bots (1 to MaxPlayers - 1; other values are clamped).</summary>
    public void NewGame(int botCount)
    {
        string[] botNames = { "Alice", "Bob", "Cara", "Dan", "Eve", "Finn", "Gus", "Hana" };
        botCount = Math.Max(1, Math.Min(botCount, MaxPlayers - 1));
        Players.Clear();
        Players.Add(new Player { Name = "You", Chips = StartingChips, IsHuman = true });
        for (int i = 0; i < botCount; i++)
        {
            string name = botNames[i % botNames.Length];
            Players.Add(new Player { Name = name, Chips = StartingChips, Style = BotStyle.For(name) });
        }
        Community.Clear();
        Dealer = -1; Current = -1; HandOver = true; ShowdownReached = false;
    }

    // ---------------------------------------------------------------- hand flow
    public void StartHand()
    {
        if (!HandOver || IsGameOver) return;

        foreach (var p in Players)
        {
            p.IsOut = p.Chips <= 0;
            p.Folded = p.IsOut;
            p.AllIn = false; p.HasActed = false; p.CanRaise = true;
            p.Bet = 0; p.Contrib = 0;
            p.Hole.Clear();
        }
        Community.Clear();
        if (NextDeckOverride != null) { _dealer.ReplayHand(NextDeckOverride); NextDeckOverride = null; }
        else _dealer.NewHand(NextInt);
        HandOver = false; ShowdownReached = false;
        PreflopAggressor = null;
        Street = Street.Preflop;

        Dealer = NextSeat(Dealer, p => !p.IsOut);
        int active = Players.Count(p => !p.IsOut);
        int sb = active == 2 ? Dealer : NextSeat(Dealer, p => !p.IsOut); // heads-up: dealer is small blind
        int bb = NextSeat(sb, p => !p.IsOut);

        Log($"--- New hand. Dealer: {Players[Dealer].Name} ---");
        PostBlind(Players[sb], SmallBlind, "small");
        PostBlind(Players[bb], BigBlind, "big");
        // FIX 2: a short-stacked big blind must not lower the amount everyone else has to call
        CurrentBet = Math.Max(BigBlind, Players.Max(p => p.Bet));
        MinRaise = BigBlind;

        // two rounds, one card at a time, starting left of the button (the button gets the last card)
        _dealer.DealHoleCards(Dealer, Players.Count, seat => !Players[seat].IsOut,
                              (seat, card) => Players[seat].Hole.Add(card));

        Current = bb;
        Advance();
    }

    // FIX 4 helpers: raising is only legal if the player has chips beyond a call,
    // is allowed to raise, and at least one opponent can still respond.
    private bool OthersCanRespond(Player p) =>
        Players.Any(o => o != p && !o.Folded && !o.AllIn && !o.IsOut);

    private bool CanRaiseNow(Player p) =>
        p.Chips > CurrentBet - p.Bet && p.CanRaise && OthersCanRespond(p);

    // FIX 3: strict API (use this for anything that isn't a trusted local UI).
    // Rejects instead of silently turning an illegal action into a call, and checks it is that seat's turn.
    public ActResult TryAct(int seat, PlayerAction action, int raiseTo = 0)
    {
        if (HandOver || Current < 0) return ActResult.NoActiveHand;
        if (seat != Current) return ActResult.NotYourTurn;
        if (!Enum.IsDefined(typeof(PlayerAction), action)) return ActResult.InvalidAction;

        if (action == PlayerAction.Raise)
        {
            var p = Players[Current];
            if (!CanRaiseNow(p)) return ActResult.IllegalRaise;
            int maxTo = p.Bet + p.Chips;
            int minTo = Math.Min(MinRaiseTo, maxTo);
            if (raiseTo < minTo || raiseTo > maxTo) return ActResult.IllegalRaise;
        }

        Act(action, raiseTo);
        return ActResult.Ok;
    }

    // Legacy/lenient API (unchanged signature): clamps the raise amount and turns an illegal raise into a call.
    public void Act(PlayerAction action, int raiseTo = 0)
    {
        if (HandOver || Current < 0) return;
        var p = Players[Current];
        int toCall = CurrentBet - p.Bet;

        switch (action)
        {
            case PlayerAction.Fold:
                p.Folded = true;
                Log($"{p.Name} folds");
                break;

            case PlayerAction.Raise when CanRaiseNow(p): // FIX 4
                int maxTo = p.Bet + p.Chips;
                int minTo = Math.Min(MinRaiseTo, maxTo);
                int to = Math.Clamp(raiseTo, minTo, maxTo);
                bool wasBet = CurrentBet == 0;
                Pay(p, to - p.Bet);
                int size = to - CurrentBet;
                bool fullRaise = size >= MinRaise; // an all-in smaller than a full raise is not a "full" raise
                if (fullRaise) MinRaise = size;
                CurrentBet = to;
                foreach (var other in Players.Where(o => o != p && !o.Folded && !o.AllIn && !o.IsOut))
                {
                    // players who already acted may only call or fold after a short all-in
                    other.CanRaise = fullRaise || (other.CanRaise && !other.HasActed);
                    other.HasActed = false; // everyone must respond to the raise
                }
                if (fullRaise && Street == Street.Preflop) PreflopAggressor = p;
                Log($"{p.Name} {(wasBet ? "bets" : "raises to")} {to}{(p.AllIn ? " (all-in)" : "")}");
                break;

            default: // check / call
                if (toCall <= 0) Log($"{p.Name} checks");
                else
                {
                    int chipsBefore = p.Chips;
                    Pay(p, toCall);
                    Log($"{p.Name} calls {chipsBefore - p.Chips}{(p.AllIn ? " (all-in)" : "")}");
                }
                break;
        }
        p.HasActed = true;
        Advance();
    }

    private void Advance()
    {
        while (true)
        {
            if (Players.Count(p => !p.Folded) == 1) { AwardUncontested(); return; }
            if (!RoundComplete())
            {
                Current = NextSeat(Current, p => !p.Folded && !p.AllIn);
                return;
            }
            if (Street == Street.River) { Showdown(); return; }
            NextStreet();
        }
    }

    private bool RoundComplete()
    {
        var able = Players.Where(p => !p.Folded && !p.AllIn).ToList();
        if (able.Count == 0) return true;
        if (able.Count == 1) return able[0].Bet >= CurrentBet;
        return able.All(p => p.HasActed && p.Bet == CurrentBet);
    }

    private void NextStreet()
    {
        Street++;
        Community.AddRange(_dealer.DealBoard(Street)); // burns one card, then deals 3 / 1 / 1
        Log($"*** {Street}: {string.Join(" ", Community)} ***");
        foreach (var p in Players) { p.Bet = 0; p.HasActed = false; p.CanRaise = true; }
        CurrentBet = 0;
        MinRaise = BigBlind;
        Current = Dealer; // first to act = next active seat after dealer (found in Advance)
    }

    // ---------------------------------------------------------------- end of hand
    private void AwardUncontested()
    {
        var winner = Players.First(p => !p.Folded);
        int pot = Pot;
        winner.Chips += pot;
        Log($"{winner.Name} wins {pot} (everyone else folded)");
        EndHand();
    }

    private void Showdown()
    {
        ShowdownReached = true;
        Street = Street.Showdown;
        var alive = Players.Where(p => !p.Folded).ToList();
        var scores = alive.ToDictionary(p => p, p => HandEvaluator.Evaluate(p.Hole.Concat(Community).ToList()));
        foreach (var p in alive)
            Log($"{p.Name} shows {string.Join(" ", p.Hole)} - {HandEvaluator.Names[HandEvaluator.Category(scores[p])]}");

        // main pot + side pots, one layer per distinct all-in level
        var levels = Players.Where(p => p.Contrib > 0).Select(p => p.Contrib).Distinct().OrderBy(x => x).ToList();
        int prev = 0;
        foreach (int level in levels)
        {
            int lo = prev;
            int layer = Players.Sum(p => Math.Min(p.Contrib, level) - Math.Min(p.Contrib, lo));
            var eligible = alive.Where(p => p.Contrib >= level).ToList();
            if (eligible.Count == 0) eligible = alive;
            long best = eligible.Max(p => scores[p]);
            // seat distance from the button: 0 = first seat left of the button ... n-1 = the button itself
            var winners = eligible.Where(p => scores[p] == best)
                                  .OrderBy(p => (Players.IndexOf(p) - Dealer - 1 + Players.Count) % Players.Count)
                                  .ToList();
            int share = layer / winners.Count, rest = layer % winners.Count;
            for (int i = 0; i < winners.Count; i++)
                winners[i].Chips += share + (i < rest ? 1 : 0); // odd chips: one each, starting left of the button

            string names = string.Join(" & ", winners.Select(w => w.Name));
            string hand = HandEvaluator.Names[HandEvaluator.Category(best)];
            Log(levels.Count > 1 && level != levels[0]
                ? $"Side pot {layer}: {names} win with {hand}"
                : $"{names} win{(winners.Count == 1 ? "s" : "")} {layer} with {hand}");
            prev = level;
        }
        EndHand();
    }

    private void EndHand()
    {
        HandOver = true;
        Street = Street.Showdown;
        Current = -1;
        _dealer.VerifyIntegrity(); // 52 unique cards accounted for
        if (IsGameOver)
            Log(HumansAllOut ? "*** You are out of chips. Game over! ***" : "*** You won the game! ***");
    }

    // ---------------------------------------------------------------- power-up card actions
    // The only way a power-up may change or look at cards. They keep the deck bookkeeping and the DealEvents correct.
    // They throw if called when no hand is running or the seat is not in the hand.

    private void RequireInHand(int seat)
    {
        if (HandOver) throw new InvalidOperationException("No hand is in progress.");
        if (seat < 0 || seat >= Players.Count) throw new ArgumentOutOfRangeException(nameof(seat));
        if (Players[seat].Folded || Players[seat].IsOut) throw new InvalidOperationException(Players[seat].Name + " is not in the hand.");
    }

    /// <summary>Swaps one of a player's hole cards for the top card of the deck (or for 'wanted'). Returns the new card.</summary>
    public Card ReplaceHoleCard(int seat, int holeIndex, Card? wanted = null)
    {
        RequireInHand(seat);
        var p = Players[seat];
        if (holeIndex < 0 || holeIndex >= p.Hole.Count) throw new ArgumentOutOfRangeException(nameof(holeIndex));
        Card fresh = _dealer.Replace(seat, p.Hole[holeIndex], wanted, NextInt);
        p.Hole[holeIndex] = fresh;
        Log(p.Name + " swaps a hole card");
        return fresh;
    }

    /// <summary>Replaces one community card with the top card of the deck (or 'wanted'). Returns the new card.</summary>
    public Card ReplaceBoardCard(int boardIndex, Card? wanted = null)
    {
        if (HandOver) throw new InvalidOperationException("No hand is in progress.");
        if (boardIndex < 0 || boardIndex >= Community.Count) throw new ArgumentOutOfRangeException(nameof(boardIndex));
        Card fresh = _dealer.Replace(-1, Community[boardIndex], wanted, NextInt);
        Community[boardIndex] = fresh;
        Log("A community card is replaced");
        return fresh;
    }

    /// <summary>The viewer secretly looks at a card in the deck (depthFromTop 0 = the next card to be dealt).</summary>
    public Card PeekDeck(int viewerSeat, int depthFromTop = 0)
    {
        RequireInHand(viewerSeat);
        return _dealer.PeekDeck(viewerSeat, depthFromTop);
    }

    /// <summary>Shows a player's hole card to viewerSeat (-1 = everybody). Returns the card.</summary>
    public Card RevealHoleCard(int ownerSeat, int holeIndex, int viewerSeat = -1)
    {
        RequireInHand(ownerSeat);
        if (viewerSeat >= Players.Count) throw new ArgumentOutOfRangeException(nameof(viewerSeat));
        var p = Players[ownerSeat];
        if (holeIndex < 0 || holeIndex >= p.Hole.Count) throw new ArgumentOutOfRangeException(nameof(holeIndex));
        Card c = p.Hole[holeIndex];
        _dealer.Reveal(ownerSeat, c, viewerSeat);
        return c;
    }

    // ---------------------------------------------------------------- helpers
    public string HandDescription(Player p)
    {
        if (Community.Count < 3 || p.Hole.Count < 2) return "";
        return HandEvaluator.Names[HandEvaluator.Category(HandEvaluator.Evaluate(p.Hole.Concat(Community).ToList()))];
    }

    private void PostBlind(Player p, int amount, string name)
    {
        Pay(p, amount);
        Log($"{p.Name} posts the {name} blind ({p.Bet})");
    }

    private static void Pay(Player p, int amount)
    {
        amount = Math.Min(amount, p.Chips);
        p.Chips -= amount; p.Bet += amount; p.Contrib += amount;
        if (p.Chips == 0) p.AllIn = true;
    }

    private int NextSeat(int from, Func<Player, bool> pred)
    {
        int n = Players.Count;
        for (int k = 1; k <= n; k++)
        {
            int i = ((from + k) % n + n) % n;
            if (pred(Players[i])) return i;
        }
        return -1;
    }
}
}