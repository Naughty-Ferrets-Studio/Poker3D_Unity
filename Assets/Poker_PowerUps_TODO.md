# Poker3D Power-Ups: To-Do, Rules and Test Plan

Status of the project today (from reading your zip): the poker engine, bots and 3D dealing animation are mostly done. The human player controls, the power-up system, the test tools and the prefab/Blender structure are not started. This file lists everything left, in the order I'd do it.

---

## 1. Game rules (your rules, made precise)

**"Round" means one hand of poker.** The end of the round is the moment **after the river has been played and every pot (the main pot and any side pots) has been paid out**. Power-ups are awarded after that, never before.

### 1.1 Getting a power-up
- After the river and after all pots are paid, every player who **did not fold** and qualifies by their final hand receives one power-up.
- Players who folded get nothing.
- Order of events at the end of a hand: river betting finishes, showdown, main pot and side pots paid, power-ups awarded (with the inventory choices from 1.4), next hand starts.
- The tier roll uses each player's final hand (best 5 of their 7 cards), including players who lost the showdown.
- Still open: hands that end early because everyone else folded, so the river is never reached (see Open Questions, Q1).

### 1.2 Which tier you get
Ten hand ranks, from High Card up to Royal Flush. The better the hand, the better the odds of a high tier. **Royal Flush is a guaranteed Legendary.** Every other hand can still roll any tier, so even a One Pair has a small chance at Legendary.

These numbers come from one formula (section 1.2.1), not from hand-picked guesses. They are a starting point for the simulation to tune.

| Rank | Hand | Common | Rare | Epic | Mythic | Legendary |
|---|---|---|---|---|---|---|
| 1 (lowest) | High Card | 77.1% | 17.7% | 4.1% | 0.9% | 0.2% |
| 2 | One Pair | 70.7% | 20.8% | 6.2% | 1.8% | 0.5% |
| 3 | Two Pair | 62.6% | 23.7% | 9.0% | 3.4% | 1.3% |
| 4 | Three of a Kind | 52.8% | 25.7% | 12.5% | 6.1% | 3.0% |
| 5 | Straight | 41.5% | 25.9% | 16.2% | 10.1% | 6.3% |
| 6 | Flush | 29.6% | 23.8% | 19.1% | 15.3% | 12.3% |
| 7 | Full House | 18.9% | 19.4% | 20.0% | 20.6% | 21.2% |
| 8 | Four of a Kind | 10.6% | 14.0% | 18.5% | 24.5% | 32.4% |
| 9 | Straight Flush | 5.3% | 9.0% | 15.4% | 26.1% | 44.2% |
| 10 (highest) | Royal Flush | 0% | 0% | 0% | 0% | **100%** |

Over a long game, with real hand frequencies, the overall mix of power-ups handed out is about 65% Common, 21.5% Rare, 8% Epic, 3.6% Mythic and 2% Legendary.

#### 1.2.1 How the table is made (so it stays fair when you change it)
- Each tier has a base weight that falls as the tier rises (Common is the most likely).
- Each hand rank has a strength from 0 (High Card) to 1 (Straight Flush), taken from its position in the ranking, **not** from how rare the hand is. (High Card is rarer than One Pair but weaker, and you said the higher hand must do better.)
- A stronger hand multiplies the weight of higher tiers more. Then each row is scaled to add up to 100%.
- Two knobs control everything: how fast the base weights fall, and how strongly hand strength tilts the odds. We set them so the overall Legendary rate across all hands is 2%.
- The rules this guarantees: a better hand is never worse at getting a given tier or higher, and every non-royal hand keeps a small chance at every tier.
- Royal Flush is a special case on top of the formula, always Legendary.
- To tune: change the tilt knob for steeper or flatter rewards, and the overall Legendary target for rarer or more common top tiers. The simulation (section 5) shows the effect.

How often each hand actually happens matters for balance. In 7-card poker, One Pair and High Card together are about 61% of hands, and a Flush or better is about 6%. A Royal Flush happens about once in 31,000 hands per player.

Keep the two knobs and the table in **one data file**, so tuning never touches game logic.

After the tier is rolled, the specific power-up is picked at random from the registry (equal chance each, with an optional per-power-up weight).

