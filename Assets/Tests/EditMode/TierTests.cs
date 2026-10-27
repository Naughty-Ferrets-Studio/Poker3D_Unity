using System;
using NUnit.Framework;
using Holdem.PowerUps;

namespace Holdem.Tests
{
    public class TierTests
    {
        [Test]
        public void Tiers_AreInOrder_AndThereAreFive()
        {
            Assert.AreEqual(5, TierExtensions.Count);
            Assert.AreEqual(0, (int)Tier.Common);
            Assert.AreEqual(4, (int)Tier.Legendary);
            Assert.Less((int)Tier.Rare, (int)Tier.Epic);
        }

        [Test]
        public void OnlyLegendaryCannotUpgrade()
        {
            foreach (Tier t in new[] { Tier.Common, Tier.Rare, Tier.Epic, Tier.Mythic })
                Assert.IsTrue(t.CanUpgrade(), t + " should be upgradeable");
            Assert.IsFalse(Tier.Legendary.CanUpgrade());
        }

        [Test]
        public void NextUp_WalksUpOneTierAtATime_AndThrowsAtLegendary()
        {
            Assert.AreEqual(Tier.Rare, Tier.Common.NextUp());
            Assert.AreEqual(Tier.Epic, Tier.Rare.NextUp());
            Assert.AreEqual(Tier.Mythic, Tier.Epic.NextUp());
            Assert.AreEqual(Tier.Legendary, Tier.Mythic.NextUp());
            Assert.Throws<InvalidOperationException>(() => Tier.Legendary.NextUp());
        }
    }

    public class TierOddsTests
    {
        private const int Categories = 10;

        [Test]
        public void Validate_PassesForTheCurrentTable()
        {
            string problem;
            Assert.IsTrue(TierOdds.Validate(out problem), problem);
            Assert.IsNull(problem);
        }

        [Test]
        public void EveryRow_AddsUpToOneHundredPercent()
        {
            for (int h = 0; h < Categories; h++)
            {
                double sum = 0;
                for (int t = 0; t < TierExtensions.Count; t++) sum += TierOdds.Chance(h, (Tier)t);
                Assert.AreEqual(1.0, sum, 1e-9, HandEvaluator.Names[h]);
            }
        }

        [Test]
        public void RoyalFlush_IsAlwaysLegendary()
        {
            Assert.AreEqual(1.0, TierOdds.Chance(HandEvaluator.RoyalFlush, Tier.Legendary), 1e-9);
            for (int r = 0; r < TierOdds.RowTotal; r++)
            {
                int roll = r;
                Assert.AreEqual(Tier.Legendary, TierOdds.Roll(HandEvaluator.RoyalFlush, n => roll), "roll " + r);
            }
        }

        [Test]
        public void EveryNonRoyalHand_CanRollEveryTier()
        {
            for (int h = 0; h < HandEvaluator.RoyalFlush; h++)
                for (int t = 0; t < TierExtensions.Count; t++)
                    Assert.Greater(TierOdds.Chance(h, (Tier)t), 0.0, HandEvaluator.Names[h] + " / " + (Tier)t);
        }

        [Test]
        public void Roll_FollowsTheColumnBoundaries_ForHighCard()
        {
            // High Card row: Common 771, Rare 177, Epic 41, Mythic 9, Legendary 2 (cumulative 771, 948, 989, 998, 1000)
            (int roll, Tier tier)[] cases =
            {
                (0, Tier.Common), (770, Tier.Common),
                (771, Tier.Rare), (947, Tier.Rare),
                (948, Tier.Epic), (988, Tier.Epic),
                (989, Tier.Mythic), (997, Tier.Mythic),
                (998, Tier.Legendary), (999, Tier.Legendary),
            };
            foreach (var c in cases)
            {
                int roll = c.roll;
                Assert.AreEqual(c.tier, TierOdds.Roll(0, n => roll), "roll " + roll);
            }
        }

        [Test]
        public void Roll_AsksTheRngForNumbersBelowTheRowTotal()
        {
            int asked = -1;
            TierOdds.Roll(3, n => { asked = n; return 0; });
            Assert.AreEqual(TierOdds.RowTotal, asked);
        }

        [Test]
        public void Roll_RejectsBadRngResultsAndBadHandRanks()
        {
            Assert.Throws<InvalidOperationException>(() => TierOdds.Roll(0, n => TierOdds.RowTotal));
            Assert.Throws<InvalidOperationException>(() => TierOdds.Roll(0, n => -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => TierOdds.Roll(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => TierOdds.Roll(10));
            Assert.Throws<ArgumentOutOfRangeException>(() => TierOdds.Chance(10, Tier.Common));
        }

        [Test]
        public void ABetterHand_NeverRollsALowerTier_ForTheSameRandomNumber()
        {
            for (int r = 0; r < TierOdds.RowTotal; r++)
            {
                int roll = r;
                for (int h = 1; h < Categories; h++)
                    Assert.GreaterOrEqual((int)TierOdds.Roll(h, n => roll), (int)TierOdds.Roll(h - 1, n => roll),
                        HandEvaluator.Names[h] + " vs " + HandEvaluator.Names[h - 1] + " at roll " + roll);
            }
        }

        [Test]
        public void Roll_MatchesTheTableOverManyRandomRolls()
        {
            var rng = new Random(2024);
            const int rolls = 100000;
            int common = 0;
            for (int i = 0; i < rolls; i++)
                if (TierOdds.Roll(1, n => rng.Next(n)) == Tier.Common) common++;

            double seen = common / (double)rolls;               // One Pair: 70.7% Common
            Assert.AreEqual(TierOdds.Chance(1, Tier.Common), seen, 0.01);
        }
    }
}
