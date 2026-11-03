using Content.Shared.Random;
using Robust.Shared.Prototypes;
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

    /// <summary>
    ///   How much to change chaos per second per player in control of a humanoid.
    /// </summary>
    [DataField]
    public float LivingChaosChange;

    /// <summary>
    ///   How much to change chaos per second per player in control of a ghost which can return to a body.
    /// </summary>
    [DataField]
    public float DeadChaosChange;

    /// <summary>
    ///   Whether to ignore incompatible roundstart antags. Also permits several of one antag.
    ///   Use if you want to allow really high amounts of roundstart antags.
    /// </summary>
    [DataField]
    public bool IgnoreIncompatible = false;

    /// <summary>
    ///   Does this round start with antags at all?
    /// </summary>
    [DataField]
    public bool NoRoundstartAntags = false;

    [DataField]
    public float PrimaryAntagBudget = 0.6f;

    [DataField]
    public int RoundStartAntagCount = 3;

    [DataField]
    public float SaveChaosBudgetRange = 0.3f;

    [DataField]
    public float OppositeChaosDirectionEventPenalty = 0.25f;

    [DataField]
    public float MidRoundSkipChance = 0.5f;

    [DataField]
    public float MidRoundSkipRange = 250f;

    [DataField]
    public float ChaosMatchingPower = 1.5f;

    /// <summary>
    ///   Weight table for primary roundstart antags.
    /// </summary>
    [DataField]
    public ProtoId<WeightedRandomPrototype> PrimaryAntagsWeightTable = "SecretPlusPrimary"; // Keep using the old SecretPlus tables for now

    /// <summary>
    ///   Weight table for roundstart antags picked after the primary.
    /// </summary>
    [DataField]
    public ProtoId<WeightedRandomPrototype> RoundStartAntagsWeightTable = "SecretPlus"; // Keep using the old SecretPlus tables for now
}
