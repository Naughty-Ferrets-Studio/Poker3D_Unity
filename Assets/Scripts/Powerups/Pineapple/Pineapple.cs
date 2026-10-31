namespace Holdem.PowerUps
{
    /// <summary>
    /// The Pineapple. Used preflop, before betting is done: draw a third hole card, then discard one (face down) so you
    /// keep two. You choose which to keep.
    ///   Pineapple I   (Rare):  discard before the flop.
    ///   Pineapple II  (Epic):  discard after the flop betting round (you see the flop first).
    ///   Pineapple III (Mythic): discard after the turn betting round (you see flop and turn first).
    /// The discard itself is done with GameEngine.DiscardHole(); the hand waits for a human to do it.
    /// </summary>
    public sealed class Pineapple : PowerUpDefinition
    {
        public override string Id => "pineapple";
        public override string Name => "The Pineapple";
        public override Tier[] Tiers => new[] { Tier.Rare, Tier.Epic, Tier.Mythic };

        public override string Describe(Tier tier)
        {
            if (tier == Tier.Rare) return "Preflop: draw a third hole card, then discard one before the flop.";
            if (tier == Tier.Epic) return "Preflop: draw a third hole card, then discard one after the flop betting round.";
            return "Preflop: draw a third hole card, then discard one after the turn betting round.";
        }

        private static Street DueAfter(Tier t) => t == Tier.Rare ? Street.Preflop : t == Tier.Epic ? Street.Flop : Street.Turn;

        public override string CanUse(PowerUpContext ctx)
        {
            string why = NeedHand(ctx);
            if (why != null) return why;
            if (ctx.Engine.Street != Street.Preflop) return "The Pineapple can only be used before the flop.";
            if (ctx.Me.Hole.Count != 2) return "You need exactly two hole cards.";
            if (ctx.Engine.PendingDiscards.ContainsKey(ctx.Seat)) return "You already have a third card to discard.";
            return null;
        }

        public override UseResult Use(PowerUpContext ctx, UseRequest req)
        {
            var third = ctx.Engine.StartPineapple(ctx.Seat, DueAfter(ctx.Tier));
            string when = ctx.Tier == Tier.Rare ? "the flop" : ctx.Tier == Tier.Epic ? "the turn is dealt" : "the river is dealt";
            return UseResult.Success("You draw the " + third + ". Discard one card before " + when + ".");
        }
    }
}
