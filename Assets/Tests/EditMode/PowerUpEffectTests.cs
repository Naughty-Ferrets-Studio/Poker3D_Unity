using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Holdem.PowerUps;

namespace Holdem.Tests
{
    public class PowerUpEffectTests
    {
        private static (GameEngine e, PowerUpSystem sys) Hand(int bots = 2)
        {
            var e = V.NewEngine(bots);
            e.StartHand();
            return (e, new PowerUpSystem(e));
        }

        private static PowerUpInstance Give(PowerUpSystem sys, int seat, string id, Tier t)
        {
            Assert.IsTrue(sys.Give(seat, id, t));
            return sys.Inventory(seat).Items.Last();
        }

        // ================================================================ registry and tiers
        [Test]
        public void TheTierMap_MatchesTheDesignList()
        {
            var expected = new Dictionary<string, Tier[]>
            {
                { "bombpot",    new[] { Tier.Common } },
                { "shankoemee", new[] { Tier.Rare } },
                { "wild",       new[] { Tier.Rare, Tier.Mythic } },
                { "cross",      new[] { Tier.Legendary } },
                { "runit",      new[] { Tier.Epic, Tier.Legendary } },
                { "pineapple",  new[] { Tier.Rare, Tier.Epic, Tier.Mythic } },
                { "snatch",     new[] { Tier.Rare, Tier.Epic } },
                { "disguised",  new[] { Tier.Rare, Tier.Epic, Tier.Mythic } },
                { "bumba",      new[] { Tier.Rare, Tier.Epic, Tier.Mythic } },
                { "xray",       new[] { Tier.Epic, Tier.Mythic } },
                { "dice",       new[] { Tier.Legendary } },
            };
            Assert.AreEqual(expected.Count, PowerUpRegistry.All.Count);
            foreach (var kv in expected) CollectionAssert.AreEqual(kv.Value, PowerUpRegistry.Find(kv.Key).Tiers, kv.Key);
            Assert.AreEqual(21, PowerUpRegistry.All.Sum(d => d.Tiers.Length));
        }

        [Test]
        public void EveryTierHasSomethingToDrop_AndTheCountsMatchTheList()
        {
            Assert.AreEqual(1, PowerUpRegistry.Candidates(Tier.Common).Count);
            Assert.AreEqual(6, PowerUpRegistry.Candidates(Tier.Rare).Count);
            Assert.AreEqual(6, PowerUpRegistry.Candidates(Tier.Epic).Count);
            Assert.AreEqual(5, PowerUpRegistry.Candidates(Tier.Mythic).Count);
            Assert.AreEqual(3, PowerUpRegistry.Candidates(Tier.Legendary).Count);
        }

        [Test]
        public void VersionNames()
        {
            Assert.AreEqual("Joker", PowerUpRegistry.Find("wild").VersionName(Tier.Rare));
            Assert.AreEqual("Wild Twos", PowerUpRegistry.Find("wild").VersionName(Tier.Mythic));
            Assert.AreEqual("Run It Twice", PowerUpRegistry.Find("runit").VersionName(Tier.Epic));
            Assert.AreEqual("Run It Thrice", PowerUpRegistry.Find("runit").VersionName(Tier.Legendary));
            Assert.AreEqual("Bumba Card II", PowerUpRegistry.Find("bumba").VersionName(Tier.Epic));
            Assert.AreEqual("The Dice", PowerUpRegistry.Find("dice").VersionName(Tier.Legendary));
        }

        [Test]
        public void Merging_GoesToTheNextVersion_EvenWhenItSkipsATier()
        {
            var runIt = PowerUpRegistry.Find("runit"); var joker = PowerUpRegistry.Find("wild");
            Assert.IsTrue(PowerUpInventory.CanMerge(new PowerUpInstance(runIt, Tier.Epic), new PowerUpInstance(runIt, Tier.Epic)));
            var inv = new PowerUpInventory();
            inv.TryAdd(new PowerUpInstance(runIt, Tier.Epic)); inv.TryAdd(new PowerUpInstance(runIt, Tier.Epic));
            Assert.AreEqual(Tier.Legendary, inv.MergeHeld(0, 1).Tier);   // Run It Twice + Run It Twice = Thrice
            inv.Clear();
            inv.TryAdd(new PowerUpInstance(joker, Tier.Rare)); inv.TryAdd(new PowerUpInstance(joker, Tier.Rare));
            Assert.AreEqual(Tier.Mythic, inv.MergeHeld(0, 1).Tier);      // Joker + Joker = Wild Twos
        }

