using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Holdem.Tests
{
    public class CardDealerTests
    {
        private List<Card> _order;
        private Deck _deck;
        private CardDealer _dealer;
        private Dictionary<int, List<Card>> _hole;

        /// <summary>3 seats, button on seat 0, fixed deck. Deal order is seats 1, 2, 0, 1, 2, 0 (top of the deck = last card of _order).</summary>
        [SetUp]
        public void SetUp()
        {
            _order = TestUtil.OrderedDeck();
            _deck = new Deck(_order);
            _dealer = new CardDealer();
            _dealer.ReplayHand(_deck);
            _hole = new Dictionary<int, List<Card>> { { 0, new List<Card>() }, { 1, new List<Card>() }, { 2, new List<Card>() } };
        }

        private void DealHoleCards() =>
            _dealer.DealHoleCards(0, 3, seat => true, (seat, card) => _hole[seat].Add(card));

        // ------------------------------------------------------------ normal dealing
        [Test]
        public void HoleCards_AreDealtOneAtATime_StartingLeftOfTheButton()
        {
            DealHoleCards();

            int[] expectedSeats = { 1, 2, 0, 1, 2, 0 };
            Assert.AreEqual(6, _dealer.Events.Count);
            for (int i = 0; i < 6; i++)
            {
                Assert.AreEqual(DealKind.Hole, _dealer.Events[i].Kind);
                Assert.AreEqual(expectedSeats[i], _dealer.Events[i].Seat, "seat of card " + i);
                Assert.AreEqual(_order[51 - i], _dealer.Events[i].Card, "card " + i);
            }
            Assert.AreEqual(_order[51], _hole[1][0]);
            Assert.AreEqual(_order[48], _hole[1][1]);
            _dealer.VerifyIntegrity();
        }

        [Test]
        public void HoleCards_SkipSeatsThatAreNotLive()
        {
            _dealer.DealHoleCards(0, 3, seat => seat != 2, (seat, card) => _hole[seat].Add(card));
            Assert.AreEqual(4, _dealer.Events.Count);
            Assert.AreEqual(0, _hole[2].Count);
            Assert.AreEqual(2, _hole[0].Count);
            Assert.AreEqual(2, _hole[1].Count);
        }

        [Test]
        public void Flop_BurnsOneCardThenDealsThree()
        {
            DealHoleCards();
            IReadOnlyList<Card> flop = _dealer.DealBoard(Street.Flop);

            Assert.AreEqual(3, flop.Count);
            Assert.AreEqual(1, _dealer.Burned.Count);
            Assert.AreEqual(_order[45], _dealer.Burned[0]);
            CollectionAssert.AreEqual(new[] { _order[44], _order[43], _order[42] }, flop);

            var kinds = _dealer.Events.Skip(6).Select(e => e.Kind).ToArray();
            CollectionAssert.AreEqual(new[] { DealKind.Burn, DealKind.Flop, DealKind.Flop, DealKind.Flop }, kinds);
            _dealer.VerifyIntegrity();
        }

        [Test]
        public void DealBoard_RejectsStreetsWithoutBoardCards()
        {
            Assert.Throws<ArgumentException>(() => _dealer.DealBoard(Street.Preflop));
            Assert.Throws<ArgumentException>(() => _dealer.DealBoard(Street.Showdown));
        }

        // ------------------------------------------------------------ replace
        [Test]
        public void Replace_PutsTheOldCardBackInTheDeckAndTakesTheTopCard()
        {
            DealHoleCards();
            Card old = _hole[1][0];                          // order[51]
            Card fresh = _dealer.Replace(1, old, null, n => 0); // old goes to the very bottom

            Assert.AreEqual(_order[45], fresh);              // the top card of the deck
            Assert.AreNotEqual(old, fresh);
            Assert.IsTrue(_deck.Contains(old));
            Assert.AreEqual(old, _deck.RemainingCards[0]);
            Assert.AreEqual(46 - 1 + 1, _deck.Remaining);    // one out, one in
            _dealer.VerifyIntegrity();
        }

        [Test]
        public void Replace_RecordsAReplaceEvent()
        {
            DealHoleCards();
            Card old = _hole[2][1];
            Card fresh = _dealer.Replace(2, old);

            DealEvent last = _dealer.Events[_dealer.Events.Count - 1];
            Assert.AreEqual(DealKind.Replace, last.Kind);
            Assert.AreEqual(2, last.Seat);
            Assert.AreEqual(fresh, last.Card);
            Assert.AreEqual(old, last.Previous.Value);
        }

        [Test]
        public void Replace_CanTakeASpecificCardThatIsStillInTheDeck()
        {
            DealHoleCards();
            Card wanted = _order[10];
            Card fresh = _dealer.Replace(0, _hole[0][0], wanted, n => 0);

            Assert.AreEqual(wanted, fresh);
            Assert.IsFalse(_deck.Contains(wanted));
            _dealer.VerifyIntegrity();
        }

        [Test]
        public void Replace_WithACardThatIsNotInTheDeck_ThrowsAndChangesNothing()
        {
            DealHoleCards();
            int events = _dealer.Events.Count;
            int remaining = _deck.Remaining;

            // _hole[2][0] is on the table, so it cannot be the replacement
            Assert.Throws<InvalidOperationException>(() => _dealer.Replace(1, _hole[1][0], _hole[2][0]));

            Assert.AreEqual(events, _dealer.Events.Count);
            Assert.AreEqual(remaining, _deck.Remaining);
            _dealer.VerifyIntegrity();
        }

        [Test]
        public void Replace_RejectsACardThatIsNotOnTheTable()
        {
            DealHoleCards();
            Assert.Throws<InvalidOperationException>(() => _dealer.Replace(1, _order[0])); // still in the deck
        }

        [Test]
        public void Replace_RejectsABurnedCard()
        {
            DealHoleCards();
            _dealer.DealBoard(Street.Flop);
            Assert.Throws<InvalidOperationException>(() => _dealer.Replace(-1, _dealer.Burned[0]));
        }

        [Test]
        public void Replace_BoardCard_AndKeepDealingKeepsEveryCardUnique()
        {
            DealHoleCards();
            IReadOnlyList<Card> flop = _dealer.DealBoard(Street.Flop);
            Card fresh = _dealer.Replace(-1, flop[0], null, TestUtil.Seeded(3));
            var board = new List<Card> { fresh, flop[1], flop[2] };
            board.AddRange(_dealer.DealBoard(Street.Turn));
            board.AddRange(_dealer.DealBoard(Street.River));

            var everything = new List<Card>(board);
            everything.AddRange(_hole.Values.SelectMany(h => h));
            Assert.AreEqual(everything.Count, everything.Distinct().Count(), "a card showed up twice");
            _dealer.VerifyIntegrity();
        }

        // ------------------------------------------------------------ peek and reveal
        [Test]
        public void Peek_ShowsTheTopCardWithoutDrawingIt()
        {
            DealHoleCards();
            Card seen = _dealer.PeekDeck(2);
            Assert.AreEqual(_order[45], seen);
            Assert.AreEqual(46, _deck.Remaining);

            DealEvent last = _dealer.Events[_dealer.Events.Count - 1];
            Assert.AreEqual(DealKind.Peek, last.Kind);
            Assert.AreEqual(2, last.Viewer);

            _dealer.DealBoard(Street.Flop);
            Assert.AreEqual(seen, _dealer.Burned[0], "the peeked card should be the next card dealt (the burn card)");
        }

        [Test]
        public void Peek_CanLookDeeperIntoTheDeck()
        {
            DealHoleCards();
            Assert.AreEqual(_order[43], _dealer.PeekDeck(0, 2));
        }

        [Test]
        public void Reveal_RecordsWhoSeesTheCard()
        {
            DealHoleCards();
            Card c = _hole[1][0];
            _dealer.Reveal(1, c, 0);
            _dealer.Reveal(1, c); // everybody

            DealEvent a = _dealer.Events[_dealer.Events.Count - 2];
            DealEvent b = _dealer.Events[_dealer.Events.Count - 1];
            Assert.AreEqual(DealKind.Reveal, a.Kind);
            Assert.AreEqual(0, a.Viewer);
            Assert.AreEqual(-1, b.Viewer);
            Assert.AreEqual(c, b.Card);
        }

        [Test]
        public void Reveal_RejectsACardThatIsStillInTheDeck()
        {
            DealHoleCards();
            Assert.Throws<InvalidOperationException>(() => _dealer.Reveal(1, _order[0]));
        }

        // ------------------------------------------------------------ bookkeeping
        [Test]
        public void ReplayHand_ClearsThePreviousHand()
        {
            DealHoleCards();
            _dealer.ReplayHand(new Deck(TestUtil.OrderedDeck()));
            Assert.AreEqual(0, _dealer.Events.Count);
            Assert.AreEqual(0, _dealer.Burned.Count);
            _dealer.VerifyIntegrity(); // nothing is on the table, the new deck is complete
        }

        [Test]
        public void DeckOrder_IsTheOrderRightAfterTheShuffle()
        {
            CollectionAssert.AreEqual(_order, _dealer.DeckOrder);
        }
    }
}
