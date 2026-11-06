using Content.Goobstation.Common.StationEvents.SecretPlus;
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
    ///   Chaos change multiplier we're using.
    ///   Affects midround chaos generation over time.
    ///   Rolled randomly when starting the gamerule.
    /// </summary>
    [ViewVariables]
    public float ChaosChangeVariation = 1f;

    /// <summary>
    ///   Minimum ChaosChangeVariation to generate at the start.
    /// </summary>
    [DataField]
    public float ChaosChangeVariationMin = 1f;

    /// <summary>
    ///   Maximum ChaosChangeVariation to generate at the start.
    /// </summary>
    [DataField]
    public float ChaosChangeVariationMax = 1f;

    /// <summary>
    ///   How much to bias ChaosChangeVariation towards generating closer to 1.
    ///   Higher values mean it rolls closer to 1, lower values means it rolls farther from 1.
    /// </summary>
    [DataField]
    public float ChaosChangeVariationExponent = 2f;

    /// <summary>
    ///   How much to ramp up per second.
    ///   Turns the scheduler into essentially the survival gamemode.
    ///   Linearly scales chaos point generation, event frequency, and reduces reoccurence delays.
    ///   For example, if you set this to 1/60, after 60 seconds chaos generation and event frequency will be doubled, and reoccurence delays halved.
    /// </summary>
    [DataField]
    public float SpeedRamping = 0f;

    /// <summary>
    ///   Whether to ignore whether an event can actually currently run.
    ///   Will lead to events firing earlier than normal and several times even when normally limited.
    /// </summary>
    [DataField]
    public bool IgnoreTimings = false;

    /// <summary>
    ///   Whether to ignore incompatible roundstart antags. Also permits several of one antag.
    ///   Use if you want to allow really high amounts of roundstart antags.
    /// </summary>
    [DataField]
    public bool IgnoreIncompatible = false;

    /// <summary>
    ///   All the event types that are disallowed to run.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<EventTypePrototype>> DisallowedEvents = new();

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
