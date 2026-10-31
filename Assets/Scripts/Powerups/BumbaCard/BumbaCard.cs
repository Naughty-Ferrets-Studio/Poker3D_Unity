using System.Linq;
using Holdem;

namespace Holdem.PowerUps
{
    /// <summary>
    /// Bumba Card ("sit on a card"). Two steps.
    ///  1) Use: secretly take a card out of the deck and sit on it. The dealer only says that a card is missing.
    ///        Bumba Card I   (Rare):   a random card (the top one).
    ///        Bumba Card II  (Epic):   you name a suit (req.ChosenSuit), you get a random card of that suit.
    ///        Bumba Card III (Mythic): you name the exact card (req.ChosenCard); if it is not in the deck nothing happens.
    ///  2) Follow-up "sneak it in": swap it with one of your hole cards (req.CardIndex). The old card goes face down into
    ///     the discard pile. Do it before the hand ends, or the card goes back into the deck and the power-up is wasted.
    /// While you are sitting on the card, anybody can accuse you by naming "bumba". Caught: the card goes back into the
    /// deck, you show one hole card of your choice (req.RevealIndex) and pay the bounty (2 antes, 3 for Bumba III).
    /// The secret mini game for sneaking the card in is for the 3D table; here the swap simply works.
    /// </summary>
    public sealed class BumbaCard : PowerUpDefinition
    {
        public override string Id => "bumba";
        public override string Name => "Bumba Card";
        public override Tier[] Tiers => new[] { Tier.Rare, Tier.Epic, Tier.Mythic };
        public override bool Accusable => true;
        public override int Bounty(Tier tier) => tier == Tier.Mythic ? 3 : 2;
        public override string FollowUpLabel => "Sneak the card into your hand";

        public override string Describe(Tier tier)
        {
            if (tier == Tier.Rare) return "Secretly take a random card from the deck and sit on it. Later, swap it with one of your hole cards. Don't get caught.";
            if (tier == Tier.Epic) return "Secretly take a random card of a suit you choose from the deck and sit on it. Later, swap it into your hand.";
            return "Secretly take the exact card you choose from the deck and sit on it. Later, swap it into your hand.";
        }

        public override string CanUse(PowerUpContext ctx)
        {
            string why = NeedHand(ctx);
            if (why != null) return why;
            if (ctx.Engine.HasHeldCard(ctx.Seat)) return "You are already sitting on a card.";
            return null;
        }

        public override UseResult Use(PowerUpContext ctx, UseRequest req)
        {
            Card? wanted = null;
            if (ctx.Tier == Tier.Epic)
            {
                if (!req.ChosenSuit.HasValue) return UseResult.Fail("Pick a suit.");
                var pool = ctx.Engine.DeckCards.Where(c => !c.IsJoker && c.Suit == req.ChosenSuit.Value).ToList();
                if (pool.Count == 0) return UseResult.Fail("No " + req.ChosenSuit.Value + " left in the deck. Not spent.");
                wanted = pool[ctx.NextInt(pool.Count)];
            }
            else if (ctx.Tier == Tier.Mythic)
            {
                if (!req.ChosenCard.HasValue) return UseResult.Fail("Pick a card.");
                if (!ctx.Engine.DeckCards.Contains(req.ChosenCard.Value)) return UseResult.Fail(req.ChosenCard.Value + " is not in the deck. Not spent.");
                wanted = req.ChosenCard.Value;
            }

            Card taken = ctx.Engine.StealFromDeck(ctx.Seat, wanted);
            var res = UseResult.Success("You take the " + taken + " from the deck and sit on it. Sneak it into your hand before the hand ends.");
            res.Secret.Add(taken);
            res.Active = true;
            return res;
        }

        public override UseResult FollowUp(PowerUpContext ctx, ActiveEffect effect, UseRequest req)
        {
            if (!ctx.Engine.HasHeldCard(ctx.Seat)) return UseResult.Fail("You are not sitting on a card.");
            if (req.CardIndex < 0 || req.CardIndex >= ctx.Me.Hole.Count) return UseResult.Fail("Pick which of your cards to swap out.");
            Card old = ctx.Engine.SwapHeldIntoHole(ctx.Seat, req.CardIndex);
            var res = UseResult.Success("You swap out the " + old + " (face down into the discard pile).");
            res.EndsEffect = true;
            return res;
        }

        public override void OnCaught(PowerUpContext ctx, ActiveEffect effect, AccuseRequest req)
        {
            ctx.Engine.ReturnHeldToDeck(effect.Seat);
            var hole = ctx.Engine.Players[effect.Seat].Hole;
            if (hole.Count > 0) ctx.Engine.RevealHoleCard(effect.Seat, System.Math.Max(0, System.Math.Min(req.RevealIndex, hole.Count - 1)), -1);
        }
    }
}
