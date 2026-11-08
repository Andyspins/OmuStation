using System.Linq;
using Content.Server.Chat.Managers;
using Content.Server.Objectives.Components;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Content.Shared.Objectives.Systems;
using Content.Shared.Roles;
using JetBrains.Annotations;
using Robust.Shared.Timing;

namespace Content.Omu.Server.StationEvents.SecretDoublePlus.ChaosFactors;

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

public enum AmbientObjectiveChaosType
{
    Steal,
    Kill,
}

[UsedImplicitly]
public sealed partial class AntagObjectiveFactor : ChaosFactor
{
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    [DataField]
    public Dictionary<AmbientObjectiveChaosType, AmbientObjectiveChaos> AmbientChaosRates = new();

    private MobStateSystem _mobs = default!;
    private SharedObjectivesSystem _objectives = default!;
    private SharedRoleSystem _roles = default!;

    private readonly Dictionary<EntityUid, TimeSpan> _firstSeenObjective = new();

    private static readonly Dictionary<Type, AmbientObjectiveChaosType> TypeMapping = new()
    {
        { typeof(StealConditionComponent), AmbientObjectiveChaosType.Steal },
        { typeof(KillPersonConditionComponent), AmbientObjectiveChaosType.Kill },
    };

    public override void Initialize()
    {
        base.Initialize();

        _mobs = _entityManager.System<MobStateSystem>();
        _objectives = _entityManager.System<SharedObjectivesSystem>();
        _roles = _entityManager.System<SharedRoleSystem>();
    }

    protected override float CalculateChaos(ChaosContext context)
    {
        var chaos = 0f;
        var curTime = _timing.CurTime;

        var mindQuery = _entityManager.EntityQueryEnumerator<MindComponent>();
        while (mindQuery.MoveNext(out var mindId, out var mind))
        {
            if (!_roles.MindIsAntagonist(mindId))
                continue;

            if (mind.OwnedEntity is not { } body || !_mobs.IsAlive(body))
                continue;

            chaos += mind.Objectives
                .Where(objective => !_objectives.IsCompleted(objective, (mindId, mind)))
                .Sum(objective => CalculateChaosForObjective(objective, curTime));
        }

        _chat.SendAdminAnnouncement($"AntagObjective Factor adding {chaos} chaos per second");
        return chaos;
    }

    private float CalculateChaosForObjective(EntityUid objective, TimeSpan curTime)
    {
        var chaos = 0f;
        foreach (var component in _entityManager.GetComponents(objective))
        {
            if (!TypeMapping.TryGetValue(component.GetType(), out var componentType))
                continue;

            if (!AmbientChaosRates.TryGetValue(componentType, out var ambient))
                continue;

            if (!_firstSeenObjective.TryGetValue(objective, out var firstSeen))
            {
                firstSeen = curTime;
                _firstSeenObjective[objective] = firstSeen;
            }

            chaos += ambient.Chaos * GetTimeDecayMultiplier(ambient, firstSeen, curTime);
        }

        return chaos;
    }

    private float GetTimeDecayMultiplier(AmbientObjectiveChaos ambient, TimeSpan firstSeen, TimeSpan curTime)
    {
        if (ambient.ExpectedTime == TimeSpan.Zero || firstSeen.Add(ambient.ExpectedTime) >= curTime)
            return 1f;

        var difference = curTime.Subtract(ambient.ExpectedTime).Subtract(firstSeen);
        var ratio = ambient.DecayPeriod != TimeSpan.Zero ? difference / ambient.DecayPeriod : 1f;

        return 1f - Math.Clamp((float) ratio, 0, 1);
    }
}
