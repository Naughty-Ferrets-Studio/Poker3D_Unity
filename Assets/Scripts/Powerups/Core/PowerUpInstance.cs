namespace Holdem.PowerUps
{
    /// <summary>What a player holds: a definition plus a tier.</summary>
    public sealed class PowerUpInstance
    {
        public readonly PowerUpDefinition Definition;
        public readonly Tier Tier;

        public PowerUpInstance(PowerUpDefinition definition, Tier tier)
        {
            Definition = definition ?? throw new System.ArgumentNullException(nameof(definition));
            Tier = tier;
        }

        public string Description => Definition.Describe(Tier);
        public override string ToString() => Definition.Name + " (" + Tier + ")";
    }
}
