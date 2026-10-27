using System;
using System.Collections.Generic;
using System.Linq;
using Holdem;

namespace Holdem.PowerUps
{
    /// <summary>
    /// Holds every player's inventory and runs activations. Lives next to the engine, not inside it,
    /// so GameEngine stays ignorant of power-ups.
    /// </summary>
    public sealed class PowerUpSystem
    {
        private readonly GameEngine _engine;
        private readonly Dictionary<Player, PowerUpInventory> _inv = new Dictionary<Player, PowerUpInventory>();

        /// <summary>Dev mode switch: skip timing rules (turn / street) when using a power-up.</summary>
        public bool IgnoreTiming;

        public event Action<int, PowerUpInstance, UseResult> Used; // seat, what, result

        public PowerUpSystem(GameEngine engine) { _engine = engine ?? throw new ArgumentNullException(nameof(engine)); }

        public PowerUpInventory Inventory(int seat)
        {
            var p = _engine.Players[seat];
            if (!_inv.TryGetValue(p, out var inv)) _inv[p] = inv = new PowerUpInventory();
            return inv;
        }

        /// <summary>Gives a power-up straight to a seat (dev mode, tests). False if the inventory is full.</summary>
        public bool Give(int seat, string id, Tier tier)
        {
            var def = PowerUpRegistry.Find(id) ?? throw new ArgumentException("Unknown power-up id: " + id);
            return Inventory(seat).TryAdd(new PowerUpInstance(def, tier)) == AddResult.Added;
        }

        /// <summary>Null if the item can be used now, otherwise the reason it cannot.</summary>
        public string CanUse(int seat, PowerUpInstance item)
        {
            var ctx = Context(seat, item.Tier);
            if (!ctx.InHand) return "You are not in a hand right now.";
            return item.Definition.CanUse(ctx);
        }

        /// <summary>Uses the item. One-use: it is removed from the inventory only if the effect really happened.</summary>
        public UseResult Use(int seat, PowerUpInstance item, UseRequest req)
        {
            var inv = Inventory(seat);
            if (!inv.Items.Contains(item)) return UseResult.Fail("You don't hold that power-up.");

            string why = CanUse(seat, item);
            if (why != null) return UseResult.Fail(why);

            UseResult res = item.Definition.Use(Context(seat, item.Tier), req) ?? UseResult.Fail("Nothing happened.");
            if (res.Ok) inv.Remove(item);
            Used?.Invoke(seat, item, res);
            return res;
        }

        private PowerUpContext Context(int seat, Tier tier) => new PowerUpContext
        {
            Engine = _engine, Seat = seat, Tier = tier, NextInt = _engine.NextInt, IgnoreTiming = IgnoreTiming
        };
    }
}
