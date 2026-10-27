using System;
using System.Collections.Generic;
using System.Linq;

namespace Holdem
{

/// <summary>
/// Personality of a computer opponent.
/// Loose = how many hands it plays, Aggr = how often it bets/raises (instead of checking/calling), Bluff = bluff frequency.
/// </summary>
public sealed class BotStyle
{
    public readonly double Loose, Aggr, Bluff;

    public BotStyle(double loose, double aggr, double bluff)
    {
        Loose = loose; Aggr = aggr; Bluff = bluff;
    }

    public static BotStyle For(string name) => name switch
    {
        "Alice" => new BotStyle(0.85, 1.10, 0.08),   // tight-aggressive
        "Bob"   => new BotStyle(1.20, 1.30, 0.18),   // loose-aggressive
        "Cara"  => new BotStyle(1.00, 1.00, 0.10),   // balanced
        "Dan"   => new BotStyle(0.80, 0.70, 0.03),   // tight-passive
        "Eve"   => new BotStyle(1.30, 0.60, 0.05),   // loose-passive ("calling station")
        "Finn"  => new BotStyle(0.90, 1.20, 0.10),   // tight-aggressive, a bit more bluffing
        "Gus"   => new BotStyle(1.10, 0.80, 0.04),   // loose-passive, rarely bluffs
        "Hana"  => new BotStyle(1.05, 1.15, 0.14),   // loose-aggressive light
        _       => new BotStyle(1.00, 1.00, 0.08),
    };
}

/// <summary>
/// Computer opponent.
/// Preflop: Chen formula (a standard starting-hand point count) + position + what has happened so far.
/// Postflop: Monte Carlo equity against the live opponents, compared with pot odds; plus continuation bets,
/// bluffs and value raises. Bots only look at their own cards and public information.
/// </summary>
public static class Bot
{
    public static (PlayerAction, int) Decide(GameEngine e, Player p, Random r)
    {
        var st = p.Style ?? new BotStyle(1.0, 1.0, 0.08);
        int bb = GameEngine.BigBlind;
        int toCall = e.CurrentBet - p.Bet;
        int callAmt = Math.Min(toCall, p.Chips);
        int pot = e.Pot;
        bool canRaise = p.Chips > toCall && p.CanRaise;
        int opp = e.Players.Count(o => o != p && !o.Folded);
        double pos = PositionOf(e, p);                      // 0 = first to act after the flop, 1 = on the button
        double stackBB = (p.Chips + p.Bet) / (double)bb;

        var call = (PlayerAction.CheckCall, 0);
        var fold = (PlayerAction.Fold, 0);
        var shove = (PlayerAction.Raise, p.Bet + p.Chips);

        // "raise to" amount, rounded to 5s; becomes a shove if it commits most of the stack
        (PlayerAction, int) Mk(double raiseTo)
        {
            int cap = p.Bet + p.Chips;
            int amount = (int)(Math.Round(raiseTo / 5.0, MidpointRounding.AwayFromZero) * 5);
            if (amount >= p.Bet + p.Chips * 0.65) return shove;
            return (PlayerAction.Raise, Math.Min(amount, cap));
        }
        // raise by a fraction of the pot (after calling)
        (PlayerAction, int) BetFrac(double frac) =>
            Mk(e.CurrentBet + Math.Max(e.MinRaise, (pot + callAmt) * frac));

        // ---------------------------------------------------------------- preflop
        if (e.Street == Street.Preflop)
        {
            int chen = Chen(p.Hole[0], p.Hole[1]);
            bool raised = e.CurrentBet > bb;
            double level = (double)e.CurrentBet / bb;       // size of the current bet in big blinds
            double looseAdj = (st.Loose - 1) * 4;
            bool bbDefence = p.Bet >= bb;                   // already posted the big blind: getting a discount
            if (raised && toCall <= 0) return call;

            if (stackBB < 12)                               // short stack: push or fold
            {
                if (!raised)
                {
                    double pushTh = (stackBB < 6 ? 3.0 : stackBB < 9 ? 5.5 : 7.0) + 2.5 * (1 - pos) - looseAdj * 0.5;
                    if (chen >= pushTh) return canRaise ? shove : call;
                    return toCall <= 0 ? call : fold;
                }
                double shortCallTh = 9.5 - looseAdj * 0.7 - (bbDefence ? 1.5 : 0) - (stackBB < 6 ? 2 : 0);
                if (chen >= shortCallTh + 1.5 && canRaise && stackBB < 8) return shove;
                return chen >= shortCallTh ? call : fold;
            }

            if (!raised)                                    // unopened (or limped) pot
            {
                int limpers = e.Players.Count(o => !o.IsOut && o.Bet >= bb) - 1;
                double openTh = 8 - 4 * pos - looseAdj;
                double limpTh = openTh - 1.5 - (toCall > 0 && toCall < bb ? 1.5 : 0);
                double raiseTh = openTh + Math.Max(0.0, 1 - st.Aggr) * 5;   // passive players raise only strong hands and limp the rest
                if (chen >= raiseTh + (toCall <= 0 ? 1 : 0) && canRaise && r.NextDouble() < Math.Min(1.0, 0.6 + 0.4 * st.Aggr))
                    return Mk(bb * (2.4 + 0.8 * r.NextDouble()) + limpers * bb);
                if (toCall <= 0 || (chen >= limpTh && (chen >= openTh || r.NextDouble() < 0.35 + 0.45 * st.Loose))) return call;
                return fold;
            }

            if (level <= 4.5)                               // facing a single raise
            {
                double callTh = 8.5 - 2 * pos + 0.6 * Math.Max(0.0, level - 3) - looseAdj - (bbDefence ? 1.5 : 0);
                double threeTh = 10.5 - (st.Aggr - 1) * 2.5;
                if (chen >= threeTh && canRaise && r.NextDouble() < 0.45 + 0.4 * st.Aggr)
                    return Mk(e.CurrentBet * (pos > 0.6 ? 2.7 : 3.3));              // 3-bet
                if (chen >= callTh) return call;
                if (canRaise && chen >= 6 && r.NextDouble() < st.Bluff * 0.6)
                    return Mk(e.CurrentBet * 3.0);                                   // light 3-bet
                return fold;
            }
            if (level <= 12)                                // facing a 3-bet
            {
                if (chen >= 15 && canRaise) return Mk(e.CurrentBet * 2.3);
                return chen >= 10.5 - (st.Loose - 1) * 3 ? call : fold;
            }
            if (chen >= 16 && canRaise) return shove;       // facing a 4-bet or a shove
            return chen >= 12.5 - (st.Loose - 1) * 2 ? call : fold;
        }

        // ---------------------------------------------------------------- postflop
        bool river = e.Street == Street.River;
        double eq = Equity(p.Hole, e.Community, opp, 300, r);
        double fair = 1.0 / (opp + 1);
        double str = (eq - fair) / (1 - fair);              // 0 = average hand for this many opponents, 1 = the nuts

        if (toCall <= 0)                                    // checked to us
        {
            if (canRaise)
            {
                if (str >= 0.42 - (st.Aggr - 1) * 0.1)      // value bet
                {
                    if (str > 0.85 && r.NextDouble() < 0.2) return call;              // slow-play a monster now and then
                    if (r.NextDouble() < Math.Min(0.95, 0.2 + 0.65 * st.Aggr))
                        return BetFrac(0.4 + 0.45 * str + 0.1 * (r.NextDouble() - 0.5));
                    return call;
                }
                if (e.Street == Street.Flop && e.PreflopAggressor == p && opp <= 2 && r.NextDouble() < 0.45 + 0.2 * st.Aggr)
                    return BetFrac(0.33 + 0.2 * r.NextDouble());                      // continuation bet
                double bluffP = st.Bluff * (0.6 + 0.8 * pos) * (river ? 0.5 : 1.0) * (opp <= 1 ? 1.0 : opp == 2 ? 0.6 : 0.25);
                if (str < 0.25 && r.NextDouble() < bluffP)
                    return BetFrac(0.5 + 0.3 * r.NextDouble());                      // stab / bluff
            }
            return call;                                    // check
        }

        double need = (double)callAmt / (pot + callAmt);    // pot odds: equity needed to break even
        double effEq = eq - (0.03 + 0.14 * Math.Min(1.0, callAmt / Math.Max((double)pot, 1.0)));   // a player who bets has more than a random hand
        if (effEq >= need * (1.15 - 0.2 * (st.Loose - 1)))
        {
            if (canRaise && str >= 0.7 && r.NextDouble() < 0.1 + 0.5 * st.Aggr)
                return BetFrac(0.7 + 0.4 * r.NextDouble());                           // raise for value
            return call;
        }
        if (canRaise && !river && eq >= 0.3 && r.NextDouble() < st.Bluff * 0.5 * (0.6 + 0.8 * pos))
            return BetFrac(0.75 + 0.3 * r.NextDouble());                              // semi-bluff raise
        if (callAmt <= pot * 0.12 && r.NextDouble() < 0.05 * st.Loose) return call;   // cheap curiosity call
        return fold;
    }

