using Holdem;

namespace Holdem.PowerUps
{
    /// <summary>
    /// X-Ray. On your turn, look at an opponent's hole card(s). Instant, one use.
    ///   Common:  see 1 random card.
    ///   Rare and up: see both cards.   (the design has 2 tiers; the higher tiers copy the top one until they get their own rules)
    /// </summary>
    public sealed class XRay : PowerUpDefinition
    {
        public override string Id => "xray";
        public override string Name => "X-Ray";

        public override string Describe(Tier tier) => tier == Tier.Common
            ? "On your turn, see 1 random hole card of an opponent."
            : "On your turn, see both hole cards of an opponent.";

        public override string CanUse(PowerUpContext ctx)
        {
            string why = NeedHand(ctx);
            if (why != null) return why;
            if (!ctx.IsMyTurn && !ctx.IgnoreTiming) return "X-Ray can only be used on your turn.";
            return null;
        }

        public override UseResult Use(PowerUpContext ctx, UseRequest req)
        {
            int t = req.TargetSeat;
            var players = ctx.Engine.Players;
            if (t < 0 || t >= players.Count) return UseResult.Fail("Pick an opponent.");
            if (t == ctx.Seat) return UseResult.Fail("You can't X-Ray yourself.");
            if (players[t].Folded || players[t].IsOut) return UseResult.Fail(players[t].Name + " has already folded.");

            var res = UseResult.Success("You X-Ray " + players[t].Name + ".");
            if (ctx.Tier == Tier.Common)
                res.Secret.Add(ctx.Engine.RevealHoleCard(t, ctx.NextInt(players[t].Hole.Count), ctx.Seat));
            else
                for (int i = 0; i < players[t].Hole.Count; i++)
                    res.Secret.Add(ctx.Engine.RevealHoleCard(t, i, ctx.Seat));
            return res;
        }
    }
}