        // ================================================================ timing windows
        [Test]
        public void PreHandPowerUps_ShapeTheNextHand_ByChangingNextRules()
        {
            var cases = new (string id, Tier tier, Func<HandRules, bool> flag)[]
            {
                ("bombpot", Tier.Common, r => r.BombPot),
                ("wild", Tier.Rare, r => r.Joker),
                ("wild", Tier.Mythic, r => r.WildTwos),
                ("cross", Tier.Legendary, r => r.Cross),
                ("shankoemee", Tier.Rare, r => r.ShanKoeMee.Contains(0)),
            };
            foreach (var c in cases)
            {
                var e = V.NewEngine(2);
                var sys = new PowerUpSystem(e);
                var item = Give(sys, 0, c.id, c.tier);
                Assert.IsNull(sys.CanUse(0, item), c.id);
                Assert.IsTrue(sys.Use(0, item, UseRequest.None).Ok, c.id);
                Assert.IsTrue(c.flag(e.NextRules), c.id);
                Assert.IsFalse(c.flag(e.Rules), c.id + ": must not change the hand already on the table");
                // a second one of the same is refused and not spent
                var again = Give(sys, 0, c.id, c.tier);
                Assert.IsFalse(sys.Use(0, again, UseRequest.None).Ok, c.id);
                Assert.AreEqual(1, sys.Inventory(0).Count);
            }
        }

        [Test]
        public void PreHandPowerUps_CannotBeUsedMidHand_UnlessDevModeIgnoresTiming()
        {
            var (e, sys) = Hand();
            var item = Give(sys, 0, "bombpot", Tier.Common);
            Assert.IsFalse(sys.Use(0, item, UseRequest.None).Ok);
            sys.IgnoreTiming = true;
            Assert.IsTrue(sys.Use(0, item, UseRequest.None).Ok);
            Assert.IsTrue(e.NextRules.BombPot);     // shapes the NEXT hand
            Assert.IsFalse(e.Rules.BombPot);
        }

        [Test]
        public void TheBombPotSet_BetweenHands_IsPlayedInTheNextHand()
        {
            var e = V.NewEngine(2);
            var sys = new PowerUpSystem(e);
            sys.Use(0, Give(sys, 0, "bombpot", Tier.Common), UseRequest.None);
            e.StartHand();
            Assert.AreEqual(Street.Flop, e.Street);
        }

        // ================================================================ snatch the sock
        [Test]
        public void Snatch_II_TakesTheChosenCard_AndGivesOneOfYours()
        {
            var (e, sys) = Hand();
            int me = e.Current, t = (me + 1) % 3;
            var item = Give(sys, me, "snatch", Tier.Epic);
            Card mine = e.Players[me].Hole[0], theirs = e.Players[t].Hole[1];
            var r = sys.Use(me, item, new UseRequest { TargetSeat = t, CardIndex = 0, TargetCardIndex = 1 });
            Assert.IsTrue(r.Ok, r.Message);
            Assert.AreEqual(theirs, e.Players[me].Hole[0]);
            Assert.AreEqual(mine, e.Players[t].Hole[1]);
            Assert.AreEqual(theirs, r.Secret[0]);
            Assert.AreEqual(2, e.DealEvents.Count(x => x.Kind == DealKind.Swap));
            V.Finish(e);
        }

        [Test]
        public void Snatch_I_TakesARandomCard()
        {
            var (e, sys) = Hand();
            int me = e.Current, t = (me + 2) % 3;
            var item = Give(sys, me, "snatch", Tier.Rare);
            var theirOld = e.Players[t].Hole.ToList(); Card mine = e.Players[me].Hole[1];
            Assert.IsTrue(sys.Use(me, item, new UseRequest { TargetSeat = t, CardIndex = 1 }).Ok);
            Assert.IsTrue(theirOld.Contains(e.Players[me].Hole[1]));
            Assert.IsTrue(e.Players[t].Hole.Contains(mine));
            Assert.AreEqual(2, e.Players[me].Hole.Count);
            Assert.AreEqual(2, e.Players[t].Hole.Count);
        }

