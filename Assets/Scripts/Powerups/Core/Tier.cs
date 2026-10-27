using System;

namespace Holdem.PowerUps
{
    /// <summary>Power-up rarity. The number is the order: Common (0) is lowest, Legendary (4) highest.</summary>
    public enum Tier { Common = 0, Rare = 1, Epic = 2, Mythic = 3, Legendary = 4 }

    public static class TierExtensions
    {
        public const int Count = 5;

        /// <summary>Legendary cannot be upgraded.</summary>
        public static bool CanUpgrade(this Tier t) => t != Tier.Legendary;

        /// <summary>The next tier up. Throws for Legendary.</summary>
        public static Tier NextUp(this Tier t)
        {
            if (!t.CanUpgrade()) throw new InvalidOperationException("Legendary cannot be upgraded.");
            return (Tier)((int)t + 1);
        }
    }
}
