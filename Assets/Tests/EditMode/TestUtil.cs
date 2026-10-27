using System;
using System.Collections.Generic;
using System.Linq;

namespace Holdem.Tests
{
    /// <summary>Small helpers shared by the tests: parse cards from text, build exact decks, seeded random numbers.</summary>
    public static class TestUtil
    {
        /// <summary>"As" = ace of spades, "Td" = ten of diamonds, "7h" = seven of hearts. Suits: c d h s.</summary>
        public static Card C(string s)
        {
            if (s == null || s.Length != 2)
                throw new ArgumentException("A card must be 2 characters like \"As\" or \"Td\", got: " + s);

            Rank r;
            switch (char.ToUpperInvariant(s[0]))
            {
                case 'A': r = Rank.Ace; break;
                case 'K': r = Rank.King; break;
                case 'Q': r = Rank.Queen; break;
                case 'J': r = Rank.Jack; break;
                case 'T': r = Rank.Ten; break;
                default:
                    if (s[0] < '2' || s[0] > '9') throw new ArgumentException("Unknown rank in card: " + s);
                    r = (Rank)(s[0] - '0');
                    break;
            }

            Suit su;
            switch (char.ToLowerInvariant(s[1]))
            {
                case 'c': su = Suit.Clubs; break;
                case 'd': su = Suit.Diamonds; break;
                case 'h': su = Suit.Hearts; break;
                case 's': su = Suit.Spades; break;
                default: throw new ArgumentException("Unknown suit in card: " + s);
            }
            return new Card(r, su);
        }

        /// <summary>"As Kh 9d" -> three cards.</summary>
        public static List<Card> Cards(string text)
        {
            return text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => C(x)).ToList();
        }

        /// <summary>All 52 cards in a fixed order, bottom to top (same layout the Deck constructor expects).</summary>
        public static List<Card> OrderedDeck()
        {
            var list = new List<Card>(52);
            foreach (Suit s in Enum.GetValues(typeof(Suit)))
                foreach (Rank r in Enum.GetValues(typeof(Rank)))
                    list.Add(new Card(r, s));
            return list;
        }

        /// <summary>A deck whose first cards drawn are exactly 'topFirst', in that order. The rest follow in a fixed order.</summary>
        public static Deck DeckWithTop(params string[] topFirst)
        {
            List<Card> top = topFirst.Select(x => C(x)).ToList();
            List<Card> rest = OrderedDeck().Where(c => !top.Contains(c)).ToList();
            if (rest.Count != 52 - top.Count)
                throw new ArgumentException("DeckWithTop was given a duplicate card.");
            top.Reverse();      // the top of the deck is the END of the list
            rest.AddRange(top);
            return new Deck(rest);
        }

        /// <summary>A repeatable RNG function for Deck / GameEngine.NextInt: nextInt(n) returns [0, n).</summary>
        public static Func<int, int> Seeded(int seed)
        {
            var r = new Random(seed);
            return n => r.Next(n);
        }
    }
}
