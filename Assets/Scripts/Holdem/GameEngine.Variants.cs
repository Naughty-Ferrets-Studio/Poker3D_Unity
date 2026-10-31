using System;
using System.Collections.Generic;
using System.Linq;

namespace Holdem
{
/// <summary>Rule changes for ONE hand. Power-ups set these before the hand starts; the engine does not know which power-up did it.</summary>
public sealed class HandRules
{
    public bool BombPot;     // everyone antes, no blinds, no preflop betting
    public bool Joker;       // a wild joker is shuffled into the deck
    public bool WildTwos;    // all four 2s are wild
    public bool Cross;       // nine community cards in a cross; use the horizontal OR the vertical line
    public readonly HashSet<int> ShanKoeMee = new HashSet<int>(); // seats that announced it (they cannot fold)

    public bool AnyWild => Joker || WildTwos;
    public bool IsWild(Card c) => (Joker && c.IsJoker) || (WildTwos && !c.IsJoker && c.Rank == Rank.Two);
    public int BoardSize => Cross ? 9 : 5;
}

/// <summary>A fake card sticker on a hole card. Sticker is what the card counts as; Real is what is really under it.</summary>
public readonly struct DisguiseInfo
{
    public readonly Card Real, Sticker;
    public DisguiseInfo(Card real, Card sticker) { Real = real; Sticker = sticker; }
}

public partial class GameEngine
{
    /// <summary>One "ante" (used by Bomb Pot, Shan Koe Mee, bounties and penalties). Equal to the small blind.</summary>
    public const int Ante = 10;

    /// <summary>The rules of the hand being played.</summary>
    public HandRules Rules { get; private set; } = new HandRules();
    /// <summary>Rules for the NEXT hand. Power-ups used between hands change this. It is copied into Rules by StartHand().</summary>
    public HandRules NextRules { get; private set; } = new HandRules();
    /// <summary>Counts up by one every time a hand starts.</summary>
    public int HandNumber { get; private set; }

    // ---------------------------------------------------------------- run it twice
    /// <summary>When true, the engine stops before dealing the rest of the board once nobody can bet any more, so a power-up can be used. Then call ContinueRunout().</summary>
    public bool PauseBeforeRunout { get; set; }
    public bool RunoutPending { get; private set; }
    /// <summary>How many times the rest of the board is dealt (1 = normal, 2 = run it twice, 3 = thrice).</summary>
    public int RunCount { get; private set; } = 1;
    private bool _runoutDecided;
    private int _runoutStart;
    private static readonly int[] StdBounds = { 3, 4, 5 };
    private static readonly int[] CrossBounds = { 5, 7, 9 };

    // ---------------------------------------------------------------- pineapple
    public bool AwaitingDiscard { get; private set; }
    private readonly Dictionary<int, Street> _pendingDiscard = new Dictionary<int, Street>();
    /// <summary>Seat -> the betting round after which they must have discarded their third card.</summary>
    public IReadOnlyDictionary<int, Street> PendingDiscards => _pendingDiscard;

    // ---------------------------------------------------------------- bumba card and disguise state
    private readonly Dictionary<int, Card> _held = new Dictionary<int, Card>();
    private readonly Dictionary<int, DisguiseInfo> _disguise = new Dictionary<int, DisguiseInfo>();

    /// <summary>Writes a line to the game log (the dealer announcing something).</summary>
    public void Announce(string line) => Log(line);

    private void BeginVariants()
    {
        Rules = NextRules;
        NextRules = new HandRules();
        HandNumber++;
        RunoutPending = false; _runoutDecided = false; _runoutStart = 0; RunCount = 1;
        AwaitingDiscard = false;
        _pendingDiscard.Clear(); _held.Clear(); _disguise.Clear();
        Rules.ShanKoeMee.RemoveWhere(s => s < 0 || s >= Players.Count || Players[s].IsOut);
        if (Rules.Joker) _dealer.AddJoker(NextInt);
    }

    // ---------------------------------------------------------------- evaluation that knows the variants
    /// <summary>The lines a player may build a hand from: just the board, or (Cross) the horizontal and the vertical line.</summary>
    public IReadOnlyList<IReadOnlyList<Card>> BoardLines(IReadOnlyList<Card> board)
    {
        if (!Rules.Cross) return new[] { board };
        var horizontal = board.Take(5).ToList();
        var vertical = new List<Card>();
        if (board.Count > 5) vertical.AddRange(board.Skip(5).Take(2)); // top two
        if (board.Count > 2) vertical.Add(board[2]);                   // the shared centre card
        if (board.Count > 7) vertical.AddRange(board.Skip(7).Take(2)); // bottom two
        return new IReadOnlyList<Card>[] { horizontal, vertical };
    }

