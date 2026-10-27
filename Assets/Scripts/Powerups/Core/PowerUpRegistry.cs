using System.Collections.Generic;
using System.Linq;

namespace Holdem.PowerUps
{
    /// <summary>The ONLY file you edit to add a power-up: one line in All.</summary>
    public static class PowerUpRegistry
    {
        public static readonly IReadOnlyList<PowerUpDefinition> All = new PowerUpDefinition[]
        {
            new XRay(),
            new Dice(),
            new BumbaCard(),
        };

        public static PowerUpDefinition Find(string id) => All.FirstOrDefault(d => d.Id == id);

        /// <summary>Every power-up that has a version at this tier.</summary>
        public static List<PowerUpDefinition> Candidates(Tier tier) => All.Where(d => d.HasTier(tier)).ToList();

        /// <summary>Award step, part 2: after the tier is rolled, pick one power-up at that tier (by Weight). Null if none exist at that tier yet.</summary>
        public static PowerUpDefinition PickForTier(Tier tier, System.Func<int, int> nextInt = null)
        {
            var list = Candidates(tier);
            if (list.Count == 0) return null;
            int total = list.Sum(d => d.Weight);
            int r = (nextInt ?? SecureRng.Next)(total);
            foreach (var d in list) { if (r < d.Weight) return d; r -= d.Weight; }
            return list[list.Count - 1];
        }
    }
}