        [Test]
        public void Snatch_RefusesSelf_Folded_AndWrongTurn_WithoutSpending()
        {
            var (e, sys) = Hand();
            int me = e.Current, t = (me + 1) % 3, other = (me + 2) % 3;
            var item = Give(sys, me, "snatch", Tier.Epic);
            Assert.IsFalse(sys.Use(me, item, new UseRequest { TargetSeat = me, CardIndex = 0, TargetCardIndex = 0 }).Ok);
            e.Players[t].Folded = true;
            Assert.IsFalse(sys.Use(me, item, new UseRequest { TargetSeat = t, CardIndex = 0, TargetCardIndex = 0 }).Ok);
            Assert.IsFalse(sys.Use(me, item, new UseRequest { TargetSeat = other, CardIndex = 5, TargetCardIndex = 0 }).Ok);
            var notMine = Give(sys, other, "snatch", Tier.Epic);
            Assert.IsFalse(sys.Use(other, notMine, new UseRequest { TargetSeat = me, CardIndex = 0, TargetCardIndex = 0 }).Ok);
            Assert.AreEqual(1, sys.Inventory(me).Count);
        }

        [Test]
        public void Snatch_ATakenCardLosesItsSticker()
        {
            var (e, sys) = Hand();
            int me = e.Current, t = (me + 1) % 3;
            Card real = e.Players[t].Hole[0];
            Card sticker = e.DeckCards.First();
            e.ApplySticker(t, 0, sticker);
            var item = Give(sys, me, "snatch", Tier.Epic);
            Assert.IsTrue(sys.Use(me, item, new UseRequest { TargetSeat = t, CardIndex = 0, TargetCardIndex = 0 }).Ok);
            Assert.AreEqual(real, e.Players[me].Hole[0]);   // the real card, not the sticker
            Assert.IsFalse(e.GetDisguise(t).HasValue);
            V.Finish(e);
        }

        // ================================================================ disguised card
        private static Card FreshCard(GameEngine e) => e.DeckCards.First(c => !e.Players.SelectMany(p => p.Hole).Contains(c));

        [Test]
        public void Disguised_PutsAStickerOnACard_AndItCountsAsTheSticker()
        {
            var (e, sys) = Hand();
            Card real = e.Players[0].Hole[0]; Card sticker = FreshCard(e);
            var item = Give(sys, 0, "disguised", Tier.Rare);
            var r = sys.Use(0, item, new UseRequest { CardIndex = 0, ChosenCard = sticker });
            Assert.IsTrue(r.Ok, r.Message);
            Assert.AreEqual(sticker, e.Players[0].Hole[0]);
            Assert.AreEqual(real, e.GetDisguise(0).Value.Real);
            Assert.AreEqual(1, sys.Effects(0).Count);
            Assert.IsTrue(e.DealEvents.Any(x => x.Kind == DealKind.Swap && x.Card == sticker && x.Previous == real));
            V.Finish(e);   // the 52-card check still passes with a fake card on the table
        }

        [Test]
        public void Disguised_RefusesAStickerYouCanSee_AndASecondSticker()
        {
            var (e, sys) = Hand();
            var item = Give(sys, 0, "disguised", Tier.Epic);
            Assert.IsFalse(sys.Use(0, item, new UseRequest { CardIndex = 0, ChosenCard = e.Players[0].Hole[1] }).Ok);
            Assert.IsFalse(sys.Use(0, item, new UseRequest { CardIndex = 0 }).Ok);
            Assert.IsFalse(sys.Use(0, item, new UseRequest { CardIndex = 3, ChosenCard = FreshCard(e) }).Ok);
            Assert.AreEqual(1, sys.Inventory(0).Count);
            Assert.IsTrue(sys.Use(0, item, new UseRequest { CardIndex = 0, ChosenCard = FreshCard(e) }).Ok);
            var second = Give(sys, 0, "disguised", Tier.Rare);
            Assert.IsFalse(sys.Use(0, second, new UseRequest { CardIndex = 1, ChosenCard = e.DeckCards.Last() }).Ok);
        }

