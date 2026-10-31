using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Holdem.PowerUps;

namespace Holdem.Tests
{
    internal static class V
    {
        public static Card C(string s) { Assert.IsTrue(CardParser.TryParse(s, out Card c), s); return c; }

        /// <summary>"As Kd Jk": a list of cards. Jk is the joker.</summary>
        public static List<Card> Cards(string s) =>
            s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t == "Jk" ? Card.Joker : C(t)).ToList();

        public static GameEngine NewEngine(int bots = 2, int seed = 11)
        {
            var e = new GameEngine { NextInt = TestUtil.Seeded(seed) };
            e.NewGame(bots);
            return e;
        }

        public static int Chips(GameEngine e) => e.Players.Sum(p => p.Chips);

        public static List<string> Log(GameEngine e) { var l = new List<string>(); e.Logged += s => l.Add(s); return l; }

        /// <summary>Plays the hand to the end: everybody checks / calls, discards are automatic, run-outs just continue.</summary>
        public static void Finish(GameEngine e)
        {
            int guard = 0;
            while (!e.HandOver && guard++ < 600)
            {
                if (e.AwaitingDiscard)
                {
                    int seat = e.PendingDiscards.First(kv => kv.Value == e.Street && !e.Players[kv.Key].Folded).Key;
                    e.AutoDiscard(seat);
                    continue;
                }
                if (e.RunoutPending) { e.ContinueRunout(); continue; }
                e.Act(PlayerAction.CheckCall);
            }
            Assert.IsTrue(e.HandOver, "the hand did not finish");
        }

        public static void ToBoard(GameEngine e, int cards)
        {
            int guard = 0;
            while (!e.HandOver && e.Community.Count < cards && guard++ < 100) e.Act(PlayerAction.CheckCall);
            Assert.AreEqual(cards, e.Community.Count);
        }

