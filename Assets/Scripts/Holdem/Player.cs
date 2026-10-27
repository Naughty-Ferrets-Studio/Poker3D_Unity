using System;
using System.Collections.Generic;
using System.Linq;

namespace Holdem
{

public class Player
{
    public string Name;
    public int Chips;
    public bool IsHuman;
    public List<Card> Hole = new List<Card>();
    public BotStyle Style;       // personality of a computer player (null for the human)

    public bool Folded, AllIn, IsOut, HasActed;
    public bool CanRaise = true; // false after a short all-in raise if this player has already acted this round
    public int Bet;      // chips put in during the current betting round
    public int Contrib;  // chips put in during the whole hand (used for side pots)
}
}