        [Test]
        public void Disguised_TellsGetWeakerWithTheTier()
        {
            Assert.AreEqual(TellLevel.Many, DisguisedCard.Tells(Tier.Rare));
            Assert.AreEqual(TellLevel.Some, DisguisedCard.Tells(Tier.Epic));
            Assert.AreEqual(TellLevel.None, DisguisedCard.Tells(Tier.Mythic));
        }

        private static (GameEngine e, PowerUpSystem sys, Card real, Card sticker) Disguised(Tier tier)
        {
            var (e, sys) = Hand();
            Card real = e.Players[0].Hole[0]; Card sticker = FreshCard(e);
            Assert.IsTrue(sys.Use(0, Give(sys, 0, "disguised", tier), new UseRequest { CardIndex = 0, ChosenCard = sticker }).Ok);
            return (e, sys, real, sticker);
        }

        [Test]
        public void Accuse_Disguised_RightGuess_ShowsTheRealCard_AndPaysTheBounty()
        {
            var (e, sys, real, sticker) = Disguised(Tier.Epic);
            bool higher = (int)real.Rank > (int)sticker.Rank;
            int accuser = e.Players[1].Chips, accused = e.Players[0].Chips;
            var r = sys.Accuse(new AccuseRequest { Accuser = 1, Target = 0, NamedId = "disguised", GuessHigher = higher });
            Assert.IsTrue(r.Ok, r.Message);
            Assert.AreEqual(accuser + 2 * GameEngine.Ante, e.Players[1].Chips);
            Assert.AreEqual(accused - 2 * GameEngine.Ante, e.Players[0].Chips);
            Assert.AreEqual(real, e.Players[0].Hole[0]);
            Assert.IsFalse(e.GetDisguise(0).HasValue);
            Assert.IsTrue(e.DealEvents.Any(x => x.Kind == DealKind.Reveal && x.Card == real && x.Viewer == -1));
            Assert.AreEqual(0, sys.Effects(0).Count);
            V.Finish(e);
        }

        [Test]
        public void Accuse_Disguised_Mythic_PaysThreeAntes()
        {
            var (e, sys, real, sticker) = Disguised(Tier.Mythic);
            int before = e.Players[1].Chips;
            Assert.IsTrue(sys.Accuse(new AccuseRequest { Accuser = 1, Target = 0, NamedId = "disguised", GuessHigher = (int)real.Rank > (int)sticker.Rank }).Ok);
            Assert.AreEqual(before + 3 * GameEngine.Ante, e.Players[1].Chips);
        }

        [Test]
        public void Accuse_Disguised_WrongGuess_TheDisguiseStays_AndTheAccuserPays()
        {
            var (e, sys, real, sticker) = Disguised(Tier.Epic);
            bool wrong = !((int)real.Rank > (int)sticker.Rank);   // a tie counts as "lower", so the right guess is "not higher"
            int accuser = e.Players[1].Chips, accused = e.Players[0].Chips;
            var r = sys.Accuse(new AccuseRequest { Accuser = 1, Target = 0, NamedId = "disguised", GuessHigher = wrong });
            Assert.IsTrue(r.Ok);
            Assert.IsTrue(r.Message.StartsWith("Wrong guess"), r.Message);
            Assert.IsTrue(e.Players[1].Chips < accuser);
            Assert.AreEqual(accuser - e.Players[1].Chips, e.Players[0].Chips - accused);
            Assert.AreEqual(sticker, e.Players[0].Hole[0]);
            Assert.IsTrue(e.GetDisguise(0).HasValue);
        }

        [Test]
        public void Accuse_ATieCountsAsLower()
        {
            var (e, sys) = Hand();
            Card real = e.Players[0].Hole[0];
            Card sameRank = e.DeckCards.FirstOrDefault(c => c.Rank == real.Rank);
            if (sameRank.Equals(default(Card))) return; // no card of that rank left (cannot happen with this seed, but stay safe)
            Assert.IsTrue(sys.Use(0, Give(sys, 0, "disguised", Tier.Rare), new UseRequest { CardIndex = 0, ChosenCard = sameRank }).Ok);
            var r = sys.Accuse(new AccuseRequest { Accuser = 1, Target = 0, NamedId = "disguised", GuessHigher = false });
            Assert.IsTrue(r.Message.Contains("caught"), r.Message);
        }