### 1.3 Variants by tier
A power-up is not just "bigger numbers at higher tiers". Each tier of a power-up can change **how it works**: more reliable, extra mechanics, fewer limits. Example from your design: Disguised Card is an imperfect disguise opponents can detect at Common, and a perfect disguise at Legendary.

So each power-up has two parts:
- **Definition**: one standalone file, containing the behaviour of all five tiers.
- **Instance**: what a player holds, which is the definition plus a tier.

Your folder rule stays the same: one folder per power-up, one line in the registry.

### 1.4 Inventory (max 3)
- A player holds at most **3** power-ups.
- When a 4th arrives, the player chooses one of:
  1. discard one they already hold, and keep the new one, or
  2. discard the new one.
- Merging (1.5) does not need a free slot, so a new power-up that matches a held one can always merge instead.
- Bots need a rule for this choice too (keep the higher tier; break ties by simulated win value).

### 1.5 Upgrading
- Two identical power-ups of the same tier combine into one copy at the next tier up (two Rare Disguised Cards become one Epic Disguised Card).
- Upgrading is **optional**.
- Legendary cannot be upgraded.
- Your message said "if it's the same tier as the current one it will rank up". Your rules text says "two identical power-ups of the same rarity". I'm using the second one (same power-up and same tier). See Q2.

### 1.6 Risk and reward
Nothing extra to build. This comes from the tier table: a weak hand can still win big, but rarely.

---

## 2. Open questions (please decide)

