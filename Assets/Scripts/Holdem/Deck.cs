using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Holdem
{
    /// <summary>
    /// Unbiased random integers. Uses the OS secure RNG; if a platform does not support it
    /// (some Unity targets), falls back to System.Random seeded from several entropy sources.
    /// Your own function (e.g. UnityEngine.Random.Range(0, max)) can be plugged in via GameEngine.NextInt.
    /// </summary>
    public static class SecureRng
    {
        private static bool _secureBroken;
        private static Random _fallback;

        /// <summary>Returns a uniformly distributed value in [0, max).</summary>
        public static int Next(int max)
        {
            if (max <= 0) throw new ArgumentOutOfRangeException(nameof(max));
            if (!_secureBroken)
            {
                try { return RandomNumberGenerator.GetInt32(max); }
                catch (Exception) { _secureBroken = true; } // PlatformNotSupported / missing API
            }
            if (_fallback == null)
                _fallback = new Random(unchecked(Environment.TickCount * 31 + Guid.NewGuid().GetHashCode()));
            return _fallback.Next(max);
        }
    }

    /// <summary>
    /// 52 cards. Top of the deck = end of the list (Draw takes from the end).
    /// Shuffle = Fisher-Yates with an unbiased RNG. The post-shuffle order is kept in
    /// InitialOrder so any hand can be replayed exactly.
    /// </summary>
    public sealed class Deck
    {
        private readonly List<Card> _cards = new List<Card>(52);
        private readonly Card[] _initialOrder;

        public int Remaining => _cards.Count;

        /// <summary>Deck order right after the shuffle (index 0 = bottom, last = top).</summary>
        public IReadOnlyList<Card> InitialOrder => _initialOrder;

        /// <summary>Cards still in the deck (read-only view, bottom to top).</summary>
        public IReadOnlyList<Card> RemainingCards => _cards;

        /// <summary>Normal play. nextInt = null uses SecureRng; nextInt(n) must return [0, n).</summary>
        public Deck(Func<int, int> nextInt = null)
        {
            foreach (Suit s in (Suit[])Enum.GetValues(typeof(Suit)))
                foreach (Rank r in (Rank[])Enum.GetValues(typeof(Rank)))
                    _cards.Add(new Card(r, s));

            Shuffle(nextInt ?? SecureRng.Next);
            _initialOrder = _cards.ToArray();
        }

        /// <summary>Replay / test: exact order, bottom to top (same layout as InitialOrder of a saved hand).</summary>
        public Deck(IEnumerable<Card> exactOrder)
        {
            _cards.AddRange(exactOrder);
            if (_cards.Count != 52)
                throw new ArgumentException("A deck must have 52 cards, got " + _cards.Count + ".");
            if (new HashSet<Card>(_cards).Count != 52)
                throw new ArgumentException("Deck contains duplicate cards.");
            _initialOrder = _cards.ToArray();
        }

        /// <summary>Looks at a card without drawing it. depthFromTop 0 = the top card.</summary>
        public Card Peek(int depthFromTop = 0)
        {
            if (depthFromTop < 0 || depthFromTop >= _cards.Count)
                throw new ArgumentOutOfRangeException(nameof(depthFromTop));
            return _cards[_cards.Count - 1 - depthFromTop];
        }

        public bool Contains(Card c) => _cards.Contains(c);

        /// <summary>Puts a card into the deck. index 0 = bottom, Remaining = top. Throws if that card is already in the deck.</summary>
        public void Insert(int index, Card c)
        {
            if (index < 0 || index > _cards.Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (_cards.Contains(c)) throw new InvalidOperationException("Card " + c + " is already in the deck.");
            _cards.Insert(index, c);
        }

        /// <summary>Shuffles a card back in at a random depth (unbiased). nextInt = null uses SecureRng.</summary>
        public void InsertRandom(Card c, Func<int, int> nextInt = null)
        {
            int j = (nextInt ?? SecureRng.Next)(_cards.Count + 1);
            if (j < 0 || j > _cards.Count)
                throw new InvalidOperationException("RNG returned " + j + ", expected a value in [0, " + _cards.Count + "].");
            Insert(j, c);
        }

        /// <summary>Removes one specific card from the deck. Throws if it is not in the deck.</summary>
        public Card Take(Card c)
        {
            if (!_cards.Remove(c)) throw new InvalidOperationException("Card " + c + " is not in the deck.");
            return c;
        }

        public Card Draw()
        {
            if (_cards.Count == 0)
                throw new InvalidOperationException("Deck is empty.");
            Card c = _cards[_cards.Count - 1];
            _cards.RemoveAt(_cards.Count - 1);
            return c;
        }

        private void Shuffle(Func<int, int> nextInt)
        {
            for (int i = _cards.Count - 1; i > 0; i--) // Fisher-Yates
            {
                int j = nextInt(i + 1);
                if (j < 0 || j > i)
                    throw new InvalidOperationException("RNG returned " + j + ", expected a value in [0, " + i + "].");
                Card tmp = _cards[i]; _cards[i] = _cards[j]; _cards[j] = tmp;
            }
        }
    }
}