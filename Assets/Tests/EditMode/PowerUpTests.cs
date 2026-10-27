using System;
using System.Linq;
using NUnit.Framework;
using Holdem.PowerUps;

namespace Holdem.Tests
{
    public class PowerUpInventoryTests
    {
        private static PowerUpInstance P(string id, Tier t) => new PowerUpInstance(PowerUpRegistry.Find(id), t);

        [Test]
        public void Add_StopsAtThree_ThenReportsFull()
        {
            var inv = new PowerUpInventory();
            Assert.AreEqual(AddResult.Added, inv.TryAdd(P("xray", Tier.Common)));
            Assert.AreEqual(AddResult.Added, inv.TryAdd(P("dice", Tier.Common)));
            Assert.AreEqual(AddResult.Added, inv.TryAdd(P("bumba", Tier.Common)));
            Assert.AreEqual(AddResult.Full, inv.TryAdd(P("xray", Tier.Rare)));
            Assert.AreEqual(3, inv.Count);
        }

        [Test]
        public void Full_ReplaceAt_KeepsTheNewOneAndDropsTheOld()
        {
            var inv = new PowerUpInventory();
            inv.TryAdd(P("xray", Tier.Common)); inv.TryAdd(P("dice", Tier.Common)); inv.TryAdd(P("bumba", Tier.Common));
            var incoming = P("xray", Tier.Epic);
            var dropped = inv.ReplaceAt(1, incoming);
            Assert.AreEqual("dice", dropped.Definition.Id);
            Assert.AreEqual(3, inv.Count);
            Assert.AreSame(incoming, inv.Items[1]);
        }

        [Test]
        public void MergeHeld_TwoSameTier_BecomesOneAtNextTier()
        {
            var inv = new PowerUpInventory();
            inv.TryAdd(P("xray", Tier.Rare)); inv.TryAdd(P("dice", Tier.Rare)); inv.TryAdd(P("xray", Tier.Rare));
            var up = inv.MergeHeld(0, 2);
            Assert.AreEqual(Tier.Epic, up.Tier);
            Assert.AreEqual(2, inv.Count);
            Assert.AreEqual("xray", inv.Items[0].Definition.Id);
        }

        [Test]
        public void MergeIncoming_WorksWhenFull()
        {
            var inv = new PowerUpInventory();
            inv.TryAdd(P("xray", Tier.Common)); inv.TryAdd(P("dice", Tier.Common)); inv.TryAdd(P("bumba", Tier.Common));
            var incoming = P("dice", Tier.Common);
            int i = inv.FindMergePartner(incoming);
            Assert.AreEqual(1, i);
            var up = inv.MergeIncoming(i, incoming);
            Assert.AreEqual(Tier.Rare, up.Tier);
            Assert.AreEqual(3, inv.Count);
        }

        [Test]
        public void Merge_NeedsSamePowerUpAndSameTier_AndLegendaryRefuses()
        {
            Assert.IsFalse(PowerUpInventory.CanMerge(P("xray", Tier.Rare), P("dice", Tier.Rare)));
            Assert.IsFalse(PowerUpInventory.CanMerge(P("xray", Tier.Rare), P("xray", Tier.Epic)));
            Assert.IsFalse(PowerUpInventory.CanMerge(P("xray", Tier.Legendary), P("xray", Tier.Legendary)));
            Assert.IsTrue(PowerUpInventory.CanMerge(P("xray", Tier.Mythic), P("xray", Tier.Mythic)));
        }

        [Test]
        public void Registry_IdsAreUnique_AndEveryTierHasADescription()
        {
            Assert.AreEqual(PowerUpRegistry.All.Count, PowerUpRegistry.All.Select(d => d.Id).Distinct().Count());
            foreach (var d in PowerUpRegistry.All)
                for (int t = 0; t < TierExtensions.Count; t++)
                    Assert.IsTrue(d.Describe((Tier)t).Length > 0, d.Id + " tier " + t);
        }
    }

    public class PowerUpUseTests
    {
        // 3 players (you + 2 bots), seeded shuffle so the tests are repeatable. Everybody just checks / calls.
        private static GameEngine NewHand()
        {
            var e = new GameEngine { NextInt = TestUtil.Seeded(7) };
            e.NewGame(2);
            e.StartHand();
            return e;
        }

        private static void ToStreet(GameEngine e, int boardCards)
        {
            int guard = 0;
            while (!e.HandOver && e.Community.Count < boardCards && guard++ < 50) e.Act(PlayerAction.CheckCall);
            Assert.AreEqual(boardCards, e.Community.Count);
        }

        private static void FinishHand(GameEngine e)
        {
            int guard = 0;
            while (!e.HandOver && guard++ < 50) e.Act(PlayerAction.CheckCall); // EndHand runs the 52-card check, it must not throw
            Assert.IsTrue(e.HandOver);
        }

