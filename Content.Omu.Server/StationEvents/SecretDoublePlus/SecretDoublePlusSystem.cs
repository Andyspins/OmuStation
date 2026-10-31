using System.Linq;
using Content.Goobstation.Common.CCVar;
using Content.Goobstation.Server.StationEvents.Components;
using Content.Server.Administration.Logs;
using Content.Server.Antag;
using Content.Server.Antag.Components;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.StationEvents;
using Content.Server.StationEvents.Components;
using Content.Shared.Database;
using Content.Shared.GameTicking.Components;
using Content.Shared.Random.Helpers;
using Content.Shared.Ghost;
using Content.Shared.Humanoid;
using JetBrains.Annotations;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Omu.Server.StationEvents.SecretDoublePlus;

/// <summary>
///   Temporary class for caching data.
/// </summary>
public sealed class SelectedEvent(
    EntityPrototype proto,
    GameRuleComponent ruleComp,
    float weight,
    float chaosScore)
{
    /// <summary>
    ///   The station event prototype
    /// </summary>
    public readonly EntityPrototype Proto = proto;
    public readonly GameRuleComponent RuleComp = ruleComp;
    public readonly float Weight = weight;
    public readonly float ChaosScore = chaosScore;
}

public sealed class PlayerCount
{
    public int Players;
    public int Ghosts;
}

[UsedImplicitly]
public sealed class SecretDoublePlusSystem : GameRuleSystem<SecretDoublePlusComponent>
{
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IComponentFactory _factory = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ILogManager _log = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    [Dependency] private readonly AntagSelectionSystem _antagSelection = default!;
    [Dependency] private readonly EventManagerSystem _event = default!;
    [Dependency] private readonly GameTicker _ticker = default!;

    // cvars
    private float _minimumTimeUntilFirstEvent;
    private float _roundstartChaosScoreMultiplier;

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = _log.GetSawmill("secret_double_plus");

