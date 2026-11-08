using Content.Server.Chat.Managers;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Objectives.Systems;
using Content.Shared.Roles;
using JetBrains.Annotations;
using Robust.Shared.Timing;

namespace Content.Omu.Server.StationEvents.SecretDoublePlus.ChaosFactors;

[UsedImplicitly]
public sealed partial class AntagObjectiveFactor : ChaosFactor
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IChatManager _chat = default!;

    public Dictionary<string, ObjectiveChaosTuning> ObjectiveAmbientChaosRates = new();

    private readonly Dictionary<EntityUid, TimeSpan> _firstSeenObjective = new();

    protected override float CalculateChaos(ChaosContext context)
    {
        // Hardcode some stuff for testing
        if (ObjectiveAmbientChaosRates.Count == 0)
        {
            var steal = new ObjectiveChaosTuning
            {
                Chaos = 1.0f,
                ExpectedTime = TimeSpan.FromMinutes(2),
                DecayPeriod = TimeSpan.FromMinutes(3),
            };

            ObjectiveAmbientChaosRates["StealConditionComponent"] = steal;
        }

        var roles = _entityManager.System<SharedRoleSystem>();
        var objectives = _entityManager.System<SharedObjectivesSystem>();
        var mobs = _entityManager.System<MobStateSystem>();

        var chaos = 0f;
        var curTime = _timing.CurTime;

        var mindQuery = _entityManager.EntityQueryEnumerator<MindComponent>();
        while (mindQuery.MoveNext(out var mindId, out var mind))
        {
            if (!roles.MindIsAntagonist(mindId))
                continue;

            if (mind.OwnedEntity is not { } body || !mobs.IsAlive(body))
                continue;

            foreach (var objective in mind.Objectives)
            {
                if (objectives.IsCompleted(objective, (mindId, mind)))
                    continue;

                foreach (var component in _entityManager.GetComponents(objective))
                {
                    if (!ObjectiveAmbientChaosRates.TryGetValue(component.GetType().Name, out var tuning))
                        continue;

                    if (!_firstSeenObjective.TryGetValue(objective, out var firstSeen))
                    {
                        firstSeen = curTime;
                        _firstSeenObjective[objective] = firstSeen;
                    }

                    var timeMultiplier = 1f;
                    if (tuning.ExpectedTime != TimeSpan.Zero && firstSeen.Add(tuning.ExpectedTime) < curTime)
                    {
                        var difference = curTime.Subtract(tuning.ExpectedTime).Subtract(firstSeen);
                        var ratio = tuning.DecayPeriod != TimeSpan.Zero ? difference / tuning.DecayPeriod : 1f;

                        timeMultiplier -= Math.Clamp((float) ratio, 0, 1);
                    }

                    var localChaos = tuning.Chaos * timeMultiplier;
                    _chat.SendAdminAnnouncement($"{mind.CharacterName} has objective {objective.ToString()}, type {component.GetType().Name}, chaos {localChaos}, multiplier {timeMultiplier}");

                    chaos += localChaos;
                }
            }
        }

        return chaos;
    }
}

public sealed class ObjectiveChaosTuning
{
    public float Chaos = 0;
    public TimeSpan ExpectedTime = TimeSpan.Zero;
    public TimeSpan DecayPeriod = TimeSpan.Zero;
}