        private static (PowerUpSystem sys, PowerUpInstance item) Give(GameEngine e, int seat, string id, Tier t)
        {
            var sys = new PowerUpSystem(e);
            Assert.IsTrue(sys.Give(seat, id, t));
            return (sys, sys.Inventory(seat).Items[0]);
        }

        // ---------------------------------------------------------------- X-Ray
        [Test]
        public void XRay_Common_ShowsOneCardOfTheTarget_AndIsSpent()
        {
            var e = NewHand(); int me = e.Current; int target = (me + 1) % 3;
            var (sys, item) = Give(e, me, "xray", Tier.Common);
            var r = sys.Use(me, item, new UseRequest { TargetSeat = target });
            Assert.IsTrue(r.Ok, r.Message);
            Assert.AreEqual(1, r.Secret.Count);
            Assert.IsTrue(e.Players[target].Hole.Contains(r.Secret[0]));
            Assert.AreEqual(0, sys.Inventory(me).Count);
            FinishHand(e);
        }

        [Test]
        public void XRay_Rare_ShowsBothCards()
        {
            var e = NewHand(); int me = e.Current; int target = (me + 2) % 3;
            var (sys, item) = Give(e, me, "xray", Tier.Rare);
            var r = sys.Use(me, item, new UseRequest { TargetSeat = target });
            Assert.IsTrue(r.Ok, r.Message);
            CollectionAssert.AreEquivalent(e.Players[target].Hole, r.Secret);
        }

        [Test]
        public void XRay_OnlyOnYourTurn_UnlessDevModeIgnoresTiming()
        {
            var e = NewHand(); int notMe = (e.Current + 1) % 3; int target = (e.Current + 2) % 3;
            var (sys, item) = Give(e, notMe, "xray", Tier.Common);
            var r = sys.Use(notMe, item, new UseRequest { TargetSeat = target });
            Assert.IsFalse(r.Ok);
            Assert.AreEqual(1, sys.Inventory(notMe).Count); // not spent
            sys.IgnoreTiming = true;
            Assert.IsTrue(sys.Use(notMe, item, new UseRequest { TargetSeat = target }).Ok);
        }

        [Test]
        public void XRay_RefusesSelfAndFoldedTargets_WithoutSpendingIt()
        {
            var e = NewHand(); int me = e.Current; int target = (me + 1) % 3;
            var (sys, item) = Give(e, me, "xray", Tier.Common);
            Assert.IsFalse(sys.Use(me, item, new UseRequest { TargetSeat = me }).Ok);
            Assert.IsFalse(sys.Use(me, item, new UseRequest { TargetSeat = -1 }).Ok);
            e.Players[target].Folded = true;
            Assert.IsFalse(sys.Use(me, item, new UseRequest { TargetSeat = target }).Ok);
            Assert.AreEqual(1, sys.Inventory(me).Count);
        }

        [Test]
        public void XRay_EmitsARevealEventOnlyTheUserSees()
        {
            var e = NewHand(); int me = e.Current; int target = (me + 1) % 3;
            var (sys, item) = Give(e, me, "xray", Tier.Rare);
            sys.Use(me, item, new UseRequest { TargetSeat = target });
            var ev = e.DealEvents.Where(x => x.Kind == DealKind.Reveal).ToList();
            Assert.AreEqual(2, ev.Count);
            Assert.IsTrue(ev.All(x => x.Viewer == me && x.Seat == target));
        }

        // ---------------------------------------------------------------- The Dice
        [Test]
        public void Dice_CannotBeUsedBeforeAllFiveCommunityCards()
        {
            var e = NewHand(); ToStreet(e, 4);
            int me = e.Current;
            var (sys, item) = Give(e, me, "dice", Tier.Common);
            Assert.IsFalse(sys.Use(me, item, UseRequest.None).Ok);
            Assert.AreEqual(1, sys.Inventory(me).Count);
        }

        [Test]
        public void Dice_RollOneToFive_ReplacesTheMatchingBoardCard()
        {
            int[] expectedIndex = { 0, 1, 2, 3, 4 };
            for (int roll = 1; roll <= 5; roll++)
            {
                var e = NewHand(); ToStreet(e, 5);
                int me = e.Current;
                var (sys, item) = Give(e, me, "dice", Tier.Common);
                var before = e.Community.ToList();
                var top = e.DeckCards.Last();
                var r = sys.Use(me, item, new UseRequest { ForcedRoll = roll });
                Assert.IsTrue(r.Ok, r.Message);
                int i = expectedIndex[roll - 1];
                Assert.AreEqual(top, e.Community[i], "roll " + roll);
                Assert.IsTrue(e.DeckCards.Contains(before[i]), "old card went back into the deck");
                for (int k = 0; k < 5; k++) if (k != i) Assert.AreEqual(before[k], e.Community[k]);
                FinishHand(e);
            }
        }

