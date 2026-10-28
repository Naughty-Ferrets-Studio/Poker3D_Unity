using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Holdem.PowerUps;

namespace Holdem.Tests
{
    public class PowerUpScenarioTests
    {
        private static Card C(string s) { Assert.IsTrue(CardParser.TryParse(s, out Card c), s); return c; }
        private static List<Card> L(string s) { Assert.IsNull(CardParser.TryParseList(s, out var l)); return l; }

        private static GameEngine Engine(int bots = 3)
        {
            var e = new GameEngine { NextInt = TestUtil.Seeded(3) };
            e.NewGame(bots);
            return e;
        }

        private static void ToBoard(GameEngine e, int cards)
        {
            int guard = 0;
            while (!e.HandOver && e.Community.Count < cards && guard++ < 60) e.Act(PlayerAction.CheckCall);
        }

        [Test]
        public void Parser_AcceptsBothWritingStyles_AndRejectsNonsense()
        {
            Assert.AreEqual(2, L("As Kd").Count);
            Assert.AreEqual(3, L("7h,2c,Td").Count);
            Assert.AreEqual(2, L("AsKd").Count);
            Assert.AreEqual(0, L("   ").Count);
            Assert.IsNotNull(CardParser.TryParseList("1s", out _));
            Assert.IsNotNull(CardParser.TryParseList("As K", out _));
            Assert.IsNotNull(CardParser.TryParseList("Ax", out _));
        }

        [Test]
        public void RiggedHand_DealsTheExactHoleCardsAndBoard()
        {
            var e = Engine();
            var s = new Scenario { Board = L("2c 7d 9h Th 3s") };
            s.Hole[0] = L("As Kd");
            s.Hole[2] = L("Qc Qd");
            Assert.IsNull(ScenarioBuilder.Apply(e, s));
            e.StartHand();
            CollectionAssert.AreEqual(L("As Kd"), e.Players[0].Hole);
            CollectionAssert.AreEqual(L("Qc Qd"), e.Players[2].Hole);
            ToBoard(e, 5);
            CollectionAssert.AreEqual(L("2c 7d 9h Th 3s"), e.Community);
        }

        [Test]
        public void RiggedHand_StillWorksOnTheSecondHand_WhenTheButtonHasMoved()
        {
            var e = Engine();
            e.StartHand();
            int guard = 0; while (!e.HandOver && guard++ < 80) e.Act(PlayerAction.CheckCall);
            var s = new Scenario(); s.Hole[1] = L("Ah Ad");
            Assert.IsNull(ScenarioBuilder.Apply(e, s));
            e.StartHand();
            CollectionAssert.AreEqual(L("Ah Ad"), e.Players[1].Hole);
        }

        [Test]
        public void RiggedHand_CanBeFinished_AndTheFiftyTwoCardCheckStillPasses()
        {
            var e = Engine();
            var s = new Scenario { Board = L("2c 7d 9h Th 3s") };
            s.Hole[0] = L("As Kd");
            ScenarioBuilder.Apply(e, s);
            e.StartHand();
            int guard = 0; while (!e.HandOver && guard++ < 80) e.Act(PlayerAction.CheckCall);
            Assert.IsTrue(e.HandOver);
        }

        [Test]
        public void PartialSpec_FillsTheRestRandomly_WithoutDuplicates()
        {
            var e = Engine();
            var s = new Scenario { Board = L("2c 7d") };
            s.Hole[0] = L("As");
            ScenarioBuilder.Apply(e, s);
            e.StartHand();
            ToBoard(e, 5);
            Assert.AreEqual(C("As"), e.Players[0].Hole[0]);
            Assert.AreEqual(C("2c"), e.Community[0]);
            Assert.AreEqual(C("7d"), e.Community[1]);
            var all = e.Players.SelectMany(p => p.Hole).Concat(e.Community).ToList();
            Assert.AreEqual(all.Count, all.Distinct().Count());
        }

        [Test]
        public void BadScenarios_AreRefusedWithAMessage()
        {
            var e = Engine();
            var dup = new Scenario(); dup.Hole[0] = L("As Kd"); dup.Board = L("As 2c 3d");
            Assert.IsNotNull(ScenarioBuilder.Apply(e, dup));
            var three = new Scenario(); three.Hole[0] = L("As Kd Qc");
            Assert.IsNotNull(ScenarioBuilder.Apply(e, three));
            var sixBoard = new Scenario { Board = L("2c 3c 4c 5c 6c 7c") };
            Assert.IsNotNull(ScenarioBuilder.Apply(e, sixBoard));
            Assert.IsNull(e.NextDeckOverride);
        }

        [Test]
        public void ScenarioPlusPowerUp_StartsAtTheRiverWithAKnownBoard()
        {
            // the point of all this: Dice test with a board we chose, no luck involved
            var e = Engine();
            var s = new Scenario { Board = L("2c 7d 9h Th 3s") };
            ScenarioBuilder.Apply(e, s);
            e.StartHand();
            ToBoard(e, 5);
            var sys = new PowerUpSystem(e);
            int me = e.Current;
            sys.Give(me, "dice", Tier.Legendary);
            var r = sys.Use(me, sys.Inventory(me).Items[0], new UseRequest { ForcedRoll = 1 });
            Assert.IsTrue(r.Ok, r.Message);
            Assert.AreNotEqual(C("2c"), e.Community[0]);
            Assert.AreEqual(C("7d"), e.Community[1]);
        }
    }
}