        Subs.CVar(_cfg, GoobCVars.MinimumTimeUntilFirstEvent, value => _minimumTimeUntilFirstEvent = value, true);
        Subs.CVar(_cfg, GoobCVars.RoundstartChaosScoreMultiplier, value => _roundstartChaosScoreMultiplier = value, true);
    }

    protected override void Added(EntityUid uid, SecretDoublePlusComponent scheduler, GameRuleComponent gameRule, GameRuleAddedEvent args)
    {
        LogMessage("Starting SecretDoublePlus");

        var totalPlayers = GetTotalPlayerCount(_playerManager.Sessions);
        scheduler.ChaosScore = -1 *
            _random.NextFloat(scheduler.MinStartingChaos * totalPlayers, scheduler.MaxStartingChaos * totalPlayers) *
            _roundstartChaosScoreMultiplier;

        LogMessage($"Initial chaos score: {scheduler.ChaosScore} (based on {totalPlayers} total players)");
        TrySpawnRoundstartAntags((uid, scheduler));
    }


    private void TrySpawnRoundstartAntags(Entity<SecretDoublePlusComponent> scheduler)
    {
        var primaryWeightList = _prototypeManager.Index(scheduler.Comp.PrimaryAntagsWeightTable);
        var primaryWeights = primaryWeightList.Weights.ToDictionary();
        var primaryEvents = BuildSelectedEventsFromDict(primaryWeights);

        var weightList = _prototypeManager.Index(scheduler.Comp.RoundStartAntagsWeightTable);
        var weights = weightList.Weights.ToDictionary();
        var events = BuildSelectedEventsFromDict(weights);

        // TODO: We need to include rule validation
        // Select and start our primary antag
        var primaryTargetChaos = -scheduler.Comp.ChaosScore;
        var adjustedPrimaryWeights = BuildChaosWeightedDict(primaryEvents, primaryTargetChaos);
        var primary = _random.Pick(adjustedPrimaryWeights);
        StartRule(scheduler, primary, false);

        // Keep selecting antags until we've fulfilled our chaos budget
        var targetChaos = -scheduler.Comp.ChaosScore;
        var adjustedWeights = BuildChaosWeightedDict(events, targetChaos);
        const int iterationGuard = 50; // Cap our iteration count just in-case
        for (var i = 0; scheduler.Comp.ChaosScore < 0 && i < iterationGuard; i++)
        {
            var pick = _random.Pick(adjustedWeights);
            adjustedWeights.Remove(pick);
            StartRule(scheduler, pick);
        }
    }

    protected override void ActiveTick(EntityUid uid, SecretDoublePlusComponent scheduler, GameRuleComponent gameRule, float frameTime)
    {
        // TODO: Update current chaos score based on tick

        var curTime = _timing.CurTime;
        if (curTime < scheduler.TimeNextEvent)
            return;

        // This is the first event, add an automatic delay
        if (scheduler.TimeNextEvent == TimeSpan.Zero)
        {
            var time = _minimumTimeUntilFirstEvent;
            scheduler.TimeNextEvent = curTime + TimeSpan.FromSeconds(time);
            LogMessage($"Started, first event in {time} seconds");
            return;
        }

        var amt = TimeSpan.FromSeconds(_random.NextDouble(scheduler.EventIntervalMin.TotalSeconds, scheduler.EventIntervalMax.TotalSeconds));
        scheduler.TimeNextEvent = curTime + amt;

        LogMessage($"Chaos score: {scheduler.ChaosScore}, Next event at: {_ticker.RoundDuration() + amt}");

        var targetChaos = -scheduler.ChaosScore;
        var possibleEvents = GetPossibleEvents(uid);
        var weights = BuildChaosWeightedDict(possibleEvents, targetChaos);

        var selected = _random.Pick(weights);
        StartRule((uid, scheduler), selected, false);
    }

    /// <summary>
    ///   Adds and, optionally, starts a gamerule while respecting rules with variable chaos.
    /// </summary>
    private void StartRule(Entity<SecretDoublePlusComponent> scheduler, SelectedEvent selected, bool doStart = true)
    {
        var ruleUid = _ticker.AddGameRule(selected.Proto.ID);
        scheduler.Comp.ChaosScore += selected.ChaosScore;
        if (doStart)
            _ticker.StartGameRule(ruleUid);
    }

    private List<SelectedEvent> BuildSelectedEventsFromDict(Dictionary<string, float> weights)
    {
        var selectedEvents = new List<SelectedEvent>();
        foreach (var (name, weight) in weights)
        {
            if (!_prototypeManager.TryIndex(name, out var entProto))
                continue;

            if (!entProto.TryGetComponent<GameRuleComponent>(out var ruleComp, _factory))
                continue;

            var chaos = GetChaosScore(entProto, ruleComp);
            selectedEvents.Add(new SelectedEvent(entProto, ruleComp, weight, chaos));
        }

        return selectedEvents;
    }

    private List<SelectedEvent> GetPossibleEvents(EntityUid uid)
    {
        if (!TryComp<SelectedGameRulesComponent>(uid, out var selectedRules))
            return [];
        var available = _event.AvailableEvents();
        if (!_event.TryBuildLimitedEvents(selectedRules.ScheduledGameRules, available, out var possibleEvents))
            return [];

        var result = new List<SelectedEvent>();
        foreach (var (proto, eventComp) in possibleEvents)
        {
            if (!proto.TryGetComponent<GameRuleComponent>(out var gameRule, _factory))
                continue;

            var chaos = GetChaosScore(proto, gameRule);

            result.Add(new SelectedEvent(proto, gameRule, eventComp.Weight, chaos));
        }

        return result;
    }

    private Dictionary<SelectedEvent, float> BuildChaosWeightedDict(List<SelectedEvent> events, float targetChaos)
    {
        var adjustedWeights = new Dictionary<SelectedEvent, float>();
        foreach (var selected in events)
        {
            var ratio = selected.ChaosScore / targetChaos;
            var absoluteDistance = MathF.Abs(1 - ratio);

            var chaosAdjustedWeight = selected.Weight / (1 + absoluteDistance);
            adjustedWeights[selected] = chaosAdjustedWeight;
        }

        return adjustedWeights;
    }

    private float GetChaosScore(EntityPrototype ruleProto, GameRuleComponent ruleComp)
    {
        if (!ruleProto.TryGetComponent<AntagSelectionComponent>(out var selection, _factory))
            return ruleComp.ChaosScore ?? 0;

        var any = false;
        var score = 0f;

        var players = GetTotalPlayerCount(_playerManager.Sessions);
        foreach (var def in selection.Definitions)
        {
            if (def.ChaosScore == null)
                continue;

            any = true;
            var count = _antagSelection.GetTargetAntagCount((EntityUid.Invalid, selection), players, def);
            score += def.ChaosScore.Value * count;
        }

        if (any)
            return score;

        return ruleComp.ChaosScore ?? 0;
    }

    /// <summary>
    ///   Count all the players on the server.
    /// </summary>
    private int GetTotalPlayerCount(IList<ICommonSession> pool)
    {
        return _antagSelection.GetTotalPlayerCount(pool) + _event.PlayerCountBias;
    }

    private void LogMessage(string message, bool showChat=true)
    {
        // TODO: LogMessage strings all require localization.
        _adminLogger.Add(LogType.SecretDoublePlus, showChat?LogImpact.Medium:LogImpact.High, $"{message}");
        if (showChat)
            _chat.SendAdminAnnouncement("SecretDoublePlus " + message);
    }
}