        [Test]
        public void Dice_RollSix_LetsYouChooseTheCard()
        {
            var e = NewHand(); ToStreet(e, 5);
            int me = e.Current;
            var (sys, item) = Give(e, me, "dice", Tier.Common);
            var before = e.Community.ToList();
            var r = sys.Use(me, item, new UseRequest { ForcedRoll = 6, CardIndex = 2 });
            Assert.IsTrue(r.Ok, r.Message);
            Assert.AreNotEqual(before[2], e.Community[2]);
            FinishHand(e);
        }

        [Test]
        public void Dice_RealRoll_AlwaysLandsOnOneBoardCard()
        {
            var e = NewHand(); ToStreet(e, 5);
            int me = e.Current;
            var (sys, item) = Give(e, me, "dice", Tier.Common);
            var before = e.Community.ToList();
            Assert.IsTrue(sys.Use(me, item, new UseRequest { CardIndex = 0 }).Ok);
            int changed = Enumerable.Range(0, 5).Count(k => before[k] != e.Community[k]);
            Assert.AreEqual(1, changed);
        }

        // ---------------------------------------------------------------- Bumba Card
        [Test]
        public void Bumba_Common_TakesTheTopCard()
        {
            var e = NewHand(); int me = e.Current;
            var (sys, item) = Give(e, me, "bumba", Tier.Common);
            var top = e.DeckCards.Last(); var old = e.Players[me].Hole[1];
            var r = sys.Use(me, item, new UseRequest { CardIndex = 1 });
            Assert.IsTrue(r.Ok, r.Message);
            Assert.AreEqual(top, e.Players[me].Hole[1]);
            Assert.IsTrue(e.DeckCards.Contains(old));
            FinishHand(e);
        }

        [Test]
        public void Bumba_Rare_GivesACardOfTheChosenSuit()
        {
            var e = NewHand(); int me = e.Current;
            var (sys, item) = Give(e, me, "bumba", Tier.Rare);
            var r = sys.Use(me, item, new UseRequest { CardIndex = 0, ChosenSuit = Suit.Hearts });
            Assert.IsTrue(r.Ok, r.Message);
            Assert.AreEqual(Suit.Hearts, e.Players[me].Hole[0].Suit);
            FinishHand(e);
        }

        [Test]
        public void Bumba_Epic_GivesTheExactCard_OrFailsWithoutSpending()
        {
            var e = NewHand(); int me = e.Current;
            var (sys, item) = Give(e, me, "bumba", Tier.Epic);
            Card inDeck = e.DeckCards[10];
            Card heldByAnother = e.Players[(me + 1) % 3].Hole[0];

            var bad = sys.Use(me, item, new UseRequest { CardIndex = 0, ChosenCard = heldByAnother });
            Assert.IsFalse(bad.Ok);
            Assert.AreEqual(1, sys.Inventory(me).Count);

            var ok = sys.Use(me, item, new UseRequest { CardIndex = 0, ChosenCard = inDeck });
            Assert.IsTrue(ok.Ok, ok.Message);
            Assert.AreEqual(inDeck, e.Players[me].Hole[0]);
            Assert.AreEqual(0, sys.Inventory(me).Count);
            FinishHand(e);
        }

        [Test]
        public void Bumba_Legendary_BehavesLikeEpic()
        {
            var e = NewHand(); int me = e.Current;
            var (sys, item) = Give(e, me, "bumba", Tier.Legendary);
            Card inDeck = e.DeckCards[5];
            Assert.IsTrue(sys.Use(me, item, new UseRequest { CardIndex = 1, ChosenCard = inDeck }).Ok);
            Assert.AreEqual(inDeck, e.Players[me].Hole[1]);
        }

        // ---------------------------------------------------------------- general
        [Test]
        public void NothingCanBeUsedOutsideAHand_OrAfterFolding()
        {
            var e = new GameEngine(); e.NewGame(2);
            var sys = new PowerUpSystem(e);
            sys.Give(0, "bumba", Tier.Common);
            Assert.IsFalse(sys.Use(0, sys.Inventory(0).Items[0], new UseRequest { CardIndex = 0 }).Ok);

            e.StartHand();
            e.Players[0].Folded = true;
            Assert.IsFalse(sys.Use(0, sys.Inventory(0).Items[0], new UseRequest { CardIndex = 0 }).Ok);
        }

        [Test]
        public void UsingTheSameItemTwice_OnlyWorksOnce()
        {
            var e = NewHand(); int me = e.Current;
            var (sys, item) = Give(e, me, "bumba", Tier.Common);
            Assert.IsTrue(sys.Use(me, item, new UseRequest { CardIndex = 0 }).Ok);
            Assert.IsFalse(sys.Use(me, item, new UseRequest { CardIndex = 0 }).Ok);
        }
    }
}