        [Test]
        public void Accuse_FalseAccusations_CostTheAccuser()
        {
            var (e, sys) = Hand();
            int before = e.Players[1].Chips, target = e.Players[0].Chips;
            var r = sys.Accuse(new AccuseRequest { Accuser = 1, Target = 0, NamedId = "disguised", GuessHigher = true });
            Assert.IsTrue(r.Ok);
            Assert.IsTrue(r.Message.StartsWith("False accusation"), r.Message);
            Assert.IsTrue(e.Players[1].Chips <= before - GameEngine.Ante);
            Assert.AreEqual(before - e.Players[1].Chips, e.Players[0].Chips - target);
        }

        [Test]
        public void Accuse_NamingTheWrongPowerUp_IsFalse()
        {
            var (e, sys, real, sticker) = Disguised(Tier.Rare);
            var r = sys.Accuse(new AccuseRequest { Accuser = 1, Target = 0, NamedId = "bumba" });
            Assert.IsTrue(r.Message.StartsWith("False accusation"), r.Message);
            Assert.IsTrue(e.GetDisguise(0).HasValue);
            var xray = sys.Accuse(new AccuseRequest { Accuser = 2, Target = 0, NamedId = "xray" });
            Assert.IsTrue(xray.Message.StartsWith("False accusation"), xray.Message);
        }

        [Test]
        public void Accuse_OnlyOncePerHand_AndAGuessIsRequiredFirst()
        {
            var (e, sys, real, sticker) = Disguised(Tier.Rare);
            var noGuess = sys.Accuse(new AccuseRequest { Accuser = 1, Target = 0, NamedId = "disguised" });
            Assert.IsFalse(noGuess.Ok);                      // not counted
            Assert.IsFalse(sys.HasAccused(1));
            Assert.IsTrue(sys.Accuse(new AccuseRequest { Accuser = 1, Target = 0, NamedId = "disguised", GuessHigher = true }).Ok);
            Assert.IsTrue(sys.HasAccused(1));
            Assert.IsFalse(sys.Accuse(new AccuseRequest { Accuser = 1, Target = 0, NamedId = "disguised", GuessHigher = true }).Ok);
            Assert.IsFalse(sys.Accuse(new AccuseRequest { Accuser = 1, Target = 1, NamedId = "disguised" }).Ok); // yourself
        }

        [Test]
        public void Accuse_ANewHand_StartsFresh()
        {
            var (e, sys, real, sticker) = Disguised(Tier.Rare);
            sys.Accuse(new AccuseRequest { Accuser = 1, Target = 0, NamedId = "disguised", GuessHigher = true });
            V.Finish(e);
            e.StartHand();
            Assert.AreEqual(0, sys.AllEffects().Count);
            Assert.IsFalse(sys.HasAccused(1));
        }

        // ================================================================ bumba card
        [Test]
        public void Bumba_I_TakesTheTopCardAndSitsOnIt_ThenSneaksItIn()
        {
            var (e, sys) = Hand();
            var log = V.Log(e);
            Card top = e.DeckCards.Last(); int deck = e.DeckCards.Count;
            var r = sys.Use(0, Give(sys, 0, "bumba", Tier.Rare), UseRequest.None);
            Assert.IsTrue(r.Ok, r.Message);
            Assert.AreEqual(top, e.HeldCard(0));
            Assert.AreEqual(deck - 1, e.DeckCards.Count);
            Assert.AreEqual(top, r.Secret[0]);
            Assert.IsTrue(log.Any(l => l.Contains("A card is missing")));
            Assert.IsFalse(log.Any(l => l.Contains(e.Players[0].Name + " takes")), "the dealer must not say who took it");
            Assert.AreEqual(2, e.Players[0].Hole.Count);

            Card old = e.Players[0].Hole[1];
            var f = sys.UseFollowUp(0, sys.Effects(0)[0], new UseRequest { CardIndex = 1 });
            Assert.IsTrue(f.Ok, f.Message);
            Assert.AreEqual(top, e.Players[0].Hole[1]);
            Assert.IsFalse(e.DeckCards.Contains(old), "the old card goes into the discard pile, not the deck");
            Assert.IsFalse(e.HasHeldCard(0));
            Assert.AreEqual(0, sys.Effects(0).Count);
            V.Finish(e);
        }

