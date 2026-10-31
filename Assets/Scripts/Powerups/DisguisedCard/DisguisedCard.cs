using Holdem;

namespace Holdem.PowerUps
{
    public enum TellLevel { None, Some, Many }

    /// <summary>
    /// Disguised Card. Stick a fake card sticker over one of your hole cards (req.CardIndex) so it counts as the sticker
    /// (req.ChosenCard). It lasts through showdown unless you are caught. The secret mini game and the visible "tells"
    /// are for the 3D table; the rules are here.
    ///   Disguised Card I   (Rare):   hard shake, many tells.
    ///   Disguised Card II  (Epic):   normal shake.
    ///   Disguised Card III (Mythic): guaranteed clean, no tells, and a bigger bounty.
    /// Catching it: the accuser names "disguised" and guesses if the real card is higher or lower than the sticker
    /// (a tie counts as lower). Right: the real card is shown and the accuser takes the bounty. Wrong: the disguise stays
    /// and the accuser pays 25% of their pot contribution (at least 1 ante).
    /// </summary>
    public sealed class DisguisedCard : PowerUpDefinition
    {
        public override string Id => "disguised";
        public override string Name => "Disguised Card";
        public override Tier[] Tiers => new[] { Tier.Rare, Tier.Epic, Tier.Mythic };
        public override bool Accusable => true;
        public override bool NeedsGuess => true;
        public override int Bounty(Tier tier) => tier == Tier.Mythic ? 3 : 2;

        /// <summary>How visible the disguise is to opponents (the table can shake or deform the card by this much).</summary>
        public static TellLevel Tells(Tier tier) => tier == Tier.Rare ? TellLevel.Many : tier == Tier.Epic ? TellLevel.Some : TellLevel.None;

        public override string Describe(Tier tier)
        {
            string tells = tier == Tier.Rare ? "Hard shake: many tells." : tier == Tier.Epic ? "Normal shake." : "Perfectly clean, no tells.";
            return "Stick a fake card sticker over one of your hole cards. It counts as the sticker unless you are caught. " + tells;
        }

        public override string CanUse(PowerUpContext ctx)
        {
            string why = NeedHand(ctx);
            if (why != null) return why;
            if (ctx.Engine.GetDisguise(ctx.Seat).HasValue) return "You already wear a sticker.";
            return null;
        }

        public override UseResult Use(PowerUpContext ctx, UseRequest req)
        {
            if (req.CardIndex < 0 || req.CardIndex >= ctx.Me.Hole.Count) return UseResult.Fail("Pick which of your cards to cover.");
            if (!req.ChosenCard.HasValue) return UseResult.Fail("Pick the card the sticker shows.");
            ctx.Engine.ApplySticker(ctx.Seat, req.CardIndex, req.ChosenCard.Value); // refuses a card you can see yourself
            var res = UseResult.Success("A " + req.ChosenCard.Value + " sticker now covers your card.");
            res.Active = true;
            return res;
        }

        public override bool IsCaught(PowerUpContext ctx, ActiveEffect effect, AccuseRequest req)
        {
            var d = ctx.Engine.GetDisguise(effect.Seat);
            if (!d.HasValue) return false;
            bool realIsHigher = (int)d.Value.Real.Rank > (int)d.Value.Sticker.Rank; // a tie counts as lower
            return req.GuessHigher.HasValue && req.GuessHigher.Value == realIsHigher;
        }

        public override void OnCaught(PowerUpContext ctx, ActiveEffect effect, AccuseRequest req) => ctx.Engine.ExposeDisguise(effect.Seat);
    }
}
