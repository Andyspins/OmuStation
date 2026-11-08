using Robust.Shared.Prototypes;

namespace Content.Omu.Shared.StationEvents.SecretDoublePlus;

[Prototype]
public sealed partial class AmbientObjectiveChaosRatesPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public Dictionary<AmbientObjectiveChaosType, AmbientObjectiveChaos> ObjectiveRates = new();

    [DataField]
    public Dictionary<AmbientPresenceChaosType, AmbientObjectiveChaos> PresenceRates = new();
}

public enum AmbientObjectiveChaosType
{
    Kill,
    Steal,
}

public enum AmbientPresenceChaosType
{
    Chimera,
    Zombie,
}

[DataDefinition]
public sealed partial class AmbientObjectiveChaos
{
    [DataField]
    public float Chaos = 0;

    [DataField]
    public TimeSpan ExpectedTime = TimeSpan.Zero;

    [DataField]
    public TimeSpan DecayPeriod = TimeSpan.Zero;
}
