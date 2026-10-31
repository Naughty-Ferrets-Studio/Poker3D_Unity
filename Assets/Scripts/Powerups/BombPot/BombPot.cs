namespace Holdem.PowerUps
{
    /// <summary>
    /// Bomb Pot (Common). Used between hands. The next hand: everybody antes, there are no blinds and no preflop betting,
    /// and play starts at the flop. Only one Bomb Pot per hand.
    /// </summary>
    public sealed class BombPot : PowerUpDefinition
    {
        public override string Id => "bombpot";
        public override string Name => "Bomb Pot";
        public override Tier[] Tiers => new[] { Tier.Common };
        public override UseWindow Window => UseWindow.BetweenHands;

        public override string Describe(Tier tier) =>
            "Before the deal: everybody antes " + GameEngine.Ante + ", there is no preflop betting, and the hand starts at the flop.";

        public override string CanUse(PowerUpContext ctx) =>
            ctx.Engine.NextRules.BombPot ? "A Bomb Pot is already set for the next hand." : null;

        public override UseResult Use(PowerUpContext ctx, UseRequest req)
        {
            ctx.Engine.NextRules.BombPot = true;
            ctx.Engine.Announce("*** " + ctx.Me.Name + " calls a BOMB POT for the next hand! ***");
            return UseResult.Success("The next hand is a Bomb Pot.");
        }
    }
}