    // ------------------------------------------------------------------ preflop hand strength (Chen formula)
    private static double ChenPoints(Rank rank) =>
        rank == Rank.Ace ? 10 : rank == Rank.King ? 8 : rank == Rank.Queen ? 7 : rank == Rank.Jack ? 6 : (int)rank / 2.0;

    public static int Chen(Card a, Card b)
    {
        Card hi = a, lo = b;
        if ((int)lo.Rank > (int)hi.Rank) { hi = b; lo = a; }
        double pts = ChenPoints(hi.Rank);
        if (hi.Rank == lo.Rank) return Math.Max(5, (int)Math.Ceiling(pts * 2));
        if (hi.Suit == lo.Suit) pts += 2;
        int gap = (int)hi.Rank - (int)lo.Rank - 1;
        pts -= gap <= 0 ? 0 : gap == 1 ? 1 : gap == 2 ? 2 : gap == 3 ? 4 : 5;
        if (gap <= 1 && (int)hi.Rank < 12) pts += 1;      // connected, both cards below a queen
        return (int)Math.Ceiling(pts);
    }

    /// <summary>0 = first live seat left of the button (acts first after the flop), 1 = the button.</summary>
    private static double PositionOf(GameEngine e, Player p)
    {
        int n = e.Players.Count, live = 0, index = 0;
        for (int k = 1; k <= n; k++)
        {
            var seat = e.Players[(e.Dealer + k) % n];
            if (seat.IsOut) continue;
            if (seat == p) index = live;
            live++;
        }
        return live > 1 ? (double)index / (live - 1) : 1.0;
    }

