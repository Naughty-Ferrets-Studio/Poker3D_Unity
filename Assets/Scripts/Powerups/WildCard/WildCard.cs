namespace Holdem.PowerUps
{
    /// <summary>
    /// Wild Card. Used between hands; wild cards are added to the next hand.
    ///   Joker (Rare):      one joker is shuffled into the deck.
    ///   Wild Twos (Mythic): all four 2s are wild.
    /// A wild card counts as any card its holder wants (never as a card already in the same five-card hand).
    /// </summary>
    public sealed class WildCard : PowerUpDefinition
    {
        public override string Id => "wild";
        public override string Name => "Wild Card";
        public override Tier[] Tiers => new[] { Tier.Rare, Tier.Mythic };
        public override UseWindow Window => UseWindow.BetweenHands;

        public override string VersionName(Tier t) => t == Tier.Rare ? "Joker" : "Wild Twos";

        public override string Describe(Tier tier) => tier == Tier.Rare
            ? "Shuffles one joker into the deck for the next hand. A joker is wild."
            : "All four 2s are wild in the next hand.";

        public override string CanUse(PowerUpContext ctx) =>
            (ctx.Tier == Tier.Rare ? ctx.Engine.NextRules.Joker : ctx.Engine.NextRules.WildTwos) ? "That is already set for the next hand." : null;

        public override UseResult Use(PowerUpContext ctx, UseRequest req)
        {
            if (ctx.Tier == Tier.Rare) ctx.Engine.NextRules.Joker = true; else ctx.Engine.NextRules.WildTwos = true;
            ctx.Engine.Announce("*** " + ctx.Me.Name + " adds " + (ctx.Tier == Tier.Rare ? "a JOKER" : "WILD TWOS") + " to the next hand! ***");
            return UseResult.Success(ctx.Tier == Tier.Rare ? "A joker will be in the next deck." : "All 2s are wild next hand.");
        }
    }
}