        [Test]
        public void Bumba_ACardNobodySwappedIn_GoesBackIntoTheDeckWhenTheHandEnds()
        {
            var (e, sys) = Hand();
            Card top = e.DeckCards.Last();
            sys.Use(0, Give(sys, 0, "bumba", Tier.Rare), UseRequest.None);
            V.Finish(e);                                 // the 52-card check must still pass
            Assert.IsFalse(e.HasHeldCard(0));
            Assert.IsTrue(e.DeckCards.Contains(top));
        }

        [Test]
        public void Bumba_II_TakesACardOfTheChosenSuit()
        {
            var (e, sys) = Hand();
            Assert.IsFalse(sys.Use(0, Give(sys, 0, "bumba", Tier.Epic), UseRequest.None).Ok);    // needs a suit
            var item = sys.Inventory(0).Items[0];
            Assert.IsTrue(sys.Use(0, item, new UseRequest { ChosenSuit = Suit.Hearts }).Ok);
            Assert.AreEqual(Suit.Hearts, e.HeldCard(0).Value.Suit);
        }

        [Test]
        public void Bumba_III_TakesTheExactCard_OrFailsWithoutSpending()
        {
            var (e, sys) = Hand();
            var item = Give(sys, 0, "bumba", Tier.Mythic);
            Card heldByAnother = e.Players[1].Hole[0];
            Assert.IsFalse(sys.Use(0, item, new UseRequest { ChosenCard = heldByAnother }).Ok);
            Assert.AreEqual(1, sys.Inventory(0).Count);
            Card inDeck = e.DeckCards[9];
            Assert.IsTrue(sys.Use(0, item, new UseRequest { ChosenCard = inDeck }).Ok);
            Assert.AreEqual(inDeck, e.HeldCard(0));
            Assert.AreEqual(0, sys.Inventory(0).Count);
        }

        [Test]
        public void Bumba_CannotSitOnTwoCards_AndTheFollowUpNeedsAHeldCard()
        {
            var (e, sys) = Hand();
            sys.Use(0, Give(sys, 0, "bumba", Tier.Rare), UseRequest.None);
            Assert.IsFalse(sys.Use(0, Give(sys, 0, "bumba", Tier.Rare), UseRequest.None).Ok);
            Assert.IsFalse(sys.UseFollowUp(0, sys.Effects(0)[0], new UseRequest { CardIndex = 7 }).Ok);  // bad card index: not spent
            Assert.IsTrue(e.HasHeldCard(0));
        }

        [Test]
        public void Accuse_Bumba_Caught_ReturnsTheCard_ShowsAHoleCard_AndPaysTheBounty()
        {
            var (e, sys) = Hand();
            Card top = e.DeckCards.Last();
            sys.Use(0, Give(sys, 0, "bumba", Tier.Rare), UseRequest.None);
            int accuser = e.Players[2].Chips, thief = e.Players[0].Chips;
            Card shown = e.Players[0].Hole[1];
            var r = sys.Accuse(new AccuseRequest { Accuser = 2, Target = 0, NamedId = "bumba", RevealIndex = 1 });
            Assert.IsTrue(r.Ok);
            Assert.IsTrue(r.Message.Contains("caught"), r.Message);
            Assert.AreEqual(accuser + 2 * GameEngine.Ante, e.Players[2].Chips);
            Assert.AreEqual(thief - 2 * GameEngine.Ante, e.Players[0].Chips);
            Assert.IsFalse(e.HasHeldCard(0));
            Assert.IsTrue(e.DeckCards.Contains(top), "the stolen card goes back into the deck");
            Assert.IsTrue(e.DealEvents.Any(x => x.Kind == DealKind.Reveal && x.Seat == 0 && x.Card == shown && x.Viewer == -1));
            Assert.AreEqual(0, sys.Effects(0).Count);
            V.Finish(e);
        }

