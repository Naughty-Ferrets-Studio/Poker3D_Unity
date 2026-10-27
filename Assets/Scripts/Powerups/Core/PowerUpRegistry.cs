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
    }
}
