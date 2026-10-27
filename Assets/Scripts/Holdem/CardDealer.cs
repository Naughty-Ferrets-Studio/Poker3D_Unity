using System;
using System.Collections.Generic;
using System.Linq;

namespace Holdem
{

/// <summary>
/// Burn / Hole / Flop / Turn / River = a card leaves the deck.
/// Replace = a card on the table went back into the deck and a new one took its place (power-ups).
/// Peek    = someone looked at a card in the deck (Viewer says who).
/// Reveal  = a card already on the table (Seat = its owner) was shown to Viewer (-1 = everybody).
/// </summary>
public enum DealKind { Burn, Hole, Flop, Turn, River, Replace, Peek, Reveal }

/// <summary>
/// One physical dealing action, in order. A 3D layer plays these back as animations.
/// Seat is -1 for burn, board cards and the deck.
/// </summary>
public readonly struct DealEvent
{
    public readonly DealKind Kind;
    public readonly int Seat;        // where the card is: a seat, or -1 for the board / the deck
    public readonly Card Card;       // the card involved (Replace: the NEW card)
    public readonly Card? Previous;  // Replace only: the card that went back into the deck
    public readonly int Viewer;      // Peek / Reveal only: who can see it (-1 = everybody)

    public DealEvent(DealKind kind, int seat, Card card) : this(kind, seat, card, null, -1) { }

    public DealEvent(DealKind kind, int seat, Card card, Card? previous, int viewer)
    {
        Kind = kind; Seat = seat; Card = card; Previous = previous; Viewer = viewer;
    }

    public override string ToString()
    {
        switch (Kind)
        {
            case DealKind.Replace: return $"Replace {(Seat >= 0 ? "seat " + Seat : "board")}: {Previous} -> {Card}";
            case DealKind.Peek:    return $"Peek (viewer {Viewer}): {Card}";
            case DealKind.Reveal:  return $"Reveal seat {Seat} to {(Viewer < 0 ? "everybody" : "seat " + Viewer)}: {Card}";
            default:               return Seat >= 0 ? $"{Kind} -> seat {Seat}: {Card}" : $"{Kind}: {Card}";
        }
    }
}

/// <summary>
/// Owns the deck for one hand and does ONLY dealing (no betting, no chips).
/// Every card that leaves the deck, or goes back into it, is recorded in Events, in order.
/// (Named CardDealer because GameEngine already has a "Dealer" seat property.)
/// Note: DeckOrder alone can no longer replay a hand in which power-ups changed cards;
/// a replay also needs the list of power-up actions.
/// </summary>
public sealed class CardDealer
{
    private Deck _deck;
    private readonly List<DealEvent> _events = new List<DealEvent>();
    private readonly List<Card> _burned = new List<Card>();
    private readonly HashSet<Card> _out = new HashSet<Card>(); // every card that is currently outside the deck

    public IReadOnlyList<DealEvent> Events => _events;
    public IReadOnlyList<Card> Burned => _burned;

    /// <summary>Save this per hand; feed it to ReplayHand(new Deck(order)) to reproduce the hand exactly (if no power-up changed a card).</summary>
    public IReadOnlyList<Card> DeckOrder => _deck?.InitialOrder;

    /// <summary>Fresh shuffled deck. nextInt = null uses the secure RNG.</summary>
    public void NewHand(Func<int, int> nextInt = null) => ReplayHand(new Deck(nextInt));

    /// <summary>Start a hand from a specific deck (saved order, or a test deck).</summary>
    public void ReplayHand(Deck deck)
    {
        _deck = deck ?? throw new ArgumentNullException(nameof(deck));
        _events.Clear();
        _burned.Clear();
        _out.Clear();
    }

    private Card DrawOut()
    {
        Card c = _deck.Draw();
        if (!_out.Add(c)) throw new InvalidOperationException($"Duplicate card dealt: {c}");
        return c;
    }

