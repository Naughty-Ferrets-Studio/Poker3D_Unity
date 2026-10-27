using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Holdem.Tests
{
    public class HandEvaluatorTests
    {
        private static long Score(string cards) => HandEvaluator.Evaluate(TestUtil.Cards(cards));
        private static int Cat(string cards) => HandEvaluator.Category(Score(cards));

        [Test]
        public void Names_HasTenRanks_AndRoyalFlushIsLast()
        {
            Assert.AreEqual(10, HandEvaluator.Names.Length);
            Assert.AreEqual(9, HandEvaluator.RoyalFlush);
            Assert.AreEqual("Royal Flush", HandEvaluator.Names[HandEvaluator.RoyalFlush]);
        }

        [Test]
        public void Code_GivesEveryCardAUniqueNumberFrom0To51()
        {
            var seen = new HashSet<int>();
            foreach (Card c in TestUtil.OrderedDeck())
            {
                int code = HandEvaluator.Code(c);
                Assert.That(code, Is.InRange(0, 51));
                Assert.IsTrue(seen.Add(code), "Duplicate code for " + c);
            }
            Assert.AreEqual(52, seen.Count);
        }

        // ------------------------------------------------------------ one example per hand rank
        [TestCase("As 9d 7c 5s 3d 2c Kh", 0, TestName = "HighCard")]
        [TestCase("Qs Qh 9d 8c 7s 4d 2c", 1, TestName = "Pair")]
        [TestCase("Qs Qh 9d 9c 7s 4d 2c", 2, TestName = "TwoPair")]
        [TestCase("Qs Qh Qd 9c 7s 4d 2c", 3, TestName = "ThreeOfAKind")]
        [TestCase("9s 8h 7d 6c 5s Ad Kc", 4, TestName = "Straight")]
        [TestCase("As 2h 3d 4c 5s 9d Kc", 4, TestName = "Straight_Wheel")]
        [TestCase("As Kh Qd Jc Ts 2d 3c", 4, TestName = "Straight_Broadway_MixedSuits")]
        [TestCase("As 9s 7s 4s 2s Kd Qc", 5, TestName = "Flush")]
        [TestCase("Ks Kh Kd 4c 4s 9d 2c", 6, TestName = "FullHouse")]
        [TestCase("Ks Kh Kd 4c 4s 4d 2c", 6, TestName = "FullHouse_TwoTrips")]
        [TestCase("7s 7h 7d 7c Ks 2d 3c", 7, TestName = "FourOfAKind")]
        [TestCase("9h 8h 7h 6h 5h Ad Kc", 8, TestName = "StraightFlush")]
        [TestCase("Ah 2h 3h 4h 5h Kd Qc", 8, TestName = "StraightFlush_Wheel_IsNotRoyal")]
        [TestCase("As Ks Qs Js Ts 2d 3c", 9, TestName = "RoyalFlush")]
        [TestCase("9s 8s 7s 6s 2s 5h 4d", 5, TestName = "FlushBeatsStraightWhenBothExist")]
        public void Category_IsCorrect(string cards, int expected)
        {
            Assert.AreEqual(expected, Cat(cards), HandEvaluator.Names[expected] + " expected for: " + cards);
        }

        [Test]
        public void Evaluate_AcceptsFiveAndSixCards()
        {
            Assert.AreEqual(9, Cat("As Ks Qs Js Ts"));
            Assert.AreEqual(7, Cat("7s 7h 7d 7c Ks 2d"));
        }

        [Test]
        public void Royal_IsOnlyTheAceHighStraightFlush()
        {
            Assert.Greater(Score("As Ks Qs Js Ts"), Score("Ks Qs Js Ts 9s"));
            Assert.AreEqual(HandEvaluator.RoyalFlush, HandEvaluator.Category(Score("Ad Kd Qd Jd Td")));
            Assert.AreEqual(8, HandEvaluator.Category(Score("Kd Qd Jd Td 9d")));
        }

        // ------------------------------------------------------------ ordering
        [Test]
        public void Score_IncreasesWithEveryHandRank()
        {
            string[] ladder =
            {
                "As Kh 9d 7c 5s 3d 2c",   // high card
                "Qs Qh 9d 8c 7s 4d 2c",   // pair
                "Qs Qh 9d 9c 7s 4d 2c",   // two pair
                "Qs Qh Qd 9c 7s 4d 2c",   // trips
                "9s 8h 7d 6c 5s Ad Kc",   // straight
                "As 9s 7s 4s 2s Kd Qc",   // flush
                "Ks Kh Kd 4c 4s 9d 2c",   // full house
                "7s 7h 7d 7c Ks 2d 3c",   // quads
                "9h 8h 7h 6h 5h Ad Kc",   // straight flush
                "As Ks Qs Js Ts 2d 3c",   // royal flush
            };
            long previous = long.MinValue;
            for (int i = 0; i < ladder.Length; i++)
            {
                long s = Score(ladder[i]);
                Assert.AreEqual(i, HandEvaluator.Category(s), "wrong category for " + ladder[i]);
                Assert.Greater(s, previous, "score did not go up at rank " + i);
                previous = s;
            }
        }

        [Test]
        public void Kickers_DecideBetweenEqualPairs()
        {
            Assert.Greater(Score("As Ah Kd 7c 5s 3d 2c"), Score("Ac Ad Qd 7h 5h 3c 2d"));
        }

        [Test]
        public void TwoPair_HigherTopPairWins()
        {
            Assert.Greater(Score("Ks Kh 2d 2c 7s 4d 9c"), Score("Qs Qh Jd Jc 7s 4d 9c"));
        }

        [Test]
        public void ThreePairs_UseTheBestTwoPairsAndBestKicker()
        {
            // K K 9 9 + best remaining card (a 4 in both hands), so these two are exactly equal
            Assert.AreEqual(Score("Ks Kh 9d 9c 4s 4d 2c"), Score("Ks Kh 9d 9c 4s 3d 2c"));
        }

        [Test]
        public void Wheel_IsTheLowestStraight()
        {
            Assert.Less(Score("As 2h 3d 4c 5s 9d Kc"), Score("2s 3h 4d 5c 6s 9d Kc"));
        }

        [Test]
        public void Flush_HighestCardsDecide()
        {
            Assert.Greater(Score("As Ks 9s 5s 3s 2d 7c"), Score("As Qs 9s 5s 3s 2d 7c"));
        }

        [Test]
        public void BoardPlays_GivesExactlyEqualScores()
        {
            // both players play the 9-high straight on the board
            Assert.AreEqual(Score("9s 8h 7d 6c 5s 2d 2c"), Score("9s 8h 7d 6c 5s Kd 3c"));
        }

        // ------------------------------------------------------------ every 5-card hand (fast, ~1 second)
        [Test]
        public void AllFiveCardHands_MatchKnownFrequencies()
        {
            long[] expected = { 1302540, 1098240, 123552, 54912, 10200, 5108, 3744, 624, 36, 4 };
            long[] counts = new long[10];
            Span<int> h = stackalloc int[5];

            for (int a = 0; a < 48; a++) { h[0] = a;
            for (int b = a + 1; b < 49; b++) { h[1] = b;
            for (int c = b + 1; c < 50; c++) { h[2] = c;
            for (int d = c + 1; d < 51; d++) { h[3] = d;
            for (int e = d + 1; e < 52; e++) { h[4] = e;
                counts[HandEvaluator.Category(HandEvaluator.EvaluateCodes(h))]++;
            }}}}}

            for (int i = 0; i < 10; i++)
                Assert.AreEqual(expected[i], counts[i], HandEvaluator.Names[i]);
        }

        // ------------------------------------------------------------ every 7-card hand (slow, run on purpose)
        [Test, Explicit("Checks all 133,784,560 seven-card hands. Takes roughly 15-60 seconds. Right-click it in the Test Runner and choose Run.")]
        [Category("Slow")]
        public void AllSevenCardHands_MatchKnownFrequencies()
        {
            long[] expected = { 23294460, 58627800, 31433400, 6461620, 6180020, 4047644, 3473184, 224848, 37260, 4324 };
            long[] counts = new long[10];
            Span<int> h = stackalloc int[7];

            for (int a = 0; a < 46; a++) { h[0] = a;
            for (int b = a + 1; b < 47; b++) { h[1] = b;
            for (int c = b + 1; c < 48; c++) { h[2] = c;
            for (int d = c + 1; d < 49; d++) { h[3] = d;
            for (int e = d + 1; e < 50; e++) { h[4] = e;
            for (int f = e + 1; f < 51; f++) { h[5] = f;
            for (int g = f + 1; g < 52; g++) { h[6] = g;
                counts[HandEvaluator.Category(HandEvaluator.EvaluateCodes(h))]++;
            }}}}}}}

            long total = 0;
            for (int i = 0; i < 10; i++)
            {
                Assert.AreEqual(expected[i], counts[i], HandEvaluator.Names[i]);
                total += counts[i];
            }
            Assert.AreEqual(133784560L, total);
        }
    }
}
