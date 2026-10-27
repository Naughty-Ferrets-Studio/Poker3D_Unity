using System;
using System.Collections.Generic;
using Holdem;

namespace Holdem.PowerUps
{
    /// <summary>What the player chose when activating (target, card, forced die roll). Unused fields are ignored.</summary>
    public struct UseRequest
    {
        public int TargetSeat;      // opponent seat (X-Ray, Snatch the Sock...)
        public int CardIndex;       // which hole card (0/1) or board card, depending on the power-up
        public Card? ChosenCard;    // an exact card (Bumba Card III)
        public Suit? ChosenSuit;    // a suit (Bumba Card II)
        public int? ForcedRoll;     // dev mode only: force the die result
        public static UseRequest None => new UseRequest { TargetSeat = -1 };
    }

    /// <summary>Result of using a power-up. If Ok is false nothing happened and the power-up is NOT spent.</summary>
    public sealed class UseResult
    {
        public bool Ok;
        public string Message = "";
        /// <summary>Cards only the user may see (X-Ray). The UI shows them to the human; a bot may read them too.</summary>
        public List<Card> Secret = new List<Card>();

        public static UseResult Fail(string why) => new UseResult { Ok = false, Message = why };
        public static UseResult Success(string msg) => new UseResult { Ok = true, Message = msg };
    }

    /// <summary>Everything a power-up may touch. It only reaches the game through the engine's public card actions.</summary>
    public sealed class PowerUpContext
    {
        public GameEngine Engine;
        public int Seat;
        public Tier Tier;
        public Func<int, int> NextInt;   // [0, n)
        /// <summary>Dev mode: skip the "can I use it now" timing checks (the rest of the rules still apply).</summary>
        public bool IgnoreTiming;

        public Player Me => Engine.Players[Seat];
        public bool IsMyTurn => Engine.Current == Seat;
        public bool HandRunning => !Engine.HandOver;
        public bool InHand => HandRunning && !Me.Folded && !Me.IsOut;
    }

    /// <summary>
    /// ONE power-up = ONE class in its own folder. Add it to PowerUpRegistry (one line) and it exists.
    /// The engine never knows what a power-up does; it only runs Use().
    /// Each tier has its own behaviour: read ctx.Tier inside CanUse / Use.
    /// </summary>
    public abstract class PowerUpDefinition
    {
        /// <summary>Stable id, used by saves, tests and the dev panel. Never change it after release.</summary>
        public abstract string Id { get; }
        public abstract string Name { get; }
        /// <summary>One line for the UI: what this power-up does at this tier.</summary>
        public abstract string Describe(Tier tier);
        /// <summary>Extra weight when the award picks a random power-up (1 = equal chance).</summary>
        public virtual int Weight => 1;

        /// <summary>Returns null if it can be used now, otherwise the reason it can't.</summary>
        public abstract string CanUse(PowerUpContext ctx);

        /// <summary>Does the effect. Only called when CanUse returned null. Return Fail(...) to refuse without spending it.</summary>
        public abstract UseResult Use(PowerUpContext ctx, UseRequest req);

        /// <summary>Shared check: a hand is running and this seat is still in it.</summary>
        protected static string NeedHand(PowerUpContext ctx) => ctx.InHand ? null : "You are not in a hand right now.";
    }
}