        public static string Name(string hand, Func<Card, bool> wild) =>
            HandEvaluator.Names[HandEvaluator.Category(WildEvaluator.Evaluate(Cards(hand), wild))];
        public static readonly Func<Card, bool> JokerWild = c => c.IsJoker;
        public static readonly Func<Card, bool> TwosWild = c => !c.IsJoker && c.Rank == Rank.Two;
    }

    // ================================================================== wild-card evaluator
    public class PowerUpWildEvaluatorTests
    {
        [Test]
        public void Joker_CompletesARoyalFlush() => Assert.AreEqual("Royal Flush", V.Name("As Ks Qs Js Jk 2c 3d", V.JokerWild));

        [Test]
        public void Joker_FillsAStraightFlushGap() => Assert.AreEqual("Straight Flush", V.Name("9h 8h 6h 5h Jk 2c Kd", V.JokerWild));

        [Test]
        public void Joker_AloneOnlyMakesAPair_WhenNothingIsClose() => Assert.AreEqual(HandEvaluator.Names[1], V.Name("As 9d 7c 4s 2d Kh Jk", V.JokerWild));

        [Test]
        public void Joker_MakesAFullHouseFromTwoPair() => Assert.AreEqual("Full House", V.Name("As Ad 9c 9s 4d Kh Jk", V.JokerWild));

        [Test]
        public void FourAcesAndAJoker_IsFourOfAKind_NeverFiveOfAKind() => Assert.AreEqual("Four of a Kind", V.Name("As Ah Ad Ac Jk 2c 3d", V.JokerWild));

        [Test]
        public void FourWildTwos_AlwaysMakeAStraightFlushOrBetter() => Assert.AreEqual("Royal Flush", V.Name("2c 2d 2h 2s 9c 5d Kd", V.TwosWild));

        [Test]
        public void TwoWildTwosAndAPair_MakeFourOfAKind() => Assert.AreEqual("Four of a Kind", V.Name("2c 2d 9h 9s 5c 3d Kh", V.TwosWild));

        [Test]
        public void WithoutAnyWildCard_ItMatchesTheNormalEvaluator()
        {
            var cards = V.Cards("As Kd 9c 7s 4d 2c 3h");
            Assert.AreEqual(HandEvaluator.Evaluate(cards), WildEvaluator.Evaluate(cards, V.JokerWild));
        }

        [Test]
        public void TooFewCards_GivesMinusOne() => Assert.AreEqual(-1L, WildEvaluator.Evaluate(V.Cards("As Kd 9c Jk"), V.JokerWild));

        [Test]
        public void WildCards_NeverMakeAHandWorse_ThanPlayingThemAsTheyAre()
        {
            var rng = new Random(9);
            var all = new List<Card>();
            foreach (Suit su in Enum.GetValues(typeof(Suit))) foreach (Rank r in Enum.GetValues(typeof(Rank))) all.Add(new Card(r, su));
            for (int i = 0; i < 300; i++)
            {
                var deck = all.OrderBy(_ => rng.Next()).Take(7).ToList();
                long wild = WildEvaluator.Evaluate(deck, V.TwosWild);
                long plain = HandEvaluator.Evaluate(deck);
                Assert.GreaterOrEqual(wild, plain, string.Join(" ", deck));
            }
        }
    }

    // ================================================================== hand rules in the engine
    public class PowerUpEngineRuleTests
    {
        // ---------------------------------------------------------------- bomb pot
        [Test]
        public void BombPot_EveryoneAntes_NoBlinds_StartsAtTheFlop()
        {
            var e = V.NewEngine(3);
            int total = V.Chips(e);
            e.NextRules.BombPot = true;
            e.StartHand();
            Assert.AreEqual(Street.Flop, e.Street);
            Assert.AreEqual(3, e.Community.Count);
            foreach (var p in e.Players) Assert.AreEqual(GameEngine.Ante, p.Contrib);
            Assert.AreEqual(4 * GameEngine.Ante, e.Pot);
            Assert.IsTrue(e.Players.All(p => p.Bet == 0));
            Assert.IsTrue(e.Current >= 0);
            V.Finish(e);
            Assert.AreEqual(total, V.Chips(e));
        }

        [Test]
        public void BombPot_OnlyLastsOneHand()
        {
            var e = V.NewEngine(2);
            e.NextRules.BombPot = true;
            e.StartHand(); V.Finish(e);
            e.StartHand();
            Assert.IsFalse(e.Rules.BombPot);
            Assert.AreEqual(Street.Preflop, e.Street);
        }

        // ---------------------------------------------------------------- shan koe mee
        private static GameEngine ShanKoeMeeHand(string seat0Hole, out List<string> log)
        {
            var e = V.NewEngine(2);
            log = V.Log(e);
            e.NextRules.ShanKoeMee.Add(0);
            var sc = new Scenario(); sc.Hole[0] = V.Cards(seat0Hole);
            Assert.IsNull(ScenarioBuilder.Apply(e, sc));
            e.StartHand();
            return e;
        }

        [Test]
        public void ShanKoeMee_CannotFold()
        {
            var e = ShanKoeMeeHand("9s Kd", out _);
            while (e.Current != 0) e.Act(PlayerAction.CheckCall);
            Assert.AreEqual(ActResult.InvalidAction, e.TryAct(0, PlayerAction.Fold, 0));
            e.Act(PlayerAction.Fold);                        // a bot trying to fold is turned into a call
            Assert.IsFalse(e.Players[0].Folded);
        }

        [Test]
        public void ShanKoeMee_ANine_CollectsTwoAntesFromEachOpponent()
        {
            var e = ShanKoeMeeHand("9s Kd", out var log);
            int total = V.Chips(e) + e.Pot; // the blinds are already in the pot
            V.Finish(e);
            Assert.IsTrue(log.Any(l => l.Contains("Shan Koe Mee!") && l.Contains("collects 40")), string.Join("\n", log));
            Assert.AreEqual(total, V.Chips(e));
        }

        [Test]
        public void ShanKoeMee_AnEight_CollectsOneAnte()
        {
            var e = ShanKoeMeeHand("8s Kd", out var log);
            V.Finish(e);
            Assert.IsTrue(log.Any(l => l.Contains("Shan Koe Mee!") && l.Contains("collects 20")), string.Join("\n", log));
        }

        [Test]
        public void ShanKoeMee_AnythingElse_PaysNothing_AtShowdown()
        {
            var e = ShanKoeMeeHand("2c 3d", out var log);
            int total = V.Chips(e) + e.Pot; // the blinds are already in the pot
            V.Finish(e);
            Assert.IsTrue(log.Any(l => l.Contains("missed (5)") && l.Contains("no reward")), string.Join("\n", log));
            Assert.AreEqual(total, V.Chips(e));
        }

        [Test]
        public void ShanKoeMee_EveryoneElseFolds_AMissCostsOneAntePerOpponent()
        {
            var e = ShanKoeMeeHand("2c 3d", out var log);
            int total = V.Chips(e) + e.Pot; // the blinds are already in the pot
            int guard = 0;
            while (!e.HandOver && guard++ < 50) e.Act(e.Current == 0 ? PlayerAction.CheckCall : PlayerAction.Fold);
            Assert.IsTrue(e.HandOver);
            Assert.IsTrue(log.Any(l => l.Contains("missed (5)") && l.Contains("pays 20")), string.Join("\n", log));
            Assert.IsTrue(e.DealEvents.Count(ev => ev.Kind == DealKind.Reveal && ev.Seat == 0 && ev.Viewer == -1) == 2, "the player must show both cards");
            Assert.AreEqual(total, V.Chips(e));
        }

        [Test]
        public void ShanKoeMee_EveryoneElseFolds_AHitStillPays()
        {
            var e = ShanKoeMeeHand("9s Kd", out var log);
            int guard = 0;
            while (!e.HandOver && guard++ < 50) e.Act(e.Current == 0 ? PlayerAction.CheckCall : PlayerAction.Fold);
            Assert.IsTrue(log.Any(l => l.Contains("Shan Koe Mee!") && l.Contains("collects 40")), string.Join("\n", log));
        }

        // ---------------------------------------------------------------- wild cards
        [Test]
        public void Joker_IsShuffledIntoTheDeck_AndTheFiftyThreeCardCheckPasses()
        {
            var e = V.NewEngine(2);
            e.NextRules.Joker = true;
            e.StartHand();
            Assert.AreEqual(53 - 6, e.DeckCards.Count);   // six hole cards are out
            Assert.IsTrue(e.DeckCards.Contains(Card.Joker) || e.Players.Any(p => p.Hole.Contains(Card.Joker)));
            V.Finish(e);                                   // EndHand checks all 53 cards
            e.StartHand();
            Assert.IsFalse(e.Rules.Joker);
            Assert.AreEqual(52 - 6, e.DeckCards.Count);
        }

        [Test]
        public void Joker_CountsAsAnyCardInThePlayersHand()
        {
            var e = V.NewEngine(2);
            e.NextRules.Joker = true;
            e.StartHand();
            e.Players[0].Hole.Clear(); e.Players[0].Hole.AddRange(V.Cards("Jk As"));
            Assert.AreEqual("Four of a Kind", e.HandName(e.Players[0], V.Cards("Ah Ad 9c 4s 2d")));
        }

        [Test]
        public void WildTwos_TheTwosBecomeWild()
        {
            var e = V.NewEngine(2);
            e.NextRules.WildTwos = true;
            e.StartHand();
            e.Players[0].Hole.Clear(); e.Players[0].Hole.AddRange(V.Cards("2c 2d"));
            Assert.AreEqual("Four of a Kind", e.HandName(e.Players[0], V.Cards("9h 9s 5c 3d Kh")));
            V.Finish(e);
        }

        // ---------------------------------------------------------------- the cross
        [Test]
        public void Cross_DealsNineCards_FiveTwoTwo_AndUsesTheBetterLine()
        {
            var e = V.NewEngine(2);
            e.NextRules.Cross = true;
            var sc = new Scenario { Board = V.Cards("2c 7d 9h Th 3s Ah Ac 5d 6d") };
            sc.Hole[0] = V.Cards("As Ad");
            Assert.IsNull(ScenarioBuilder.Apply(e, sc));
            e.StartHand();
            V.ToBoard(e, 5);   // flop: the horizontal line
            V.ToBoard(e, 7);   // turn: two cards above the centre
            V.ToBoard(e, 9);   // river: two cards below
            CollectionAssert.AreEqual(V.Cards("2c 7d 9h Th 3s Ah Ac 5d 6d"), e.Community);
            // horizontal line gives only a pair of aces; the vertical line (Ah Ac 9h 5d 6d) gives four aces
            Assert.AreEqual("Four of a Kind", e.HandName(e.Players[0]));
            long horizontalOnly = HandEvaluator.Evaluate(e.Players[0].Hole.Concat(e.Community.Take(5)).ToList());
            Assert.Greater(e.ScoreFor(e.Players[0], e.Community), horizontalOnly);
            V.Finish(e);
        }

        [Test]
        public void Cross_OnlyLastsOneHand()
        {
            var e = V.NewEngine(2);
            e.NextRules.Cross = true;
            e.StartHand(); V.Finish(e);
            e.StartHand(); V.ToBoard(e, 3);
            Assert.IsFalse(e.Rules.Cross);
        }

        // ---------------------------------------------------------------- run it twice / thrice
        private static GameEngine RunItSetup(string[] draws)
        {
            var e = V.NewEngine(1);                 // heads up: you (seat 0, the button) and one bot
            e.PauseBeforeRunout = true;
            e.NextDeckOverride = TestUtil.DeckWithTop(draws);
            e.StartHand();
            int guard = 0;
            while (!e.RunoutPending && !e.HandOver && guard++ < 10) e.Act(PlayerAction.Raise, 100000); // both all-in
            Assert.IsTrue(e.RunoutPending, "both players should be all-in");
            return e;
        }

        // seat 1 gets As Ah, seat 0 gets Kc Kd. Run 1: kings win. Run 2: aces win. Run 3: aces win.
        private static readonly string[] RunDraws = {
            "As", "Kc", "Ah", "Kd",
            "2c", "Kh", "7d", "2s", "3c", "9d", "4c", "Jc",
            "5c", "Ad", "7h", "8s", "6c", "3d", "Tc", "4s",
            "6s", "Ac", "9h", "5h", "8c", "Qd", "3h", "6d" };

        [Test]
        public void RunItTwice_SplitsEveryPotInHalves()
        {
            var e = RunItSetup(RunDraws);
            var log = V.Log(e);
            Assert.IsTrue(e.RequestRuns(0, 2));
            e.ContinueRunout();
            Assert.IsTrue(e.HandOver);
            Assert.AreEqual(1000, e.Players[0].Chips);   // won run 1: half of 2000
            Assert.AreEqual(1000, e.Players[1].Chips);   // won run 2
            Assert.IsTrue(log.Any(l => l.StartsWith("Run 1:")) && log.Any(l => l.StartsWith("Run 2:")));
        }

        [Test]
        public void RunItThrice_SplitsInThirds_OddChipsToTheFirstRuns()
        {
            var e = RunItSetup(RunDraws);
            Assert.IsTrue(e.RequestRuns(1, 3));
            e.ContinueRunout();
            Assert.AreEqual(667, e.Players[0].Chips);    // run 1: 667
            Assert.AreEqual(1333, e.Players[1].Chips);   // runs 2 and 3: 667 + 666
        }

        [Test]
        public void WithoutRunIt_OnePlayerTakesTheWholePot()
        {
            var e = RunItSetup(RunDraws);
            e.ContinueRunout();
            Assert.IsTrue(e.Players[0].Chips == 2000 || e.Players[1].Chips == 2000 || e.Players[0].Chips == 1000);
            Assert.AreEqual(2000, V.Chips(e));
        }

        [Test]
        public void RunIt_CanOnlyBeUsedByAnAllInPlayer_WhileTheRunoutIsPending()
        {
            var e = V.NewEngine(1);
            e.PauseBeforeRunout = true;
            e.StartHand();
            var sys = new PowerUpSystem(e);
            sys.Give(0, "runit", Tier.Epic);
            var item = sys.Inventory(0).Items[0];
            Assert.IsFalse(sys.Use(0, item, UseRequest.None).Ok);   // nobody is all-in yet
            int guard = 0;
            while (!e.RunoutPending && guard++ < 10) e.Act(PlayerAction.Raise, 100000);
            Assert.IsTrue(sys.Use(0, item, UseRequest.None).Ok);
            Assert.AreEqual(2, e.RunCount);
            sys.Give(1, "runit", Tier.Legendary);
            Assert.IsTrue(sys.Use(1, sys.Inventory(1).Items[0], UseRequest.None).Ok);
            Assert.AreEqual(3, e.RunCount);
            e.ContinueRunout();
            Assert.IsTrue(e.HandOver);
        }

        // ---------------------------------------------------------------- the pineapple
        [Test]
        public void Pineapple_Rare_ThirdCard_ThenAHumanMustDiscardBeforeTheFlop()
        {
            var e = V.NewEngine(2); e.StartHand();
            var sys = new PowerUpSystem(e); sys.Give(0, "pineapple", Tier.Rare);
            Assert.IsTrue(sys.Use(0, sys.Inventory(0).Items[0], UseRequest.None).Ok);
            Assert.AreEqual(3, e.Players[0].Hole.Count);
            int guard = 0;
            while (!e.AwaitingDiscard && !e.HandOver && guard++ < 20) e.Act(PlayerAction.CheckCall);
            Assert.IsTrue(e.AwaitingDiscard);
            Assert.AreEqual(Street.Preflop, e.Street);
            Assert.AreEqual(ActResult.NoActiveHand, e.TryAct(0, PlayerAction.CheckCall, 0));
            var kept = new[] { e.Players[0].Hole[0], e.Players[0].Hole[1] };
            e.DiscardHole(0, 2);
            Assert.IsFalse(e.AwaitingDiscard);
            CollectionAssert.AreEqual(kept, e.Players[0].Hole);
            Assert.AreEqual(Street.Flop, e.Street);
            Assert.AreEqual(3, e.Community.Count);
            V.Finish(e);
        }

        [Test]
        public void Pineapple_Epic_DiscardIsDueAfterTheFlopBetting_Mythic_AfterTheTurn()
        {
            foreach (var (tier, street, board) in new[] { (Tier.Epic, Street.Flop, 3), (Tier.Mythic, Street.Turn, 4) })
            {
                var e = V.NewEngine(2); e.StartHand();
                var sys = new PowerUpSystem(e); sys.Give(0, "pineapple", tier);
                Assert.IsTrue(sys.Use(0, sys.Inventory(0).Items[0], UseRequest.None).Ok);
                int guard = 0;
                while (!e.AwaitingDiscard && !e.HandOver && guard++ < 40) e.Act(PlayerAction.CheckCall);
                Assert.IsTrue(e.AwaitingDiscard, tier.ToString());
                Assert.AreEqual(street, e.Street);
                Assert.AreEqual(board, e.Community.Count);   // the player has seen these cards before choosing
                e.DiscardHole(0, 0);
                Assert.AreEqual(2, e.Players[0].Hole.Count);
                V.Finish(e);
            }
        }

        [Test]
        public void Pineapple_ForABot_DiscardsByItself()
        {
            var e = V.NewEngine(2); e.StartHand();
            var sys = new PowerUpSystem(e); sys.Give(1, "pineapple", Tier.Rare);
            Assert.IsTrue(sys.Use(1, sys.Inventory(1).Items[0], UseRequest.None).Ok);
            int guard = 0;
            while (e.Street == Street.Preflop && !e.HandOver && guard++ < 20) e.Act(PlayerAction.CheckCall);
            Assert.IsFalse(e.AwaitingDiscard);
            Assert.AreEqual(2, e.Players[1].Hole.Count);
            Assert.AreEqual(0, e.PendingDiscards.Count);
            V.Finish(e);
        }

        [Test]
        public void Pineapple_FoldingCancelsTheDiscard_AndItCanOnlyBeUsedPreflop()
        {
            var e = V.NewEngine(2); e.StartHand();
            var sys = new PowerUpSystem(e); sys.Give(0, "pineapple", Tier.Epic);
            Assert.IsTrue(sys.Use(0, sys.Inventory(0).Items[0], UseRequest.None).Ok);
            int guard = 0;
            while (e.Current != 0 && guard++ < 10) e.Act(PlayerAction.CheckCall);
            e.Act(PlayerAction.Fold);
            Assert.IsFalse(e.PendingDiscards.ContainsKey(0));
            V.Finish(e);

            var e2 = V.NewEngine(2); e2.StartHand(); V.ToBoard(e2, 3);
            var sys2 = new PowerUpSystem(e2); sys2.Give(0, "pineapple", Tier.Rare);
            Assert.IsFalse(sys2.Use(0, sys2.Inventory(0).Items[0], UseRequest.None).Ok);
        }

        // ---------------------------------------------------------------- everything together
        [Test]
        public void ManyHands_WithRandomRules_AlwaysFinish_AndNoChipIsLost()
        {
            var rng = new Random(5);
            var e = new GameEngine { NextInt = TestUtil.Seeded(21) };
            e.NewGame(4);
            e.PauseBeforeRunout = true;
            int total = V.Chips(e);
            for (int hand = 0; hand < 120 && !e.IsGameOver; hand++)
            {
                e.NextRules.BombPot = rng.Next(5) == 0;
                e.NextRules.Joker = rng.Next(5) == 0;
                e.NextRules.WildTwos = rng.Next(6) == 0;
                e.NextRules.Cross = rng.Next(5) == 0;
                if (rng.Next(4) == 0) e.NextRules.ShanKoeMee.Add(rng.Next(e.Players.Count));
                e.StartHand();
                if (rng.Next(3) == 0 && e.Street == Street.Preflop)
                {
                    int s = rng.Next(e.Players.Count);
                    if (!e.Players[s].Folded && e.Players[s].Hole.Count == 2)
                        e.StartPineapple(s, new[] { Street.Preflop, Street.Flop, Street.Turn }[rng.Next(3)]);
                }
                int guard = 0;
                while (!e.HandOver && guard++ < 3000)
                {
                    if (e.AwaitingDiscard)
                    {
                        int seat = e.PendingDiscards.First(kv => kv.Value == e.Street && !e.Players[kv.Key].Folded).Key;
                        e.AutoDiscard(seat);
                        continue;
                    }
                    if (e.RunoutPending)
                    {
                        var allIn = e.Players.Select((p, i) => (p, i)).Where(x => !x.p.Folded && x.p.AllIn).ToList();
                        if (allIn.Count > 0 && rng.Next(2) == 0) e.RequestRuns(allIn[0].i, 2 + rng.Next(2));
                        e.ContinueRunout();
                        continue;
                    }
                    var pl = e.Players[e.Current];
                    var (action, amount) = Bot.Decide(e, pl, rng);
                    e.Act(action, amount);
                }
                Assert.IsTrue(e.HandOver, "hand " + hand + " did not finish");
                Assert.AreEqual(total, V.Chips(e), "chips changed in hand " + hand);
            }
        }
    }
}
