using System.Collections.Generic;
using System.Linq;
using Holdem;

namespace Holdem.PowerUps
{
    /// <summary>
    /// Bumba Card. Swap one of your hole cards (req.CardIndex 0 or 1) for a card from the deck.
    ///   Common: the top card (random).
    ///   Rare:   a random card of the suit you name (req.ChosenSuit).
    ///   Epic and up: the exact card you name (req.ChosenCard). If it is not in the deck, nothing happens and it is not spent.
    /// Not built yet: the "sit on the card and swap later" step, and the accusation / catch rules (they need UI).
    /// The swapped-out card goes back into the deck at a random depth (the engine's rule for every swap).
    /// </summary>
    public sealed class BumbaCard : PowerUpDefinition
    {
        public override string Id => "bumba";
        public override string Name => "Bumba Card";

        public override string Describe(Tier tier)
        {
            if (tier == Tier.Common) return "Swap one of your cards for a random card from the deck.";
            if (tier == Tier.Rare) return "Swap one of your cards for a random card of a suit you choose.";
            return "Swap one of your cards for the exact card you choose (if it is in the deck).";
        }

        public override string CanUse(PowerUpContext ctx) => NeedHand(ctx); // any time during the hand

        public override UseResult Use(PowerUpContext ctx, UseRequest req)
        {
            var me = ctx.Me;
            if (req.CardIndex < 0 || req.CardIndex >= me.Hole.Count) return UseResult.Fail("Pick which of your cards to swap.");
            var old = me.Hole[req.CardIndex];

            Card? wanted = null;
            if (ctx.Tier == Tier.Rare)
            {
                if (!req.ChosenSuit.HasValue) return UseResult.Fail("Pick a suit.");
                List<Card> pool = ctx.Engine.DeckCards.Where(c => c.Suit == req.ChosenSuit.Value).ToList();
                if (pool.Count == 0) return UseResult.Fail("No " + req.ChosenSuit.Value + " left in the deck. Not spent.");
                wanted = pool[ctx.NextInt(pool.Count)];
            }
            else if (ctx.Tier >= Tier.Epic)
            {
                if (!req.ChosenCard.HasValue) return UseResult.Fail("Pick a card.");
                if (!ctx.Engine.DeckCards.Contains(req.ChosenCard.Value)) return UseResult.Fail(req.ChosenCard.Value + " is not in the deck. Not spent.");
                wanted = req.ChosenCard.Value;
            }

            var fresh = ctx.Engine.ReplaceHoleCard(ctx.Seat, req.CardIndex, wanted);
            return UseResult.Success("You swap " + old + " for " + fresh + ".");
        }
    }
}
