namespace Holdem.PowerUps
{
    /// <summary>
    /// Run It Twice / Thrice. When every player left in the hand is all-in, any all-in player can use it (it cannot be blocked).
    /// The rest of the board is dealt again, and each run decides an equal share of every pot (side pots too).
    ///   Run It Twice  (Epic):      two runs, halves.
    ///   Run It Thrice (Legendary): three runs, thirds.
    /// </summary>
    public sealed class RunIt : PowerUpDefinition
    {
        public override string Id => "runit";
        public override string Name => "Run It";
        public override Tier[] Tiers => new[] { Tier.Epic, Tier.Legendary };
        public override UseWindow Window => UseWindow.AllInRunout;

        public override string VersionName(Tier t) => t == Tier.Epic ? "Run It Twice" : "Run It Thrice";

        public override string Describe(Tier tier) => tier == Tier.Epic
            ? "When everyone left is all-in: deal the rest of the board twice. Each run wins half of each pot."
            : "When everyone left is all-in: deal the rest of the board three times. Each run wins a third of each pot.";

        public override string CanUse(PowerUpContext ctx) => null; // the window (everyone all-in, board not dealt yet) is checked by the system

        public override UseResult Use(PowerUpContext ctx, UseRequest req)
        {
            int runs = ctx.Tier == Tier.Epic ? 2 : 3;
            return ctx.Engine.RequestRuns(ctx.Seat, runs)
                ? UseResult.Success("The board will be run " + (runs == 2 ? "twice." : "three times."))
                : UseResult.Fail("The board is already going to be run that many times.");
        }
    }
}
