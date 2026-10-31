using System;
using System.Collections.Generic;
using System.Linq;
using Holdem;

namespace Holdem.PowerUps
{
    /// <summary>
    /// A situation to deal: chosen hole cards per seat and/or chosen board cards. Anything left empty is dealt at random.
    /// </summary>
    public sealed class Scenario
    {
        /// <summary>Hole cards by seat (0, 1 or 2 cards; missing ones are random). A seat that is missing or null is fully random.</summary>
        public Dictionary<int, List<Card>> Hole = new Dictionary<int, List<Card>>();
        /// <summary>Board cards in order: flop 1-3, turn, river (Cross: 5 / 2 / 2, up to 9). Missing ones are random.</summary>
        public List<Card> Board = new List<Card>();
    }

    public static class CardParser
    {
        /// <summary>"As" = ace of spades, "Td" = ten of diamonds, "7h" = seven of hearts.</summary>
        public static bool TryParse(string s, out Card card)
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

        /// <summary>"As Kd" or "AsKd" or "As,Kd". Blank text gives an empty list (= random). Returns an error text or null.</summary>
        public static string TryParseList(string text, out List<Card> cards)
        {
            cards = new List<Card>();
            if (string.IsNullOrWhiteSpace(text)) return null;
            string t = new string(text.Where(ch => !char.IsWhiteSpace(ch) && ch != ',').ToArray());
            if (t.Length % 2 != 0) return "\"" + text.Trim() + "\" is not a list of cards (use like As Kd 7h).";
            for (int i = 0; i < t.Length; i += 2)
            {
                if (!TryParse(t.Substring(i, 2), out Card c)) return "\"" + t.Substring(i, 2) + "\" is not a card (ranks 2-9 T J Q K A, suits c d h s).";
                cards.Add(c);
            }
            return null;
        }
    }

    /// <summary>Builds a rigged deck for the NEXT hand so a test starts from an exact situation. Plain C#.</summary>
    public static class ScenarioBuilder
    {
        /// <summary>Who will be the dealer in the next hand (the engine moves the button to the next seat that still has chips).</summary>
        public static int NextDealer(GameEngine e)
        {
            int n = e.Players.Count;
            for (int k = 1; k <= n; k++)
            {
                int i = (((e.Dealer + k) % n) + n) % n;
                if (e.Players[i].Chips > 0) return i;
            }
            return -1;
        }

        /// <summary>Returns an error text, or null if the scenario is fine. On success the engine's NextDeckOverride is set.</summary>
        public static string Apply(GameEngine e, Scenario s, Func<int, int> nextInt = null)
        {
            try
            {
                e.NextDeckOverride = BuildDeck(e, s, nextInt ?? e.NextInt);
                return null;
            }
            catch (ArgumentException ex) { return ex.Message; }
        }

        public static Deck BuildDeck(GameEngine e, Scenario s, Func<int, int> nextInt)
        {
            int n = e.Players.Count;
            var used = new HashSet<Card>();
            foreach (var list in s.Hole.Values.Concat(new[] { s.Board }))
                foreach (Card c in list)
                    if (!used.Add(c)) throw new ArgumentException("The card " + c + " is used twice.");
            foreach (var kv in s.Hole)
                if (kv.Value.Count > 2) throw new ArgumentException("A player can have at most 2 hole cards.");
            bool cross = e.NextRules.Cross;                 // the Cross has 9 board cards, dealt 5 / 2 / 2
            int maxBoard = cross ? 9 : 5;
            if (s.Board.Count > maxBoard) throw new ArgumentException("The board can have at most " + maxBoard + " cards.");

            // everything not chosen, in random order (fills the gaps)
            var pool = new List<Card>();
            foreach (Suit su in Enum.GetValues(typeof(Suit)))
                foreach (Rank r in Enum.GetValues(typeof(Rank)))
                {
                    var c = new Card(r, su);
                    if (!used.Contains(c)) pool.Add(c);
                }
            for (int i = pool.Count - 1; i > 0; i--) { int j = nextInt(i + 1); var t = pool[i]; pool[i] = pool[j]; pool[j] = t; }
            int next = 0;
            Card Fill() => pool[next++];

            // draw order, top of the deck first: exactly how CardDealer deals
            var draws = new List<Card>(52);
            int dealer = NextDealer(e);
            for (int round = 0; round < 2; round++)
                for (int k = 1; k <= n; k++)
                {
                    int seat = (((dealer + k) % n) + n) % n;
                    if (e.Players[seat].Chips <= 0) continue;
                    bool has = s.Hole.TryGetValue(seat, out var h) && round < h.Count;
                    draws.Add(has ? h[round] : Fill());
                }
            int b = 0;
            Card BoardCard() { if (b < s.Board.Count) return s.Board[b++]; b++; return Fill(); }
            foreach (int count in cross ? new[] { 5, 2, 2 } : new[] { 3, 1, 1 }) // flop, turn, river
            {
                draws.Add(Fill());                               // burn
                for (int i = 0; i < count; i++) draws.Add(BoardCard());
            }
            while (draws.Count < 52) draws.Add(Fill());

            draws.Reverse(); // Deck wants bottom to top
            return new Deck(draws);
        }
    }
}
