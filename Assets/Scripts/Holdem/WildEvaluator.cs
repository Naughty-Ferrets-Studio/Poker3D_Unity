using System;
using System.Collections.Generic;

namespace Holdem
{
    /// <summary>
    /// Best hand when some cards are wild (a Joker, or all the 2s). A wild card counts as any card the holder wants,
    /// but never as a card that is already in the same five-card hand, so five of a kind cannot happen.
    /// The ten normal hand ranks stay the only ranks.
    /// </summary>
    public static class WildEvaluator
    {
        private const long Base5 = 15L * 15 * 15 * 15 * 15;
        private const long Digit4 = 15L * 15 * 15 * 15;

        private static long StraightFlushScore(int high) =>
            (high == 14 ? HandEvaluator.RoyalFlush : 8) * Base5 + high * Digit4;

        /// <summary>Same scale as HandEvaluator.Evaluate (higher = better). Returns -1 if there are fewer than 5 cards.</summary>
        public static long Evaluate(IReadOnlyList<Card> cards, Func<Card, bool> isWild)
        {
            var real = new List<int>();
            int w = 0;
            foreach (Card c in cards)
            {
                if (isWild(c)) w++;
                else real.Add(HandEvaluator.Code(c));
            }
            if (cards.Count < 5) return -1;
            if (w == 0) return HandEvaluator.Evaluate(cards);
            if (w >= 5) return StraightFlushScore(14);

            // 1) Straight flush / royal flush: a window of 5 ranks in one suit that the real cards plus the wilds can fill.
            for (int high = 14; high >= 5; high--)
                for (int suit = 0; suit < 4; suit++)
                {
                    int have = 0;
                    foreach (int code in real)
                    {
                        if ((code & 3) != suit) continue;
                        int rank = (code >> 2) + 2;
                        bool inWindow = high == 5 ? (rank <= 5 || rank == 14) : (rank <= high && rank > high - 5);
                        if (inWindow) have++;
                    }
                    if (have + w >= 5) return StraightFlushScore(high);
                }

            // 2) Everything else: use every wild (a wild is never worse than the real card it replaces),
            //    try every way to pick the real cards, and every way to let the wilds stand for other cards.
            int m = 5 - w;
            long best = -1;
            var five = new int[5];
            var pick = new int[m];

            void Wilds(int slot, int start)
            {
                if (slot == 5)
                {
                    long s = HandEvaluator.EvaluateCodes(five);
                    if (s > best) best = s;
                    return;
                }
                for (int code = start; code < 52; code++)
                {
                    bool clash = false;
                    for (int i = 0; i < m; i++) if (five[i] == code) { clash = true; break; }
                    if (clash) continue;
                    five[slot] = code;
                    Wilds(slot + 1, code + 1);
                }
            }

            void Reals(int idx, int start)
            {
                if (idx == m)
                {
                    for (int i = 0; i < m; i++) five[i] = pick[i];
                    Wilds(m, 0);
                    return;
                }
                for (int i = start; i < real.Count; i++) { pick[idx] = real[i]; Reals(idx + 1, i + 1); }
            }

            Reals(0, 0);
            return best;
        }
    }
}
