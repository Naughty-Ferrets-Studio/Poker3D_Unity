namespace Holdem.PowerUps
{
    /// <summary>
    /// The Cross (Legendary). Used between hands. The next hand has nine community cards laid out as a cross: a horizontal
    /// line of 5 and a vertical line of 5 that share the centre card. Your hand is your 2 hole cards plus the horizontal
    /// line OR the vertical line, never a mix. The flop deals the horizontal line (5), the turn the two upper vertical
    /// cards, the river the two lower ones.
    /// </summary>
    public sealed class TheCross : PowerUpDefinition
    {
        public override string Id => "cross";
        public override string Name => "The Cross";
        public override Tier[] Tiers => new[] { Tier.Legendary };
        public override UseWindow Window => UseWindow.BetweenHands;

        public override string Describe(Tier tier) =>
            "Next hand has 9 community cards in a cross (5 across, 5 down, sharing the middle). Use your cards plus one line only.";

        public override string CanUse(PowerUpContext ctx) =>
            ctx.Engine.NextRules.Cross ? "The Cross is already set for the next hand." : null;

        public override UseResult Use(PowerUpContext ctx, UseRequest req)
        {
            ctx.Engine.NextRules.Cross = true;
            ctx.Engine.Announce("*** " + ctx.Me.Name + " lays out THE CROSS for the next hand! ***");
            return UseResult.Success("The next hand uses the Cross board.");
        }
    }
}
