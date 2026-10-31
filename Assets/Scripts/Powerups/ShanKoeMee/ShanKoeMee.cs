namespace Holdem.PowerUps
{
    /// <summary>
    /// Shan Koe Mee (Rare). Announced between hands. At showdown add your two hole cards (Ace = 1, 2-9 face value,
    /// 10/J/Q/K = 0, only the last digit counts). A total of 8 collects 1 ante from every opponent, 9 collects 2.
    /// Any other total: nothing, and it is spent. You cannot fold. You are paid whether or not you win the hand.
    /// If everybody else folds you still show your cards: a hit pays, a miss costs you 1 ante to each opponent.
    /// (The engine does the counting and paying; this class only switches it on.)
    /// </summary>
    public sealed class ShanKoeMee : PowerUpDefinition
    {
        public override string Id => "shankoemee";
        public override string Name => "Shan Koe Mee";
        public override Tier[] Tiers => new[] { Tier.Rare };
        public override UseWindow Window => UseWindow.BetweenHands;

        public override string Describe(Tier tier) =>
            "Announce before the hand. At showdown your two cards add up (A=1, 10/J/Q/K=0, last digit): 8 collects 1 ante from each opponent, 9 collects 2. You can't fold.";

        public override string CanUse(PowerUpContext ctx) =>
            ctx.Engine.NextRules.ShanKoeMee.Contains(ctx.Seat) ? "You already announced Shan Koe Mee for the next hand." : null;

        public override UseResult Use(PowerUpContext ctx, UseRequest req)
        {
            ctx.Engine.NextRules.ShanKoeMee.Add(ctx.Seat);
            ctx.Engine.Announce(ctx.Me.Name + " announces SHAN KOE MEE for the next hand! (no folding)");
            return UseResult.Success("Shan Koe Mee is set for the next hand. You can't fold.");
        }
    }
}
