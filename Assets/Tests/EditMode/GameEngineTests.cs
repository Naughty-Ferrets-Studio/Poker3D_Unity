using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Holdem.Tests
{
    public class GameEngineTests
    {
        /// <summary>A started hand: you (seat 0) plus botCount bots. The button is seat 0 on the first hand.</summary>
        private static GameEngine Start(int botCount, Deck deck = null, int seed = 1)
        {
            var e = new GameEngine();
            e.NewGame(botCount);
            e.NextInt = TestUtil.Seeded(seed);
            if (deck != null) e.NextDeckOverride = deck;
            e.StartHand();
            return e;
        }

        private static void CheckDown(GameEngine e)
        {
            int guard = 0;
            while (!e.HandOver && guard++ < 200) e.Act(PlayerAction.CheckCall);
            Assert.IsTrue(e.HandOver, "the hand did not finish");
        }

        private static int TotalChips(GameEngine e) => e.Players.Sum(p => p.Chips);

        // ------------------------------------------------------------ start of a hand
        [Test]
        public void StartHand_PostsBlindsAndDealsTwoCardsToEverybody()
        {
            GameEngine e = Start(2);

            Assert.AreEqual(3, e.Players.Count);
            Assert.IsFalse(e.HandOver);
            Assert.AreEqual(Street.Preflop, e.Street);
            Assert.AreEqual(GameEngine.SmallBlind + GameEngine.BigBlind, e.Pot);
            foreach (Player p in e.Players) Assert.AreEqual(2, p.Hole.Count);
            Assert.AreEqual(0, e.Dealer);
            Assert.AreEqual(0, e.Current, "with 3 players the button acts first preflop");
            Assert.AreEqual(6, e.DealEvents.Count);
        }

        [Test]
        public void StartHand_WithAReplayDeck_DealsExactlyThoseCards()
        {
            List<Card> order = TestUtil.OrderedDeck();
            GameEngine e = Start(2, new Deck(order));

            // deal order is seats 1, 2, 0, 1, 2, 0; the top of the deck is the last card of 'order'
            Assert.AreEqual(order[51], e.Players[1].Hole[0]);
            Assert.AreEqual(order[50], e.Players[2].Hole[0]);
            Assert.AreEqual(order[49], e.Players[0].Hole[0]);
            Assert.AreEqual(order[48], e.Players[1].Hole[1]);
            Assert.AreEqual(order[47], e.Players[2].Hole[1]);
            Assert.AreEqual(order[46], e.Players[0].Hole[1]);
            CollectionAssert.AreEqual(order, e.DeckOrder);
        }

        // ------------------------------------------------------------ TryAct
        [Test]
        public void TryAct_RejectsMovesOutOfTurnAndIllegalMoves()
        {
            GameEngine e = Start(2);
            Assert.AreEqual(ActResult.NotYourTurn, e.TryAct(1, PlayerAction.CheckCall));
            Assert.AreEqual(ActResult.IllegalRaise, e.TryAct(0, PlayerAction.Raise, 5));
            Assert.AreEqual(ActResult.IllegalRaise, e.TryAct(0, PlayerAction.Raise, 99999));
            Assert.AreEqual(ActResult.InvalidAction, e.TryAct(0, (PlayerAction)99));
            Assert.AreEqual(ActResult.Ok, e.TryAct(0, PlayerAction.Raise, 60));
            Assert.AreEqual(60, e.CurrentBet);
        }

        [Test]
        public void TryAct_AfterTheHandIsOver_SaysNoActiveHand()
        {
            GameEngine e = Start(2);
            e.Act(PlayerAction.Fold);
            e.Act(PlayerAction.Fold);
            Assert.IsTrue(e.HandOver);
            Assert.AreEqual(ActResult.NoActiveHand, e.TryAct(0, PlayerAction.Fold));
        }

        // ------------------------------------------------------------ ending a hand
        [Test]
        public void EveryoneFolds_TheLastPlayerWinsTheBlinds()
        {
            GameEngine e = Start(2);   // seat 0 = button, seat 1 = small blind, seat 2 = big blind
            e.Act(PlayerAction.Fold);  // seat 0
            e.Act(PlayerAction.Fold);  // seat 1

            Assert.IsTrue(e.HandOver);
            Assert.IsFalse(e.ShowdownReached);
            Assert.AreEqual(1000, e.Players[0].Chips);
            Assert.AreEqual(990, e.Players[1].Chips);
            Assert.AreEqual(1010, e.Players[2].Chips);
            Assert.AreEqual(3000, TotalChips(e));
        }

        [Test]
        public void CheckDown_ReachesShowdown_AndAllChipsAreStillThere()
        {
            GameEngine e = Start(2);
            CheckDown(e);
            Assert.IsTrue(e.ShowdownReached);
            Assert.AreEqual(Street.Showdown, e.Street);
            Assert.AreEqual(5, e.Community.Count);
            Assert.AreEqual(3000, TotalChips(e));
        }

        [Test]
        public void Showdown_PaysTheBestHand()
        {
            // top of the deck, first card first: seat 1 gets As + Ah, seat 2 gets 2c + 3d, seat 0 gets 7d + 8c,
            // then burn 3s, flop Kd 9h 4c, burn 5h, turn Jd, burn 6h, river 2h
            Deck deck = TestUtil.DeckWithTop("As", "2c", "7d", "Ah", "3d", "8c",
                                             "3s", "Kd", "9h", "4c",
                                             "5h", "Jd",
                                             "6h", "2h");
            GameEngine e = Start(2, deck);
            CheckDown(e);

            Assert.IsTrue(e.ShowdownReached);
            Assert.AreEqual(1040, e.Players[1].Chips); // pair of aces wins the 60 chip pot (each player put in 20)
            Assert.AreEqual(980, e.Players[0].Chips);
            Assert.AreEqual(980, e.Players[2].Chips);
        }

        [Test]
        public void ManyBotHands_NeverLoseOrCreateChips()
        {
            var e = new GameEngine();
            e.NewGame(5);
            e.NextInt = TestUtil.Seeded(7);
            var rng = new Random(11);
            int total = 6 * GameEngine.StartingChips;

            for (int hand = 0; hand < 60 && !e.IsGameOver; hand++)
            {
                e.StartHand();
                int guard = 0;
                while (!e.HandOver && guard++ < 500)
                {
                    Player p = e.Players[e.Current];
                    (PlayerAction action, int amount) = Bot.Decide(e, p, rng);
                    e.Act(action, amount);
                }
                Assert.IsTrue(e.HandOver, "hand " + hand + " did not finish");
                Assert.AreEqual(total, TotalChips(e), "chips changed during hand " + hand);
            }
        }

        // ------------------------------------------------------------ power-up card actions
        [Test]
        public void ReplaceHoleCard_SwapsTheCard_AndTheHandStillFinishesCleanly()
        {
            List<Card> order = TestUtil.OrderedDeck();
            GameEngine e = Start(2, new Deck(order));
            Card old = e.Players[1].Hole[0];

            Card fresh = e.ReplaceHoleCard(1, 0);

            Assert.AreEqual(order[45], fresh);                 // the top card of the deck
            Assert.AreEqual(fresh, e.Players[1].Hole[0]);
            Assert.AreEqual(2, e.Players[1].Hole.Count);
            Assert.AreNotEqual(old, fresh);
            DealEvent last = e.DealEvents[e.DealEvents.Count - 1];
            Assert.AreEqual(DealKind.Replace, last.Kind);
            Assert.AreEqual(old, last.Previous.Value);

            CheckDown(e); // EndHand runs the 52-card check; it would throw if the swap lost or doubled a card
            Assert.AreEqual(3000, TotalChips(e));
        }

        [Test]
        public void ReplaceHoleCard_CanTakeASpecificCard()
        {
            List<Card> order = TestUtil.OrderedDeck();
            GameEngine e = Start(2, new Deck(order));
            Card fresh = e.ReplaceHoleCard(0, 1, order[3]);
            Assert.AreEqual(order[3], fresh);
            Assert.AreEqual(order[3], e.Players[0].Hole[1]);
            CheckDown(e);
        }

        [Test]
        public void ReplaceBoardCard_SwapsOneCommunityCard()
        {
            GameEngine e = Start(2);
            int guard = 0;
            while (e.Community.Count < 3 && !e.HandOver && guard++ < 50) e.Act(PlayerAction.CheckCall);
            Assert.AreEqual(3, e.Community.Count);

            Card old = e.Community[1];
            Card fresh = e.ReplaceBoardCard(1);
            Assert.AreNotEqual(old, fresh);
            Assert.AreEqual(fresh, e.Community[1]);

            CheckDown(e);
            var allCards = e.Community.Concat(e.Players.SelectMany(p => p.Hole)).ToList();
            Assert.AreEqual(11, allCards.Count);
            Assert.AreEqual(11, allCards.Distinct().Count(), "a card is on the table twice");
        }

        [Test]
        public void CardActions_ThrowWhenTheyCannotBeUsed()
        {
            GameEngine e = Start(2);
            e.Act(PlayerAction.Fold); // seat 0 folds
            Assert.Throws<InvalidOperationException>(() => e.ReplaceHoleCard(0, 0));   // folded player
            Assert.Throws<ArgumentOutOfRangeException>(() => e.ReplaceHoleCard(1, 2)); // no third hole card
            Assert.Throws<ArgumentOutOfRangeException>(() => e.ReplaceBoardCard(0));   // no board yet

            e.Act(PlayerAction.Fold);
            Assert.IsTrue(e.HandOver);
            Assert.Throws<InvalidOperationException>(() => e.ReplaceHoleCard(2, 0));   // hand is over
            Assert.Throws<InvalidOperationException>(() => e.PeekDeck(2));
        }

        [Test]
        public void PeekDeck_DoesNotUseUpTheCard()
        {
            List<Card> order = TestUtil.OrderedDeck();
            GameEngine e = Start(2, new Deck(order));

            Card seen = e.PeekDeck(0);
            Assert.AreEqual(order[45], seen);

            int guard = 0;
            while (e.Community.Count < 3 && !e.HandOver && guard++ < 50) e.Act(PlayerAction.CheckCall);
            // the peeked card was burned, then the flop is the next three cards
            CollectionAssert.AreEqual(new[] { order[44], order[43], order[42] }, e.Community);
        }

        [Test]
        public void RevealHoleCard_RecordsAnEvent_AndReturnsTheCard()
        {
            GameEngine e = Start(2);
            Card c = e.RevealHoleCard(1, 0, 0);
            Assert.AreEqual(e.Players[1].Hole[0], c);

            DealEvent last = e.DealEvents[e.DealEvents.Count - 1];
            Assert.AreEqual(DealKind.Reveal, last.Kind);
            Assert.AreEqual(1, last.Seat);
            Assert.AreEqual(0, last.Viewer);
        }

        // ------------------------------------------------------------ bots
        [Test]
        public void Equity_PocketAcesBeatOneRandomHandAboutEightyFivePercent()
        {
            double eq = Bot.Equity(TestUtil.Cards("As Ah"), new List<Card>(), 1, 5000, new Random(5));
            Assert.That(eq, Is.InRange(0.82, 0.88));
        }
    }
}