    /// <summary>Score of a player's best hand on this board (higher = better), or -1 if there are not enough cards yet.</summary>
    public long ScoreFor(Player p, IReadOnlyList<Card> board) => ScoreFor(p.Hole, board);

    public long ScoreFor(IReadOnlyList<Card> hole, IReadOnlyList<Card> board)
    {
        long best = -1;
        foreach (var line in BoardLines(board))
        {
            var cards = new List<Card>(hole);
            cards.AddRange(line);
            if (cards.Count < 5) continue;
            long s = Rules.AnyWild ? WildEvaluator.Evaluate(cards, Rules.IsWild) : HandEvaluator.Evaluate(cards);
            if (s > best) best = s;
        }
        return best;
    }

    /// <summary>"Two Pair", "Flush"... for the player's hand on the current board ("" before the flop).</summary>
    public string HandName(Player p) => HandName(p, Community);

    public string HandName(Player p, IReadOnlyList<Card> board) => HandName(p.Hole, board);

    public string HandName(IReadOnlyList<Card> hole, IReadOnlyList<Card> board)
    {
        if (board.Count < 3 || hole.Count < 2) return "";
        long s = ScoreFor(hole, board);
        return s < 0 ? "" : HandEvaluator.Names[HandEvaluator.Category(s)];
    }

    // ---------------------------------------------------------------- chips between players
    /// <summary>Moves chips from one player's stack to another's (a bounty, a penalty...). Pays only what the payer has. Returns the amount moved.</summary>
    public int TransferChips(int fromSeat, int toSeat, int amount)
    {
        if (fromSeat < 0 || fromSeat >= Players.Count || toSeat < 0 || toSeat >= Players.Count) throw new ArgumentOutOfRangeException();
        var from = Players[fromSeat]; var to = Players[toSeat];
        int n = Math.Max(0, Math.Min(amount, from.Chips));
        from.Chips -= n; to.Chips += n;
        return n;
    }

    // ---------------------------------------------------------------- run it twice / thrice
    /// <summary>An all-in player asks for the rest of the board to be dealt 'runs' times. Only while RunoutPending.</summary>
    public bool RequestRuns(int seat, int runs)
    {
        if (!RunoutPending || seat < 0 || seat >= Players.Count) return false;
        var p = Players[seat];
        if (p.Folded || p.IsOut || !p.AllIn) return false;
        runs = Math.Max(1, Math.Min(runs, 3));
        if (runs <= RunCount) return false;
        RunCount = runs;
        Log(p.Name + " calls for the board to be run " + (runs == 2 ? "twice" : "three times") + "!");
        return true;
    }

    /// <summary>Carry on dealing after the pause (see PauseBeforeRunout).</summary>
    public void ContinueRunout()
    {
        if (!RunoutPending) return;
        RunoutPending = false;
        _runoutDecided = true;
        Advance();
    }

    private bool NoMoreBetting() => Players.Count(p => !p.Folded && !p.IsOut && !p.AllIn) <= 1;

    // ---------------------------------------------------------------- pineapple
    /// <summary>Draws a third hole card for the seat. They must discard one by the end of the 'discardAfter' betting round.</summary>
    public Card StartPineapple(int seat, Street discardAfter)
    {
        RequireInHand(seat);
        if (Street != Street.Preflop || Players[seat].Hole.Count != 2) throw new InvalidOperationException("Pineapple needs two hole cards, before the flop.");
        if (_pendingDiscard.ContainsKey(seat)) throw new InvalidOperationException("Already has a third card.");
        Card c = _dealer.DealExtraHole(seat);
        Players[seat].Hole.Add(c);
        _pendingDiscard[seat] = discardAfter;
        Log(Players[seat].Name + " takes a third card");
        return c;
    }

    /// <summary>The seat throws one hole card face-down into the discard pile (it ends with two cards).</summary>
    public void DiscardHole(int seat, int holeIndex)
    {
        if (!_pendingDiscard.ContainsKey(seat)) throw new InvalidOperationException("Nothing to discard.");
        var p = Players[seat];
        if (holeIndex < 0 || holeIndex >= p.Hole.Count) throw new ArgumentOutOfRangeException(nameof(holeIndex));

        Card shown = p.Hole[holeIndex];
        Card physical = shown;
        if (_disguise.TryGetValue(seat, out var d) && d.Sticker == shown) { physical = d.Real; _disguise.Remove(seat); } // the sticker goes with the card
        p.Hole.RemoveAt(holeIndex);
        _dealer.Discard(seat, physical);
        _pendingDiscard.Remove(seat);
        Log(p.Name + " discards a card face down");

        if (AwaitingDiscard && !DiscardsDueNow().Any())
        {
            AwaitingDiscard = false;
            Advance();
        }
    }

