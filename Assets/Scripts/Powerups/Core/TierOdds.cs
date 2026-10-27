using System;

namespace Holdem.PowerUps
{
    /// <summary>
    /// Which tier a player's final hand can win. Rows are the 10 hand ranks
    /// (0 = High Card ... 9 = Royal Flush, same numbers as HandEvaluator.Category).
    /// Columns are Common, Rare, Epic, Mythic, Legendary, written in tenths of a percent (each row adds up to 1000).
    /// Royal Flush is always Legendary.
    /// Tuning: change the numbers in Weights and call Validate().
    /// </summary>
    public static class TierOdds
    {
        public const int RowTotal = 1000;

        private static readonly int[][] Weights =
        {
            new[] { 771, 177,  41,   9,    2 },  // 0 High Card
            new[] { 707, 208,  62,  18,    5 },  // 1 One Pair
            new[] { 626, 237,  90,  34,   13 },  // 2 Two Pair
            new[] { 527, 257, 125,  61,   30 },  // 3 Three of a Kind
            new[] { 415, 259, 162, 101,   63 },  // 4 Straight
            new[] { 296, 238, 191, 153,  122 },  // 5 Flush
            new[] { 189, 194, 200, 206,  211 },  // 6 Full House
            new[] { 106, 140, 185, 245,  324 },  // 7 Four of a Kind
            new[] {  53,  90, 154, 261,  442 },  // 8 Straight Flush
            new[] {   0,   0,   0,   0, 1000 },  // 9 Royal Flush
        };

        /// <summary>Chance (0 to 1) of getting this exact tier with this hand rank.</summary>
        public static double Chance(int handCategory, Tier tier)
        {
            CheckCategory(handCategory);
            return Weights[handCategory][(int)tier] / (double)RowTotal;
        }

        /// <summary>
        /// Picks a tier for a hand rank. nextInt(n) must return a value in [0, n); null uses SecureRng.
        /// Pass your own function in tests to force a result.
        /// </summary>
        public static Tier Roll(int handCategory, Func<int, int> nextInt = null)
        {
            CheckCategory(handCategory);
            int r = (nextInt ?? SecureRng.Next)(RowTotal);
            if (r < 0 || r >= RowTotal)
                throw new InvalidOperationException("RNG returned " + r + ", expected a value in [0, " + RowTotal + ").");

            int[] row = Weights[handCategory];
            int acc = 0;
            for (int t = 0; t < row.Length; t++)
            {
                acc += row[t];
                if (r < acc) return (Tier)t;
            }
            return Tier.Legendary; // unreachable while every row adds up to RowTotal
        }

        /// <summary>True when every row has 5 columns and adds up to 1000, Royal Flush is 100% Legendary, and a better hand never has a lower chance of a given tier or higher.</summary>
        public static bool Validate(out string problem)
        {
            if (Weights.Length != HandEvaluator.Names.Length) { problem = "Weights has " + Weights.Length + " rows, expected " + HandEvaluator.Names.Length + "."; return false; }
            for (int h = 0; h < Weights.Length; h++)
            {
                if (Weights[h].Length != TierExtensions.Count) { problem = "Row " + h + " does not have 5 columns."; return false; }
                int sum = 0;
                foreach (int w in Weights[h]) { if (w < 0) { problem = "Row " + h + " has a negative weight."; return false; } sum += w; }
                if (sum != RowTotal) { problem = "Row " + h + " (" + HandEvaluator.Names[h] + ") adds up to " + sum + ", expected " + RowTotal + "."; return false; }
            }
            if (Weights[HandEvaluator.RoyalFlush][(int)Tier.Legendary] != RowTotal) { problem = "Royal Flush must be 100% Legendary."; return false; }

            for (int t = 1; t < TierExtensions.Count; t++)           // chance of tier t or higher must never drop as the hand improves
                for (int h = 1; h < Weights.Length; h++)
                    if (AtLeast(h, t) < AtLeast(h - 1, t)) { problem = HandEvaluator.Names[h] + " has a lower chance of " + (Tier)t + " or better than " + HandEvaluator.Names[h - 1] + "."; return false; }

            problem = null;
            return true;
        }

        private static int AtLeast(int h, int tier)
        {
            int s = 0;
            for (int t = tier; t < TierExtensions.Count; t++) s += Weights[h][t];
            return s;
        }

        private static void CheckCategory(int c)
        {
            if (c < 0 || c >= Weights.Length) throw new ArgumentOutOfRangeException(nameof(c), "Hand category must be 0 to 9.");
        }
    }
}
