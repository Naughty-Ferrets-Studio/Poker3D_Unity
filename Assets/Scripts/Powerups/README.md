# Adding a power-up

1. Make a folder `Assets/Scripts/Powerups/<Name>/` and one class `<Name>.cs` that extends `PowerUpDefinition`.
2. Add ONE line to `Core/PowerUpRegistry.cs`: `new <Name>(),`
3. Write tests in `Assets/Tests/EditMode/PowerUpTests.cs` (copy an existing one).
4. Test by hand: press Cmd/Ctrl + Shift + D in Play mode, pick it, pick a tier, Give, Use.

Rules: a power-up only touches the game through the public engine calls (`ReplaceHoleCard`, `ReplaceBoardCard`,
`RevealHoleCard`, `PeekDeck`). Read `ctx.Tier` to change behaviour per tier. Return `UseResult.Fail(...)` to refuse
without spending it. Never edit `GameEngine` for one power-up.
