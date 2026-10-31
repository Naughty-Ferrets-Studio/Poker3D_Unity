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

        // ---------------------------------------------------------------- effects that stay on the table
        private readonly List<ActiveEffect> _effects = new List<ActiveEffect>();
        private readonly HashSet<int> _accused = new HashSet<int>();
        private int _effectsHand = -1;

        private void Sync()
        {
            if (_effectsHand == _engine.HandNumber) return;
            _effects.Clear(); _accused.Clear(); _effectsHand = _engine.HandNumber; // a new hand wipes them
        }

        public IReadOnlyList<ActiveEffect> Effects(int seat) { Sync(); return _effects.Where(e => e.Seat == seat).ToList(); }
        public IReadOnlyList<ActiveEffect> AllEffects() { Sync(); return _effects.ToList(); }
        public bool HasAccused(int seat) { Sync(); return _accused.Contains(seat); }

        /// <summary>Null if the item can be used now, otherwise the reason it cannot.</summary>
        public string CanUse(int seat, PowerUpInstance item)
        {
            var ctx = Context(seat, item.Tier);
            switch (item.Definition.Window)
            {
                case UseWindow.BetweenHands:
                    if (!IgnoreTiming && (!_engine.HandOver || _engine.IsGameOver)) return "Use this between hands, before the next deal.";
                    if (ctx.Me.Chips <= 0) return "You have no chips left.";
                    break;
                case UseWindow.AllInRunout:
                    if (!_engine.RunoutPending) return "Only when everyone left in the hand is all-in, before the rest of the board is dealt.";
                    if (!ctx.InHand || !ctx.Me.AllIn) return "Only a player who is all-in can use this.";
                    break;
                default:
                    if (!ctx.InHand) return "You are not in a hand right now.";
                    break;
            }
            return item.Definition.CanUse(ctx);
        }

        /// <summary>Uses the item. One-use: it is removed from the inventory only if the effect really happened.</summary>
        public UseResult Use(int seat, PowerUpInstance item, UseRequest req)
        {
            Sync();
            var inv = Inventory(seat);
            if (!inv.Items.Contains(item)) return UseResult.Fail("You don't hold that power-up.");

            string why = CanUse(seat, item);
            if (why != null) return UseResult.Fail(why);

            UseResult res;
            try { res = item.Definition.Use(Context(seat, item.Tier), req) ?? UseResult.Fail("Nothing happened."); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            { res = UseResult.Fail(ex.Message); } // the engine refused (nothing was spent)

            if (res.Ok)
            {
                inv.Remove(item);
                if (res.Active) _effects.Add(new ActiveEffect(seat, item.Definition, item.Tier));
            }
            Used?.Invoke(seat, item, res);
            return res;
        }

        /// <summary>The second step of a power-up that has one (Bumba Card: sneak the card into your hand).</summary>
        public UseResult UseFollowUp(int seat, ActiveEffect effect, UseRequest req)
        {
            Sync();
            if (!_effects.Contains(effect) || effect.Seat != seat) return UseResult.Fail("That is not active any more.");
            var ctx = Context(seat, effect.Tier);
            if (!ctx.InHand) return UseResult.Fail("You are not in a hand right now.");
            UseResult res;
            try { res = effect.Definition.FollowUp(ctx, effect, req) ?? UseResult.Fail("Nothing happened."); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            { res = UseResult.Fail(ex.Message); }
            if (res.Ok && res.EndsEffect) _effects.Remove(effect);
            return res;
        }

        // ---------------------------------------------------------------- accusations
        /// <summary>
        /// "I think seat X is using [power-up]". One accusation per player per hand, shared across all power-ups.
        /// Caught: the accused pays the bounty. Wrong guess, wrong name, or nothing to catch: the accuser pays the accused
        /// 25% of what they have put in the pot this hand (at least 1 ante).
        /// </summary>
        public UseResult Accuse(AccuseRequest r)
        {
            Sync();
            int n = _engine.Players.Count;
            if (_engine.HandOver) return UseResult.Fail("There is no hand to accuse anyone in.");
            if (r.Accuser < 0 || r.Accuser >= n || r.Target < 0 || r.Target >= n) return UseResult.Fail("Pick a player.");
            if (r.Accuser == r.Target) return UseResult.Fail("You can't accuse yourself.");
            var accuser = _engine.Players[r.Accuser]; var target = _engine.Players[r.Target];
            if (accuser.Folded || accuser.IsOut) return UseResult.Fail("You are not in this hand.");
            if (target.Folded || target.IsOut) return UseResult.Fail(target.Name + " is not in this hand.");
            if (_accused.Contains(r.Accuser)) return UseResult.Fail("You already used your one accusation this hand.");

            var def = PowerUpRegistry.Find(r.NamedId);
            ActiveEffect eff = def != null && def.Accusable ? _effects.FirstOrDefault(e => e.Seat == r.Target && e.Definition == def) : null;

            if (eff != null && def.NeedsGuess && !r.GuessHigher.HasValue)
                return UseResult.Fail("Guess first: is the real card higher or lower than the sticker? (a tie counts as lower)");

            _accused.Add(r.Accuser); // the accusation is used from here on

            if (eff != null && def.IsCaught(Context(r.Target, eff.Tier), eff, r))
            {
                def.OnCaught(Context(r.Target, eff.Tier), eff, r);
                _effects.Remove(eff);
                int paid = _engine.TransferChips(r.Target, r.Accuser, def.Bounty(eff.Tier) * GameEngine.Ante);
                string msg = accuser.Name + " caught " + target.Name + " using " + def.VersionName(eff.Tier) + " and collects " + paid + "!";
                _engine.Announce(msg);
                return UseResult.Success(msg);
            }

            int penalty = Math.Max(GameEngine.Ante, accuser.Contrib / 4);
            int sent = _engine.TransferChips(r.Accuser, r.Target, penalty);
            string why = eff == null ? "False accusation" : "Wrong guess, the disguise stays";
            string note = why + "! " + accuser.Name + " pays " + target.Name + " " + sent + ".";
            _engine.Announce(note);
            return UseResult.Success(note);
        }

        private PowerUpContext Context(int seat, Tier tier) => new PowerUpContext
        {
            Engine = _engine, Seat = seat, Tier = tier, NextInt = _engine.NextInt, IgnoreTiming = IgnoreTiming
        };
    }
}
