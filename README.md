# Poker3D_Unity

Texas Hold'em for Unity 6 (6000.6).

- Dealer: Fisher-Yates shuffle with a secure RNG, burn cards, deal order starting left of the button
- DealEvents: every dealt card in order, ready to drive 3D animations
- Replay: save a hand's deck order and deal it again exactly
- Verified: hand evaluator checked against all 133,784,560 7-card hands; side pots and chip totals stress-tested

Game logic lives in Assets/Scripts/Holdem (plain C#, no Unity dependencies).
