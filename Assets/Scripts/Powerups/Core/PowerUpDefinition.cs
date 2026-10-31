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
        public int TargetCardIndex; // which of the target's hole cards (Snatch the Sock II)
        public static UseRequest None => new UseRequest { TargetSeat = -1 };
    }

    /// <summary>Result of using a power-up. If Ok is false nothing happened and the power-up is NOT spent.</summary>
    public sealed class UseResult
    {
        public bool Ok;
        public string Message = "";
        /// <summary>Cards only the user may see (X-Ray). The UI shows them to the human; a bot may read them too.</summary>
        public List<Card> Secret = new List<Card>();
        /// <summary>The use started an effect that stays on the table (Bumba Card, Disguised Card). Opponents may accuse it.</summary>
        public bool Active;
        /// <summary>A follow-up action finished the effect.</summary>
        public bool EndsEffect;

        public static UseResult Fail(string why) => new UseResult { Ok = false, Message = why };
        public static UseResult Success(string msg) => new UseResult { Ok = true, Message = msg };
    }

    /// <summary>When a power-up can be used.</summary>
    public enum UseWindow
    {
        InHand,        // while you are still in a hand that is running
        BetweenHands,  // after one hand ended, before the next deal (it shapes the next hand)
        AllInRunout,   // everybody left is all-in and the rest of the board has not been dealt yet
    }

    /// <summary>An effect that is still on the table after a power-up was used (a card being sat on, a sticker on a card).</summary>
    public sealed class ActiveEffect
    {
        public readonly int Seat;
        public readonly PowerUpDefinition Definition;
        public readonly Tier Tier;
        public ActiveEffect(int seat, PowerUpDefinition definition, Tier tier) { Seat = seat; Definition = definition; Tier = tier; }
        public override string ToString() => Definition.VersionName(Tier) + " (seat " + Seat + ")";
    }

    /// <summary>One accusation: "I think Target is using [NamedId]".</summary>
    public struct AccuseRequest
    {
        public int Accuser, Target;
        public string NamedId;      // "bumba" or "disguised"
        public bool? GuessHigher;   // Disguised Card: is the real card higher than the sticker? (a tie counts as lower)
        public int RevealIndex;     // Bumba Card: which hole card the caught thief shows
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
        /// <summary>Is the seat 'p' still playing this hand?</summary>
        public bool Alive(int seat) => seat >= 0 && seat < Engine.Players.Count && !Engine.Players[seat].Folded && !Engine.Players[seat].IsOut;
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

        /// <summary>Id of the power-up an accuser has to name to catch this one. Only used when Accusable.</summary>
        public virtual bool Accusable => false;
        public virtual bool NeedsGuess => false;
        /// <summary>Chip bounty (in antes) the accuser takes when this power-up is caught.</summary>
        public virtual int Bounty(Tier tier) => 0;
        /// <summary>Did the accusation catch it? (Bumba: always. Disguised: if the higher/lower guess was right.)</summary>
        public virtual bool IsCaught(PowerUpContext ctx, ActiveEffect effect, AccuseRequest req) => true;
        /// <summary>The effect after being caught (card back into the deck, sticker off...). ctx is the accused player's.</summary>
        public virtual void OnCaught(PowerUpContext ctx, ActiveEffect effect, AccuseRequest req) { }

        /// <summary>When it can be used.</summary>
        public virtual UseWindow Window => UseWindow.InHand;

        /// <summary>A second step after Use (Bumba Card: sneak the card into your hand). Null = none.</summary>
        public virtual string FollowUpLabel => null;
        public virtual UseResult FollowUp(PowerUpContext ctx, ActiveEffect effect, UseRequest req) => UseResult.Fail("Nothing to do.");

        /// <summary>
        /// The tiers this power-up exists at, lowest first. Each entry is one version (I, II, III) fixed to that tier.
        /// Example: Bumba Card = { Rare, Epic, Mythic } means Bumba Card I is Rare, II is Epic, III is Mythic.
        /// </summary>
        public abstract Tier[] Tiers { get; }

        public bool HasTier(Tier t) => System.Array.IndexOf(Tiers, t) >= 0;

        /// <summary>1-based version number at that tier (Bumba Card II = 2).</summary>
        public int Version(Tier t) => System.Array.IndexOf(Tiers, t) + 1;

        /// <summary>The next version up (what two merged copies become), or null if this is the last version.</summary>
        public Tier? NextTier(Tier t)
        {
            int i = System.Array.IndexOf(Tiers, t);
            return i >= 0 && i + 1 < Tiers.Length ? Tiers[i + 1] : (Tier?)null;
        }

        /// <summary>"Bumba Card II", or just "The Dice" when there is only one version.</summary>
        public virtual string VersionName(Tier t)
        {
            if (Tiers.Length == 1) return Name;
            string[] roman = { "I", "II", "III", "IV", "V" };
            return Name + " " + roman[Version(t) - 1];
        }
        /// <summary>One line for the UI: what this version does. Only called with a tier from Tiers.</summary>
        public abstract string Describe(Tier tier);
        /// <summary>Extra weight when the award picks a random power-up (1 = equal chance).</summary>
        public virtual int Weight => 1;

        /// <summary>Returns null if it can be used now, otherwise the reason it can't.</summary>
        public abstract string CanUse(PowerUpContext ctx);

        /// <summary>Shared check for powers that must be used on your own turn.</summary>
        protected static string NeedTurn(PowerUpContext ctx) => ctx.IsMyTurn || ctx.IgnoreTiming ? null : "This can only be used on your turn.";

        /// <summary>Does the effect. Only called when CanUse returned null. Return Fail(...) to refuse without spending it.</summary>
        public abstract UseResult Use(PowerUpContext ctx, UseRequest req);

        /// <summary>Shared check: a hand is running and this seat is still in it.</summary>
        protected static string NeedHand(PowerUpContext ctx) => ctx.InHand ? null : "You are not in a hand right now.";
    }
}