    /// <summary>For bots (and auto-play): throws away the lowest card. Wild cards are kept.</summary>
    public void AutoDiscard(int seat)
    {
        var p = Players[seat];
        int worst = 0, worstValue = int.MaxValue;
        for (int i = 0; i < p.Hole.Count; i++)
        {
            int v = Rules.IsWild(p.Hole[i]) ? 100 : (int)p.Hole[i].Rank;
            if (v < worstValue) { worstValue = v; worst = i; }
        }
        DiscardHole(seat, worst);
    }

    private IEnumerable<int> DiscardsDueNow() =>
        _pendingDiscard.Where(kv => kv.Value == Street && !Players[kv.Key].Folded).Select(kv => kv.Key).ToList();

    private bool DiscardsDue()
    {
        foreach (int seat in DiscardsDueNow().ToList())
            if (!Players[seat].IsHuman) AutoDiscard(seat);          // bots choose by themselves
        AwaitingDiscard = DiscardsDueNow().Any();
        if (AwaitingDiscard) Current = -1;
        return AwaitingDiscard;
    }

    private void ResolvePendingDiscards()
    {
        AwaitingDiscard = false;
        foreach (int seat in _pendingDiscard.Keys.ToList()) if (!Players[seat].Folded) AutoDiscard(seat);
        _pendingDiscard.Clear();
    }

    // ---------------------------------------------------------------- bumba card: sit on a stolen card
    public bool HasHeldCard(int seat) => _held.ContainsKey(seat);
    public Card? HeldCard(int seat) => _held.TryGetValue(seat, out var c) ? c : (Card?)null;

    /// <summary>Secretly takes a card out of the deck (the top one, or 'wanted') and keeps it. The dealer only announces that a card is missing.</summary>
    public Card StealFromDeck(int seat, Card? wanted = null)
    {
        RequireInHand(seat);
        if (_held.ContainsKey(seat)) throw new InvalidOperationException("Already sitting on a card.");
        Card c = _dealer.Steal(wanted);
        _held[seat] = c;
        Log("A card is missing from the deck!");
        return c;
    }

    /// <summary>Sneaks the held card into the hand in place of a hole card. The old card goes face-down into the discard pile. Returns the old card.</summary>
    public Card SwapHeldIntoHole(int seat, int holeIndex)
    {
        RequireInHand(seat);
        if (!_held.TryGetValue(seat, out Card held)) throw new InvalidOperationException("Not holding a card.");
        var p = Players[seat];
        if (holeIndex < 0 || holeIndex >= p.Hole.Count) throw new ArgumentOutOfRangeException(nameof(holeIndex));
        PeelSticker(seat, holeIndex);
        Card old = p.Hole[holeIndex];
        p.Hole[holeIndex] = held;
        _held.Remove(seat);
        _dealer.SwapIntoPlace(seat, old, held);
        Log(p.Name + " sneaks a card into their hand");
        return old;
    }

    /// <summary>The held card goes back into the deck (the thief was caught). Returns it.</summary>
    public Card ReturnHeldToDeck(int seat)
    {
        if (!_held.TryGetValue(seat, out Card c)) throw new InvalidOperationException("Not holding a card.");
        _held.Remove(seat);
        _dealer.ReturnToDeck(c, NextInt);
        return c;
    }

    // ---------------------------------------------------------------- disguised card: a sticker over a hole card
    public DisguiseInfo? GetDisguise(int seat) => _disguise.TryGetValue(seat, out var d) ? d : (DisguiseInfo?)null;

    /// <summary>Sticks a fake card over a hole card. It counts as 'sticker' until the disguise is caught.</summary>
    public void ApplySticker(int seat, int holeIndex, Card sticker)
    {
        RequireInHand(seat);
        var p = Players[seat];
        if (holeIndex < 0 || holeIndex >= p.Hole.Count) throw new ArgumentOutOfRangeException(nameof(holeIndex));
        if (_disguise.ContainsKey(seat)) throw new InvalidOperationException("Already wearing a sticker.");
        if (p.Hole.Contains(sticker) || Community.Contains(sticker)) throw new InvalidOperationException("The sticker can't be a card you can see in your own hand or on the board.");
        Card real = p.Hole[holeIndex];
        p.Hole[holeIndex] = sticker;
        _disguise[seat] = new DisguiseInfo(real, sticker);
        _dealer.FaceChange(seat, real, sticker);
        Log(p.Name + " fiddles with a card");
    }

