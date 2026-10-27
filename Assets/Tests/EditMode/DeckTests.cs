using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Holdem.Tests
{
    public class DeckTests
    {
        [Test]
        public void NewDeck_Has52UniqueCards()
        {
            var deck = new Deck();
            Assert.AreEqual(52, deck.Remaining);
            Assert.AreEqual(52, deck.RemainingCards.Distinct().Count());
            Assert.AreEqual(52, deck.InitialOrder.Count);
        }

        [Test]
        public void SameSeed_GivesSameShuffle_DifferentSeed_Differs()
        {
            var a = new Deck(TestUtil.Seeded(42));
            var b = new Deck(TestUtil.Seeded(42));
            var c = new Deck(TestUtil.Seeded(43));
            CollectionAssert.AreEqual(a.InitialOrder, b.InitialOrder);
            CollectionAssert.AreNotEqual(a.InitialOrder, c.InitialOrder);
        }

        [Test]
        public void Shuffle_RejectsAnRngThatReturnsOutOfRangeNumbers()
        {
            Assert.Throws<InvalidOperationException>(() => new Deck(n => n));
            Assert.Throws<InvalidOperationException>(() => new Deck(n => -1));
        }

        [Test]
        public void ExactOrder_DrawsFromTheEndOfTheList()
        {
            List<Card> order = TestUtil.OrderedDeck();
            var deck = new Deck(order);
            Assert.AreEqual(order[51], deck.Draw());
            Assert.AreEqual(order[50], deck.Draw());
            Assert.AreEqual(50, deck.Remaining);
        }

        [Test]
        public void ExactOrder_RejectsWrongSizeAndDuplicates()
        {
            List<Card> order = TestUtil.OrderedDeck();

            Assert.Throws<ArgumentException>(() => new Deck(order.Take(51)));

            var dup = new List<Card>(order);
            dup[51] = dup[0];
            Assert.Throws<ArgumentException>(() => new Deck(dup));
        }

        [Test]
        public void Peek_ShowsACardWithoutRemovingIt()
        {
            List<Card> order = TestUtil.OrderedDeck();
            var deck = new Deck(order);
            Assert.AreEqual(order[51], deck.Peek());
            Assert.AreEqual(order[49], deck.Peek(2));
            Assert.AreEqual(52, deck.Remaining);
            Assert.Throws<ArgumentOutOfRangeException>(() => deck.Peek(52));
            Assert.Throws<ArgumentOutOfRangeException>(() => deck.Peek(-1));
        }

        [Test]
        public void Draw_FromAnEmptyDeckThrows()
        {
            var deck = new Deck(TestUtil.OrderedDeck());
            for (int i = 0; i < 52; i++) deck.Draw();
            Assert.Throws<InvalidOperationException>(() => deck.Draw());
        }

        [Test]
        public void Take_RemovesThatExactCard_AndThrowsIfItIsNotThere()
        {
            List<Card> order = TestUtil.OrderedDeck();
            var deck = new Deck(order);
            Card c = order[10];
            Assert.AreEqual(c, deck.Take(c));
            Assert.IsFalse(deck.Contains(c));
            Assert.AreEqual(51, deck.Remaining);
            Assert.Throws<InvalidOperationException>(() => deck.Take(c));
        }

        [Test]
        public void Insert_PutsACardAtTheChosenDepth()
        {
            List<Card> order = TestUtil.OrderedDeck();
            var deck = new Deck(order);
            Card c = deck.Draw();                 // the top card
            deck.Insert(0, c);                    // back at the bottom
            Assert.AreEqual(c, deck.RemainingCards[0]);
            Assert.AreEqual(52, deck.Remaining);

            Card d = deck.Draw();
            deck.Insert(deck.Remaining, d);       // back on top
            Assert.AreEqual(d, deck.Peek());
        }

        [Test]
        public void Insert_RejectsDuplicatesAndBadIndexes()
        {
            var deck = new Deck(TestUtil.OrderedDeck());
            Card c = deck.Peek();
            Assert.Throws<InvalidOperationException>(() => deck.Insert(0, c)); // already in the deck

            Card drawn = deck.Draw();
            Assert.Throws<ArgumentOutOfRangeException>(() => deck.Insert(-1, drawn));
            Assert.Throws<ArgumentOutOfRangeException>(() => deck.Insert(deck.Remaining + 1, drawn));
        }

        [Test]
        public void InsertRandom_UsesTheRngResultAsTheDepth_AndChecksItsRange()
        {
            var deck = new Deck(TestUtil.OrderedDeck());
            Card c = deck.Draw();
            deck.InsertRandom(c, n => 0);
            Assert.AreEqual(c, deck.RemainingCards[0]);

            Card d = deck.Draw();
            Assert.Throws<InvalidOperationException>(() => deck.InsertRandom(d, n => n + 1));
            Assert.Throws<InvalidOperationException>(() => deck.InsertRandom(d, n => -1));
        }
    }
}
