using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Omu.Server.StationEvents.SecretDoublePlus;

[RegisterComponent, AutoGenerateComponentPause, Access(typeof(SecretDoublePlusSystem))]
public sealed partial class SecretDoublePlusComponent : Component
{
    /// <summary>
    ///   How long until the next check for an event runs
    ///   Default value is how long until first event is allowed
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan TimeNextEvent;

    /// <summary>
    ///   Minimum interval between events
    /// </summary>
    [DataField]
    public TimeSpan EventIntervalMin;

    /// <summary>
    ///   Maximum interval between events
    /// </summary>
    [DataField]
    public TimeSpan EventIntervalMax;

    /// <summary>
    ///   The current chaos score.
    ///   Lower (negative - so the number is actually higher) values will tend to spawn higher-impact events.
    ///   Can be and usually is negative.
    /// </summary>
    [DataField]
    public float ChaosScore = 0;

    /// <summary>
    ///   The minimum amount of chaos to generate at the start per player.
    ///   Normally used for roundstart antags, but will still generate if they are disabled and will then be spent on midround events.
    /// </summary>
    [DataField]
    public float MinStartingChaos;

    /// <summary>
    ///   The maximum amount of chaos to generate at the start per player.
    /// </summary>
    [DataField]
    public float MaxStartingChaos;
}