    // ------------------------------------------------------------------ Monte Carlo equity
    /// <summary>Chance (ties split) that 'hole' beats 'opponents' random hands on this board.</summary>
    public static double Equity(IReadOnlyList<Card> hole, IReadOnlyList<Card> board, int opponents, int sims, Random rng)
    {
        Span<int> deck = stackalloc int[52];
        Span<bool> used = stackalloc bool[52];
        Span<int> hero = stackalloc int[7];
        Span<int> vil = stackalloc int[7];
        int boardCount = board.Count;

        hero[0] = HandEvaluator.Code(hole[0]);
        hero[1] = HandEvaluator.Code(hole[1]);
        used[hero[0]] = true;
        used[hero[1]] = true;
        for (int i = 0; i < boardCount; i++)
        {
            int code = HandEvaluator.Code(board[i]);
            hero[2 + i] = code;
            vil[2 + i] = code;
            used[code] = true;
        }
        int m = 0;
        for (int code = 0; code < 52; code++)
            if (!used[code]) deck[m++] = code;

        int need = 5 - boardCount;
        opponents = Math.Min(opponents, (m - need) / 2);
        if (opponents < 1) return 1.0;
        int draw = need + 2 * opponents;

        double score = 0;
        for (int s = 0; s < sims; s++)
        {
            for (int i = 0; i < draw; i++)                  // partial Fisher-Yates: only the cards we need
            {
                int j = i + rng.Next(m - i);
                (deck[i], deck[j]) = (deck[j], deck[i]);
            }
            for (int i = 0; i < need; i++)
            {
                hero[2 + boardCount + i] = deck[i];
                vil[2 + boardCount + i] = deck[i];
            }
            long mine = HandEvaluator.EvaluateCodes(hero);
            int ties = 1;
            bool lost = false;
            for (int o = 0; o < opponents; o++)
            {
                vil[0] = deck[need + 2 * o];
                vil[1] = deck[need + 2 * o + 1];
                long theirs = HandEvaluator.EvaluateCodes(vil);
                if (theirs > mine) { lost = true; break; }
                if (theirs == mine) ties++;
            }
            if (!lost) score += 1.0 / ties;
        }
        return score / sims;
    }
}
}
