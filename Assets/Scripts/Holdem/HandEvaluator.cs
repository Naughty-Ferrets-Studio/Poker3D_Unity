using System;
using System.Collections.Generic;
using System.Linq;

namespace Holdem
{

public static class HandEvaluator
{
    public static readonly string[] Names =
    {
        "High Card", "Pair", "Two Pair", "Three of a Kind", "Straight",
        "Flush", "Full House", "Four of a Kind", "Straight Flush", "Royal Flush"
    };

    private const long Base5 = 15L * 15 * 15 * 15 * 15;

    /// <summary>Category number of a Royal Flush (the 10th and highest hand rank).</summary>
    public const int RoyalFlush = 9;

    public static int Category(long score) => (int)(score / Base5);

    /// <summary>Card -> 0..51 (rank-major, 4 suits per rank).</summary>
    public static int Code(Card c) => ((int)c.Rank - 2) * 4 + (int)c.Suit;

    /// <summary>Best 5-card hand out of 5-7 cards. Higher score = better hand.</summary>
    public static long Evaluate(IReadOnlyList<Card> cards)
    {
        Span<int> buf = stackalloc int[cards.Count];
        for (int i = 0; i < buf.Length; i++) buf[i] = Code(cards[i]);
        return EvaluateCodes(buf);
    }

    // score = category * 15^5 + five tie-break digits (ranks 2..14, so base 15 never overflows a digit)
    private static long Pack(int cat, int d0, int d1, int d2, int d3, int d4) =>
        ((((cat * 15L + d0) * 15 + d1) * 15 + d2) * 15 + d3) * 15 + d4;

    /// <summary>High card of the best straight in a rank bit-mask (bit r = rank r present), or 0. The ace also plays low.</summary>
    private static int StraightHigh(int mask)
    {
        if ((mask & (1 << 14)) != 0) mask |= 2;
        for (int h = 14; h >= 5; h--)
            if (((mask >> (h - 4)) & 31) == 31) return h;
        return 0;
    }

    /// <summary>Highest rank below 'below' that has at least minCount cards and is not ex1/ex2; 0 if none.</summary>
    private static int Pick(ReadOnlySpan<int> rankCount, int minCount, int below, int ex1, int ex2)
    {
        for (int r = Math.Min(14, below - 1); r >= 2; r--)
            if (rankCount[r] >= minCount && r != ex1 && r != ex2) return r;
        return 0;
    }

    /// <summary>Allocation-free evaluator over card codes (see <see cref="Code"/>). Works for 5-7 cards.</summary>
    public static long EvaluateCodes(ReadOnlySpan<int> codes)
    {
        Span<int> rc = stackalloc int[15];       // cards per rank
        Span<int> suitMask = stackalloc int[4];  // rank bit-mask per suit
        Span<int> suitCount = stackalloc int[4];
        int allMask = 0;
        foreach (int cd in codes)
        {
            int rk = (cd >> 2) + 2, su = cd & 3;
            rc[rk]++; suitCount[su]++; suitMask[su] |= 1 << rk; allMask |= 1 << rk;
        }

        int flushSuit = -1;
        for (int su = 0; su < 4; su++) if (suitCount[su] >= 5) flushSuit = su;

        if (flushSuit >= 0)
        {
            int sfHigh = StraightHigh(suitMask[flushSuit]);
            if (sfHigh > 0) return Pack(sfHigh == 14 ? RoyalFlush : 8, sfHigh, 0, 0, 0, 0); // ace-high straight flush = royal flush
        }

        int quad = Pick(rc, 4, 15, 0, 0);
        if (quad > 0)
        {
            int quadKicker = Pick(rc, 1, 15, quad, 0);
            return Pack(7, quad, quad, quad, quad, quadKicker);
        }

        int trips = Pick(rc, 3, 15, 0, 0);
        if (trips > 0)
        {
            int fhPair = Pick(rc, 2, 15, trips, 0);
            if (fhPair > 0) return Pack(6, trips, trips, trips, fhPair, fhPair);
        }

        if (flushSuit >= 0)
        {
            int f0 = 0, f1 = 0, f2 = 0, f3 = 0, f4 = 0, fk = 0;
            for (int rk = 14; rk >= 2 && fk < 5; rk--)
            {
                if ((suitMask[flushSuit] & (1 << rk)) == 0) continue;
                fk++;
                if (fk == 1) f0 = rk; else if (fk == 2) f1 = rk; else if (fk == 3) f2 = rk; else if (fk == 4) f3 = rk; else f4 = rk;
            }
            return Pack(5, f0, f1, f2, f3, f4);
        }

        int straight = StraightHigh(allMask);
        if (straight > 0) return Pack(4, straight, 0, 0, 0, 0);

        if (trips > 0)
        {
            int tk1 = Pick(rc, 1, 15, trips, 0);
            int tk2 = Pick(rc, 1, tk1, trips, 0);
            return Pack(3, trips, trips, trips, tk1, tk2);
        }

        int pair1 = Pick(rc, 2, 15, 0, 0);
        if (pair1 > 0)
        {
            int pair2 = Pick(rc, 2, 15, pair1, 0);
            if (pair2 > 0)
            {
                int tpKicker = Pick(rc, 1, 15, pair1, pair2);
                return Pack(2, pair1, pair1, pair2, pair2, tpKicker);
            }
            int pk1 = Pick(rc, 1, 15, pair1, 0);
            int pk2 = Pick(rc, 1, pk1, pair1, 0);
            int pk3 = Pick(rc, 1, pk2, pair1, 0);
            return Pack(1, pair1, pair1, pk1, pk2, pk3);
        }

        int h0 = Pick(rc, 1, 15, 0, 0);
        int h1 = Pick(rc, 1, h0, 0, 0);
        int h2 = Pick(rc, 1, h1, 0, 0);
        int h3 = Pick(rc, 1, h2, 0, 0);
        int h4 = Pick(rc, 1, h3, 0, 0);
        return Pack(0, h0, h1, h2, h3, h4);
    }
}
}