    /// <summary>If the hole card at this index wears a sticker, take it off (the real card shows again).</summary>
    private bool PeelSticker(int seat, int holeIndex)
    {
        if (!_disguise.TryGetValue(seat, out var d)) return false;
        var p = Players[seat];
        if (holeIndex < 0 || holeIndex >= p.Hole.Count || p.Hole[holeIndex] != d.Sticker) return false;
        p.Hole[holeIndex] = d.Real;
        _disguise.Remove(seat);
        _dealer.FaceChange(seat, d.Sticker, d.Real);
        return true;
    }

    /// <summary>The disguise was caught: the sticker comes off and the real card is shown to everybody. Returns the real card.</summary>
    public Card ExposeDisguise(int seat)
    {
        if (!_disguise.TryGetValue(seat, out var d)) throw new InvalidOperationException("No disguise.");
        int idx = Players[seat].Hole.IndexOf(d.Sticker);
        PeelSticker(seat, idx);
        _dealer.RevealUnchecked(seat, d.Real, -1);
        Log(Players[seat].Name + "'s card was a fake! It is really the " + d.Real);
        return d.Real;
    }

    // ---------------------------------------------------------------- snatch the sock
    /// <summary>Two players swap one hole card each.</summary>
    public void SwapHoleCards(int seatA, int indexA, int seatB, int indexB)
    {
        RequireInHand(seatA); RequireInHand(seatB);
        if (seatA == seatB) throw new ArgumentException("Pick two different players.");
        var a = Players[seatA]; var b = Players[seatB];
        if (indexA < 0 || indexA >= a.Hole.Count) throw new ArgumentOutOfRangeException(nameof(indexA));
        if (indexB < 0 || indexB >= b.Hole.Count) throw new ArgumentOutOfRangeException(nameof(indexB));
        PeelSticker(seatA, indexA); PeelSticker(seatB, indexB);
        Card ca = a.Hole[indexA], cb = b.Hole[indexB];
        a.Hole[indexA] = cb; b.Hole[indexB] = ca;
        _dealer.FaceChange(seatA, ca, cb);
        _dealer.FaceChange(seatB, cb, ca);
        Log(a.Name + " snatches a card from " + b.Name);
    }

    // ---------------------------------------------------------------- shan koe mee
    private static int SkmValue(Card c) => c.IsJoker ? 0 : c.Rank == Rank.Ace ? 1 : (int)c.Rank <= 9 ? (int)c.Rank : 0;

    /// <summary>Called after the pots are paid. A total of 8 collects 1 ante from every opponent, 9 collects 2.
    /// If everybody else folded, the player must still show: a miss then costs 1 ante to each opponent.</summary>
    private void SettleShanKoeMee(bool everyoneElseFolded)
    {
        foreach (int seat in Rules.ShanKoeMee.OrderBy(s => s))
        {
            var p = Players[seat];
            if (p.IsOut || p.Hole.Count < 2) continue;
            int total = (SkmValue(p.Hole[0]) + SkmValue(p.Hole[1])) % 10;
            var opponents = Players.Where(o => o != p && !o.IsOut).ToList();

            if (everyoneElseFolded)
                for (int i = 0; i < p.Hole.Count; i++) RevealInternal(seat, i, -1);

            if (total >= 8)
            {
                int each = (total - 7) * Ante, got = 0;
                foreach (var o in opponents) got += TransferChips(Players.IndexOf(o), seat, each);
                Log("Shan Koe Mee! " + p.Name + " has a " + total + " and collects " + got);
            }
            else if (everyoneElseFolded)
            {
                int paid = 0;
                foreach (var o in opponents) paid += TransferChips(seat, Players.IndexOf(o), Ante);
                Log("Shan Koe Mee missed (" + total + "): " + p.Name + " pays " + paid);
            }
            else Log("Shan Koe Mee missed (" + total + "): no reward for " + p.Name);
        }
    }

    // shows a hole card; a sticker is not on the table, so it is not checked against the table
    private void RevealInternal(int ownerSeat, int holeIndex, int viewerSeat)
    {
        Card c = Players[ownerSeat].Hole[holeIndex];
        if (_disguise.TryGetValue(ownerSeat, out var d) && d.Sticker == c) _dealer.RevealUnchecked(ownerSeat, c, viewerSeat);
        else _dealer.Reveal(ownerSeat, c, viewerSeat);
    }
}
}