        [Test]
        public void Accuse_Bumba_Mythic_PaysThreeAntes()
        {
            var (e, sys) = Hand();
            sys.Use(0, Give(sys, 0, "bumba", Tier.Mythic), new UseRequest { ChosenCard = e.DeckCards[3] });
            int before = e.Players[1].Chips;
            sys.Accuse(new AccuseRequest { Accuser = 1, Target = 0, NamedId = "bumba" });
            Assert.AreEqual(before + 3 * GameEngine.Ante, e.Players[1].Chips);
        }

        [Test]
        public void Accuse_Bumba_AfterTheSwap_IsTooLate()
        {
            var (e, sys) = Hand();
            sys.Use(0, Give(sys, 0, "bumba", Tier.Rare), UseRequest.None);
            sys.UseFollowUp(0, sys.Effects(0)[0], new UseRequest { CardIndex = 0 });
            int before = e.Players[1].Chips;
            var r = sys.Accuse(new AccuseRequest { Accuser = 1, Target = 0, NamedId = "bumba" });
            Assert.IsTrue(r.Message.StartsWith("False accusation"), r.Message);
            Assert.IsTrue(e.Players[1].Chips < before);
        }


        // ================================================================ tells (the shaking avatar)
        [Test]
        public void Tells_ADisguiseShakesLessWithTheTier_AndTheLevelIsPublic()
        {
            foreach (var (tier, expected) in new[] { (Tier.Rare, 1f), (Tier.Epic, 0.5f), (Tier.Mythic, 0f) })
            {
                var (e, sys) = Hand();
                Assert.AreEqual(0f, sys.TellLevel(0), 0.0001);
                sys.Use(0, Give(sys, 0, "disguised", tier), new UseRequest { CardIndex = 0, ChosenCard = FreshCard(e) });
                Assert.AreEqual(expected, sys.TellLevel(0), 0.0001, tier.ToString());
                Assert.AreEqual(0f, sys.TellLevel(1), 0.0001, "other players stay calm");
            }
        }

        [Test]
        public void Tells_SittingOnABumbaCardTwitches_UntilYouSneakItIn()
        {
            var (e, sys) = Hand();
            sys.Use(0, Give(sys, 0, "bumba", Tier.Rare), UseRequest.None);
            Assert.AreEqual(0.8f, sys.TellLevel(0), 0.0001);
            sys.UseFollowUp(0, sys.Effects(0)[0], new UseRequest { CardIndex = 0 });
            Assert.AreEqual(0f, sys.TellLevel(0), 0.0001);
        }

        [Test]
        public void Tells_ABluffTwitchLooksTheSame_ButCostsNothing_AndEndsWithTheHand()
        {
            var (e, sys) = Hand();
            sys.Bluff(2, 0.7f);
            Assert.AreEqual(0.7f, sys.TellLevel(2), 0.0001);
            Assert.AreEqual(0, sys.Effects(2).Count);
            V.Finish(e);
            Assert.AreEqual(0f, sys.TellLevel(2), 0.0001);   // hand over
            e.StartHand();
            Assert.AreEqual(0f, sys.TellLevel(2), 0.0001);   // a new hand wipes it
        }

        [Test]
        public void Tells_AFoldedPlayerStopsTwitching()
        {
            var (e, sys) = Hand();
            sys.Bluff(1, 1f);
            e.Players[1].Folded = true;
            Assert.AreEqual(0f, sys.TellLevel(1), 0.0001);
        }

        [Test]
        public void TheOneAccusationPerHand_IsSharedAcrossPowerUps()
        {
            var (e, sys) = Hand();
            sys.Use(0, Give(sys, 0, "bumba", Tier.Rare), UseRequest.None);
            sys.Use(2, Give(sys, 2, "disguised", Tier.Rare), new UseRequest { CardIndex = 0, ChosenCard = FreshCard(e) });
            Assert.IsTrue(sys.Accuse(new AccuseRequest { Accuser = 1, Target = 0, NamedId = "bumba" }).Ok);
            Assert.IsFalse(sys.Accuse(new AccuseRequest { Accuser = 1, Target = 2, NamedId = "disguised", GuessHigher = true }).Ok);
        }
    }
}
