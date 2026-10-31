namespace Holdem.PowerUps
{
    /// <summary>
    /// Snatch the Sock. On your turn, take one of an opponent's hole cards and give them one of yours (face down).
    /// Both hands stay at two cards and you can use the card you took right away. Works well after X-Ray.
    ///   Snatch the Sock I  (Rare): you take a random card (req.CardIndex = YOUR card to give away).
    ///   Snatch the Sock II (Epic): you choose which card to take (req.TargetCardIndex).
    /// A sticker on a swapped card falls off.
    /// </summary>
    public sealed class SnatchTheSock : PowerUpDefinition
    {
        public override string Id => "snatch";
        public override string Name => "Snatch the Sock";
        public override Tier[] Tiers => new[] { Tier.Rare, Tier.Epic };

        public override string Describe(Tier tier) => tier == Tier.Rare
            ? "On your turn: take a random hole card from an opponent and give them one of yours."
            : "On your turn: take the hole card you choose from an opponent and give them one of yours.";

        public override string CanUse(PowerUpContext ctx)
        {
            string why = NeedHand(ctx);
            return why ?? NeedTurn(ctx);
        }

        public override UseResult Use(PowerUpContext ctx, UseRequest req)
        {
            int t = req.TargetSeat;
            if (t < 0 || t >= ctx.Engine.Players.Count) return UseResult.Fail("Pick an opponent.");
            if (t == ctx.Seat) return UseResult.Fail("You can't snatch from yourself.");
            if (!ctx.Alive(t)) return UseResult.Fail(ctx.Engine.Players[t].Name + " has already folded.");
            var target = ctx.Engine.Players[t];
            if (req.CardIndex < 0 || req.CardIndex >= ctx.Me.Hole.Count) return UseResult.Fail("Pick which of your cards to give away.");

            int theirs = ctx.Tier == Tier.Rare ? ctx.NextInt(target.Hole.Count) : req.TargetCardIndex;
            if (theirs < 0 || theirs >= target.Hole.Count) return UseResult.Fail("Pick which of their cards to take.");

            ctx.Engine.SwapHoleCards(ctx.Seat, req.CardIndex, t, theirs);
            var res = UseResult.Success("You snatch a card from " + target.Name + " and give them one of yours.");
            res.Secret.Add(ctx.Me.Hole[req.CardIndex]); // the card you took
            return res;
        }
    }
}
