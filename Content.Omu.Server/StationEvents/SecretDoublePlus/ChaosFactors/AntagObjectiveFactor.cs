using System.Linq;
using Content.Omu.Shared.StationEvents.SecretDoublePlus;
using Content.Server.Chat.Managers;
using Content.Server.Objectives.Components;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Content.Shared.Objectives.Systems;
using Content.Shared.Roles;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Omu.Server.StationEvents.SecretDoublePlus.ChaosFactors;

[UsedImplicitly]
public sealed partial class AntagObjectiveFactor : ChaosFactor
{
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    [DataField]
    public ProtoId<AmbientObjectiveChaosRatesPrototype> ChaosRates;

    private AmbientObjectiveChaosRatesPrototype _rates = default!;
    private MobStateSystem _mobs = default!;
    private SharedObjectivesSystem _objectives = default!;
    private SharedRoleSystem _roles = default!;

    private readonly Dictionary<EntityUid, TimeSpan> _firstSeenObjective = new();

    private static readonly Dictionary<string, AmbientPresenceChaosType> PresenceTypeMap = new()
    {
        { "Zombie", AmbientPresenceChaosType.Zombie },
        { "InitialInfected", AmbientPresenceChaosType.Zombie },
    };

    private static readonly Dictionary<Type, AmbientObjectiveChaosType> ObjectiveTypeMapping = new()
    {
        { typeof(StealConditionComponent), AmbientObjectiveChaosType.Steal },
        { typeof(KillPersonConditionComponent), AmbientObjectiveChaosType.Kill },
    };

    public override void Initialize()
    {
        base.Initialize();

        _rates = _proto.Index(ChaosRates);

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

            chaos += CalculateChaosForRole((mindId, mind), curTime);
            chaos += mind.Objectives
                .Where(objective => !_objectives.IsCompleted(objective, (mindId, mind)))
                .Sum(objective => CalculateChaosForObjective(objective, curTime));
        }

        _chat.SendAdminAnnouncement($"AntagObjective Factor adding {chaos} chaos per second");
        return chaos;
    }

    private float CalculateChaosForRole(Entity<MindComponent?> mind, TimeSpan curTime)
    {
        var chaos = 0f;
        foreach (var role in _roles.MindGetAllRoleInfo(mind))
        {
            if (!PresenceTypeMap.TryGetValue(role.Prototype, out var presenceType))
                continue;

            if (!_rates.PresenceRates.TryGetValue(presenceType, out var presence))
                continue;

            var firstSeen = GetFirstSeen(mind.Owner, curTime);
            chaos += presence.Chaos * GetTimeDecayMultiplier(presence, firstSeen, curTime);
        }

        return chaos;
    }

    private float CalculateChaosForObjective(EntityUid objective, TimeSpan curTime)
    {
        var chaos = 0f;
        foreach (var component in _entityManager.GetComponents(objective))
        {
            if (!ObjectiveTypeMapping.TryGetValue(component.GetType(), out var componentType))
                continue;

            if (!_rates.ObjectiveRates.TryGetValue(componentType, out var ambient))
                continue;

            var firstSeen = GetFirstSeen(objective, curTime);
            chaos += ambient.Chaos * GetTimeDecayMultiplier(ambient, firstSeen, curTime);
        }

        return chaos;
    }

    private TimeSpan GetFirstSeen(EntityUid id, TimeSpan curTime)
    {
        if (_firstSeenObjective.TryGetValue(id, out var firstSeen))
            return firstSeen;

        _firstSeenObjective[id] = curTime;
        return curTime;
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