| # | Question | My suggestion |
|---|---|---|
| Q1 | What if the hand ends before the river because everyone else folded? The winner never reached the river or showed a hand. | No power-up, since there is no final hand to rank. (Alternative: a Common-weighted roll, so folding opponents can't starve the winner.) |
| Q2 | Does merging need the same power-up (my reading) or just the same tier? | Same power-up and same tier. |
| Q3 | If a merge creates a copy that matches another held power-up, does it merge again automatically? | No, ask the player each time. |
| Q4 | When can power-ups be used (any time, only on your turn, only before betting, only at certain streets)? | Each power-up declares its own allowed moments. |
| Q5 | Use once and it's gone, or reusable with a cooldown? | One-use. Easier to balance with a 3-slot limit. |
| Q6 | Are held power-ups hidden from opponents? Your Disguised Card idea ("opponents can detect") means opponents must be able to notice some things. | Held items are hidden. Activations may be visible depending on tier. |
| Q7 | Does the winner of the hand get better odds than the loser at showdown, or is it purely hand strength? | Purely hand strength, as written. |
| Q8 | Do bots get power-ups too? | Yes, otherwise balance testing means nothing. |

---

## 3. Build order (checklist)

**Progress key:** `[x]` = done, `[ ]` = not done. Last synced against the full code on 2026-10-28 (every `.cs` file read). Everything marked done is written but **has never been compiled or run in Unity**, and the repo has no tests, so treat `[x]` as "code exists", not "verified".

**Sync findings (code vs this list), and what was fixed:**
- Two Phase 0 items were already in the code but unticked; now ticked (card replacement, Replace/Peek/Reveal events).
- **Fixed:** `PokerTable3D.DealCard` used to treat every non-Hole, non-Burn event as a board card. It now handles Replace, Peek and Reveal before that code runs.
- **Fixed:** the test framework is now listed in `manifest.json`; `Readme.asset` is deleted.
- **Decided:** the folder stays `Assets/Scripts/Powerups/` (lowercase u, as it is today). Namespace is `Holdem.PowerUps`. Section 6 now uses the same spelling.
- **Still open, needs Unity:** `Powerups/`, `Core/`, `Tier.cs` and `TierOdds.cs` have no `.meta` files. Unity creates them on first open; commit them afterwards so the script GUIDs stay stable.
- **Added:** the test project (see Phase 0). Still unproven until the Test Runner has been run.
- The new `.asmdef` files also have no `.meta` yet; Unity creates them on first open. Commit them with the rest.
- Scene wiring is fine: `PokerTable3D` is attached once in `SampleScene`.
- `TierOdds` rows were checked by hand: each adds up to 1000, Royal Flush is 100% Legendary, and the "better hand never worse" rule holds. `Validate()` exists but nothing calls it yet.

### Phase 0: Fix the foundation
- [x] Add **card replacement support** to the deck and dealer (put a card back, replace a card, record it) so the end-of-hand 52-card check doesn't throw when a power-up changes a card. *(done: `Deck.Insert/Take/InsertRandom`, `CardDealer.Replace`, `GameEngine.ReplaceHoleCard/ReplaceBoardCard`; not yet compiled or tested)*
- [x] Add new deal event kinds (Replace, Peek, Reveal) so the table can animate card-changing power-ups. *(done in `CardDealer.cs` and `GameEngine.PeekDeck/RevealHoleCard`; `PokerTable3D` now plays them too, see the item below)*
- [x] **Make `PokerTable3D` handle Replace, Peek and Reveal events** *(done: `PokerTable3D.cs`. Replace swaps the card object in place and the old card slides back to the deck. Reveal flips a bot's or board card face-up for you. Peek draws nothing for now; the secret-view panel comes with the power-up UI. Known limit: revealing YOUR OWN card to others has no visual yet, because hold-to-look controls your cards. Not compiled or run yet.)*
- [x] Add `com.unity.test-framework` to `Packages/manifest.json`. *(done: version 1.4.6, the one the lock file already asks for; Unity may bump it on open)*
- [ ] Add a **game event stream** (bet, fold, chips to pot, hand result, power-up awarded, used, discarded, upgraded). The 3D table and the simulation both read it.
- [x] Add **Royal Flush** as its own 10th hand rank in `HandEvaluator` (today it is a Straight Flush with an Ace high), so the award system can tell it apart. *(done: `HandEvaluator.cs`; Royal Flush is category 9)*
- [ ] Add a structured **hand result** (who won, how much, with what hand), not just log text.
- [x] Add an `.asmdef` for the `Holdem` folder and an **EditMode test project**. *(done, not yet opened in Unity: `Scripts/Holdem/Holdem.asmdef`, `Scripts/Powerups/Holdem.PowerUps.asmdef` (needed because a test assembly cannot see loose scripts), `Tests/EditMode/Holdem.Tests.EditMode.asmdef`. Both game assemblies have `noEngineReferences` on, so they stay plain C#. `Table/` stays in the default assembly and still sees them.)*
- [x] Write the evaluator tests. *(done: `HandEvaluatorTests.cs`. Every one of the 2,598,960 five-card hands is counted against the known table (fast). The full 133,784,560 seven-card run is there too, marked `Explicit` so it only runs when you right-click it, about 15 to 60 seconds. The README claim counts as verified only after that test has passed once.)*
- [x] Write tests for the rest of the existing code. *(done, same folder: `DeckTests.cs`, `CardDealerTests.cs`, `GameEngineTests.cs` (deal order, TryAct, fold-out, a rigged showdown, chip conservation over 60 bot hands, hole/board replace, peek, reveal), `TierTests.cs` (Tier plus all of `TierOdds`), and `TestUtil.cs` with helpers like `DeckWithTop("As", "2c", ...)`.)*
- [ ] **Open the project, run Window > General > Test Runner > EditMode > Run All, and fix anything red.** None of the tests have ever been compiled or run, so expect a few small fixes on the first run. Then run the Explicit seven-card test once.
- [x] Delete template leftovers (`TutorialInfo`, `Readme.asset`). *(done: `TutorialInfo` was already gone; `Readme.asset` and its `.meta` are deleted)*

### Phase 1: Let the human play
- [x] Fold / Check-Call / Raise buttons and a raise slider, using the engine's `TryAct`. *(done: `PokerTable3D.cs`, plus an "Auto-play me" toggle for quick tests)*
- [x] Wait for the human on their turn; bots keep their current think delay. *(done)*
- [x] Your hole cards start face-down; hold the left mouse button on a card, or hold H, to look at them, and they flip back when you let go. *(done; the table tracks which cards are face-up, which a later power-up needs)*
- [ ] Show the winner and the winning hand at the end of each hand. *(not started: the winner only appears in the log lines; `_banner` stays empty after a hand. Needs the structured hand result from Phase 0.)*

### Phase 2: Power-up data model
- [x] `Tier` enum (Common, Rare, Epic, Mythic, Legendary). *(done: `PowerUps/Core/Tier.cs`, with a Legendary-can't-upgrade check and `NextUp()`)*
- [ ] Power-up definition base (id, name, description per tier, allowed timing, per-tier behaviour).
- [ ] Power-up instance (definition + tier).
- [ ] Registry file (the only file edited to add a power-up).
- [ ] Inventory on `Player` (max 3) with these operations: add, discard, merge/upgrade, use. Keep this logic in **plain C#, separate from Unity**, so it can be tested alone.
- [ ] Tier rules: Legendary cannot upgrade; merging needs same power-up and same tier.

### Phase 3: Award system
- [x] The tier-odds table from 1.2 as a data file. *(done: `PowerUps/Core/TierOdds.cs`, with `Roll` and `Validate`)*
- [ ] End-of-hand step, run **after the river and after all pots are paid**: for each non-folded player, roll tier from their final hand, pick a power-up, offer it. This needs a new engine hook ("after pots distributed") placed after `Showdown()` pays the last side pot and before the next `StartHand()`.
- [ ] Inventory-full flow: discard old, discard new, or merge.
- [ ] Bot version of that choice.
- [x] RNG is injectable, so tests can force an exact roll. *(done for `TierOdds.Roll`; the award system itself is not built yet)*

### Phase 4: Hooks in the engine
- [ ] Hook points: hand start, flop, turn, river, before showdown, after showdown, pot won, plus "player activates".
- [ ] The engine calls hooks on each player's held power-ups and knows nothing about what any of them do.
- [ ] Build your first two power-ups (powerone, powertwo) with all five tiers each.
- [ ] Information model: who is allowed to see what (needed for Peek, Reveal and Disguise-style powers).

### Phase 5: Test tools (section 4)
- [ ] Scenario builder for single-power-up tests.
- [ ] Per-power-up, per-tier test suite.
- [ ] Debug sandbox scene.

### Phase 6: Simulation and analytics (section 5)
- [ ] Headless runner.
- [ ] Per-power-up and per-tier statistics.
- [ ] Pair and triple combination analysis.
- [ ] Report output (CSV and a markdown summary).

### Phase 7: Bots that use power-ups
- [ ] A usage rule for each power-up, so bots use it sensibly.
- [ ] Without this, win rates show how bad the bots are at using power-ups, not how good the power-ups are.

### Phase 8: Interface and polish
- [ ] Inventory UI (3 slots, tier colours, discard and merge prompts).
- [ ] Power-up buttons during play.
- [ ] Replace `OnGUI` with proper UI.
- [ ] Prefabs for cards, chips, dealer button, table (with the current code-built objects as fallback).
- [ ] Chip visuals.
- [ ] Dealer hand and activation animations by tier.
- [ ] Swap in the Blender models.

---

## 4. Testing one power-up without playing a game

The goal is to load a situation, trigger one power-up, and check the result in under a second. Use these layers, cheapest first.

### 4.1 Pure logic tests (EditMode, no scene)
For each power-up and each tier, a test that:
1. sets up a **known deck order** (your engine already supports this through a replay deck),
2. gives player A that power-up at that tier,
3. moves the game to the street where it can be used,
4. activates it,
5. checks the exact outcome (cards, chips, what opponents can see, that the 52-card check still passes).

Also test the "can this be used right now?" rule, and what happens if it is used twice.

### 4.2 Scenario builder
A small helper that creates a game from a description instead of playing into it:
- stack sizes, blinds and which seat is the dealer,
- each player's hole cards,
- board cards (none, flop, turn or river) and the cards still in the deck,
- who holds what power-up at what tier,
- which street it is and whose turn.

This is what lets a test start at "river, you hold a flush draw, deck top is the nine of hearts" without playing the first three streets.

### 4.3 Forced rolls
Because the RNG can be injected, tests can say "the next award rolls Epic and picks powerone". This also tests the inventory-full flow quickly.

### 4.4 Inventory and merge tests
Pure tests, no poker:
- 3 held, 4th arrives: discard old, discard new, merge.
- Two Rare of the same power-up become one Epic.
- Legendary refuses to merge.
- A merge result that matches a third held copy (depends on Q3).

### 4.5 Sandbox scene (for looking at it)
A debug panel in Unity with:
- dropdowns to pick the power-up and the tier,
- a "give to me" button,
- a "deal rigged hand" button (fixed seed or a chosen card order),
- a "step" button that advances one street or one action,
- a "re-run same hand" button (replays the saved deck order).

This is for checking how a power-up looks and feels. Logic correctness is checked by 4.1 to 4.4.

### 4.6 Fixed-seed replay
Every hand can be reproduced from its deck order. When a simulation finds a strange result, save that hand and replay it in the sandbox.

---

## 5. Finding the strongest power-ups and combinations

### 5.1 Headless simulation
The poker engine has no Unity dependency, so it can run thousands of hands per minute with no graphics (a test project or a command-line runner). For balance runs, lower the bots' equity simulation count to speed things up.

### 5.2 What to record per hand
- which power-ups (and tiers) each player held,
- which were used, and when,
- the hand result (who won, chips won or lost),
- net chips gained or lost per hand (a better signal than win/loss alone),
- how often a player reached showdown.

### 5.3 Single power-up analysis
For each power-up, at each tier:
- **Win rate** and **net chips per hand** against a baseline player with no power-ups.
- **Use rate**: how often it was actually used before the hand ended.
- **Value when used**: results in hands where it was used, compared with similar hands where it wasn't.
- **Pick rate** under the discard rules (what players keep and what they throw away).

### 5.4 Removing luck: paired runs
Play the same deck order twice, once with the power-up and once without. The difference is mostly the power-up and not the cards. This needs far fewer hands to be reliable.

### 5.5 How many hands
Rough guide: about 10,000 hands per configuration gives about plus or minus 1 percentage point on a win rate at 95% confidence. Detecting smaller differences needs more, or paired runs. Always show the margin of error next to the number.

### 5.6 Combinations
With a 3-slot inventory, the number of combinations grows fast (power-ups times five tiers, chosen three at a time). So:
1. Test all **pairs** first and calculate **synergy**: the pair's result minus what you'd expect from adding the two singles. Positive means they work together, negative means they get in each other's way.
2. Take the top pairs and extend them to **triples**.
3. For the full space, use random sampling or a simple evolutionary search (keep the best loadouts, mutate them, repeat), instead of testing everything.
4. Test combos **at the same tier** and **mixed tiers**, because tier odds will make mixed inventories the normal case.

### 5.7 Full-game simulation
Single-power-up tests use a fixed loadout. Also run the **whole loop** (award, inventory limit, discards, upgrades) over long games with bots, and record:
- which power-ups end up in inventories,
- how fast players reach higher tiers,
- whether a snowball effect makes the first leader unbeatable,
- how often Legendary appears.

### 5.8 Outputs
- A CSV per run (one row per power-up, tier or combo).
- A markdown summary: top and bottom power-ups, top synergies, anything that wins far more or less than the rest.
- Optionally, a Unity editor window with a sortable table.

### 5.9 Balance targets (to agree on)
For example: no single power-up adds more than a set percentage of win rate over baseline, each tier is clearly stronger than the one below, and no tier-for-tier combo dominates. We decide the actual numbers after the first results.

---

## 6. Suggested folder layout

Your current folders are `Scripts/Holdem` and `Scripts/Table`. I'd keep them and add:

```
Assets/Scripts/
  Holdem/                  (engine, existing)
  Table/                   (3D table, existing)
  Powerups/                (real folder name today; keep this spelling or rename once)
    Core/                  (tier enum, definition and instance, inventory, award system, registry)
    powerone/
    powertwo/
    powerthree/
    powerfour/
  Simulation/              (headless runner, statistics, reports)
Assets/Tests/
  EditMode/                (written: evaluator, deck, dealer, engine, tier odds. To come: inventory, award, per-power-up tests)
```

---

## 7. What I'd do next

1. Run the tests in Unity and fix anything red (Phase 0, last item). Everything after this builds on that code.
2. Hand result and game event stream (Phase 0), so the table can show the winner and the award step knows who won with what.
3. End-of-hand hook, then the power-up data model and inventory with tests (Phases 3 and 2).
4. Your first two power-ups, tested through the scenario builder, then the simulation.

To go further I need:
- your answers to Q1 to Q8,
- what powerone and powertwo do at each tier.