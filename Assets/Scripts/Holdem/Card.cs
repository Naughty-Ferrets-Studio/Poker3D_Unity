using System;
using System.Globalization;

namespace Holdem
{
    public enum Suit { Clubs, Diamonds, Hearts, Spades }

    public enum Rank
    {
        Two = 2, Three, Four, Five, Six, Seven, Eight, Nine, Ten,
        Jack, Queen, King, Ace
    }

    /// <summary>Immutable playing card with value equality (usable as a Dictionary / HashSet key).</summary>
    public readonly struct Card : IEquatable<Card>
    {
        public readonly Rank Rank;
        public readonly Suit Suit;

        public Card(Rank rank, Suit suit) { Rank = rank; Suit = suit; }

        /// <summary>Asset / prefab / texture name, e.g. card_hearts_A, card_spades_02, card_clubs_10.</summary>
        public string AssetName
        {
            get
            {
                string r;
                switch (Rank)
                {
                    case Rank.Jack:  r = "J"; break;
                    case Rank.Queen: r = "Q"; break;
                    case Rank.King:  r = "K"; break;
                    case Rank.Ace:   r = "A"; break;
                    default: r = ((int)Rank).ToString("00", CultureInfo.InvariantCulture); break;
                }
                // Invariant: avoids the Turkish-locale "I" problem when lower-casing names
                return "card_" + Suit.ToString().ToLowerInvariant() + "_" + r;
            }
        }

        public bool Equals(Card other) => Rank == other.Rank && Suit == other.Suit;
        public override bool Equals(object obj) => obj is Card c && Equals(c);
        public override int GetHashCode() => ((int)Rank << 2) | (int)Suit;
        public static bool operator ==(Card a, Card b) => a.Equals(b);
        public static bool operator !=(Card a, Card b) => !a.Equals(b);

        public override string ToString() => AssetName.Replace("card_", "");
    }
}