    /// <summary>
    /// Two rounds, one card per live seat, starting left of the button; the button gets the last card.
    /// give(seat, card) hands the card to your player object.
    /// </summary>
    public void DealHoleCards(int button, int seatCount, Func<int, bool> isLive, Action<int, Card> give)
    {
        for (int round = 0; round < 2; round++)
            for (int k = 1; k <= seatCount; k++)
            {
                int seat = ((button + k) % seatCount + seatCount) % seatCount;
                if (!isLive(seat)) continue;
                var card = DrawOut();
                give(seat, card);
                _events.Add(new DealEvent(DealKind.Hole, seat, card));
            }
    }

    /// <summary>Burn one card, then deal the board cards for that street (flop 3, turn 1, river 1).</summary>
    public IReadOnlyList<Card> DealBoard(Street street)
    {
        DealKind kind;
        int count;
        switch (street)
        {
            case Street.Flop:  kind = DealKind.Flop;  count = 3; break;
            case Street.Turn:  kind = DealKind.Turn;  count = 1; break;
            case Street.River: kind = DealKind.River; count = 1; break;
            default: throw new ArgumentException($"No board cards are dealt on {street}.");
        }

        var burn = DrawOut();
        _burned.Add(burn);
        _events.Add(new DealEvent(DealKind.Burn, -1, burn));

        var dealt = new List<Card>(count);
        for (int i = 0; i < count; i++)
        {
            var c = DrawOut();
            dealt.Add(c);
            _events.Add(new DealEvent(kind, -1, c));
        }
        return dealt;
    }

    // ---------------------------------------------------------------- power-up card actions
    /// <summary>Looks at a card in the deck without drawing it (0 = the top card) and records who looked.</summary>
    public Card PeekDeck(int viewerSeat, int depthFromTop = 0)
    {
        Card c = _deck.Peek(depthFromTop);
        _events.Add(new DealEvent(DealKind.Peek, -1, c, null, viewerSeat));
        return c;
    }

    /// <summary>Records that a card on the table (owned by ownerSeat, -1 = board) is shown to viewerSeat (-1 = everybody).</summary>
    public void Reveal(int ownerSeat, Card card, int viewerSeat = -1)
    {
        if (!_out.Contains(card)) throw new InvalidOperationException($"Card {card} is not on the table.");
        _events.Add(new DealEvent(DealKind.Reveal, ownerSeat, card, null, viewerSeat));
    }

    /// <summary>
    /// Takes 'old' off the table and shuffles it back into the deck at a random depth, then puts a different card in its place:
    /// the top card of the deck, or the specific card 'wanted' (which must still be in the deck).
    /// The new card is taken BEFORE the old one goes back, so you can never get the same card again.
    /// seat = the owner (-1 = a board card). The caller updates its own Hole / Community list with the returned card.
    /// </summary>
    public Card Replace(int seat, Card old, Card? wanted = null, Func<int, int> nextInt = null)
    {
        if (!_out.Contains(old)) throw new InvalidOperationException($"Card {old} is not on the table.");
        if (_burned.Contains(old)) throw new InvalidOperationException($"Card {old} was burned and cannot be replaced.");

        Card fresh = wanted.HasValue ? _deck.Take(wanted.Value) : _deck.Draw();
        _deck.InsertRandom(old, nextInt);
        _out.Remove(old);
        _out.Add(fresh);
        _events.Add(new DealEvent(DealKind.Replace, seat, fresh, old, -1));
        return fresh;
    }

    /// <summary>
    /// Conservation check: the cards outside the deck plus the cards still in the deck
    /// must be exactly the 52 unique cards (no card lost, none twice).
    /// </summary>
    public void VerifyIntegrity()
    {
        var seen = new HashSet<Card>(_out);
        foreach (var c in _deck.RemainingCards)
            if (!seen.Add(c))
                throw new InvalidOperationException($"Card is both on the table and in the deck: {c}");
        if (seen.Count != 52)
            throw new InvalidOperationException($"Expected 52 cards, found {seen.Count}.");
    }
}
}