using Holdem;

namespace Holdem.PowerUps
{
    /// <summary>
    /// The Dice. Roll a die to redraw one community card, only when all five are on the table.
    ///   1-3: flop card 1-3.  4: turn.  5: river.  6: your choice (req.CardIndex 0-4, board order).
    /// No tier changes yet (the design has no variants); every tier works the same.
    /// </summary>
    public sealed class Dice : PowerUpDefinition
    {
        public override string Id => "dice";
        public override string Name => "The Dice";

        public override string Describe(Tier tier) =>
            "Roll a die to redraw a community card (1-3 flop, 4 turn, 5 river, 6 your choice). Needs all 5 community cards.";

        public override string CanUse(PowerUpContext ctx)
        {
            string why = NeedHand(ctx);
            if (why != null) return why;
            int have = ctx.Engine.Community.Count;
            if (ctx.IgnoreTiming ? have == 0 : have < 5) return "The Dice need all 5 community cards on the table.";
            return null;
        }

        public override UseResult Use(PowerUpContext ctx, UseRequest req)
        {
            int roll = req.ForcedRoll ?? ctx.NextInt(6) + 1;
            if (roll < 1 || roll > 6) return UseResult.Fail("A die shows 1 to 6.");

            int idx = roll <= 3 ? roll - 1 : roll == 4 ? 3 : roll == 5 ? 4 : req.CardIndex;
            if (idx < 0 || idx >= ctx.Engine.Community.Count)
                return UseResult.Fail("Rolled " + roll + ": there is no community card there. Not spent.");

            var old = ctx.Engine.Community[idx];
            var fresh = ctx.Engine.ReplaceBoardCard(idx);
            return UseResult.Success("Rolled " + roll + ": " + old + " is replaced by " + fresh + ".");
        }
    }
}
