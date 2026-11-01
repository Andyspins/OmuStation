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
    EntityPrototype? proto,
    GameRuleComponent? ruleComp,
    float weight,
    float chaosScore)
{
    /// <summary>
    ///   The station event prototype
    /// </summary>
    public readonly EntityPrototype? Proto = proto;
    public readonly GameRuleComponent? RuleComp = ruleComp;
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
        var primaryTargetChaos = -scheduler.Comp.ChaosScore * scheduler.Comp.PrimaryAntagBudget;
        var adjustedPrimaryWeights = BuildChaosWeightedDict(scheduler.Comp, primaryEvents, primaryTargetChaos);
        var primary = PickAndLog(adjustedPrimaryWeights, primaryTargetChaos);
        StartRule(scheduler, primary, false);

        // Keep selecting antags until we've fulfilled our chaos budget
        var remainingAntagsToPick = scheduler.Comp.AdditionalRoundStartAntags;
        var targetChaos = -scheduler.Comp.ChaosScore / remainingAntagsToPick;
        var adjustedWeights = BuildChaosWeightedDict(scheduler.Comp, events, targetChaos);

        adjustedWeights.Remove(primary);

        const int iterationGuard = 50; // Cap our iteration count just in-case
        for (var i = 0; scheduler.Comp.ChaosScore < 0 && i < iterationGuard; i++)
        {
            var pick = PickAndLog(adjustedWeights, targetChaos);
            adjustedWeights.Remove(pick);
            StartRule(scheduler, pick);
        }
    }

    protected override void ActiveTick(EntityUid uid, SecretDoublePlusComponent scheduler, GameRuleComponent gameRule, float frameTime)
    {
        var count = CountActivePlayers();
        var speedup = _event.EventSpeedup;

        // TODO: Count active/unfinished antag objectives as positive chaos score
        scheduler.ChaosScore += count.Players * scheduler.LivingChaosChange * frameTime * speedup;
        scheduler.ChaosScore += count.Ghosts * scheduler.DeadChaosChange * frameTime * speedup;

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

        if (ShouldSkipTick(scheduler))
        {
            LogMessage("Skipping event tick");
            return;
        }

        var chaosDampening = 1f;
        if (scheduler.ChaosScore < 0)
            chaosDampening -= _random.NextFloat(scheduler.SaveChaosBudgetRange);

        var targetChaos = -scheduler.ChaosScore * chaosDampening;

        var possibleEvents = GetPossibleEvents(uid);
        var weights = BuildChaosWeightedDict(scheduler, possibleEvents, targetChaos);

        if (weights.Count == 0)
            return;

        var selected = PickAndLog(weights, targetChaos);
        StartRule((uid, scheduler), selected, false);
    }

    /// <summary>
    ///   Adds and, optionally, starts a gamerule while respecting rules with variable chaos.
    /// </summary>
    private void StartRule(Entity<SecretDoublePlusComponent> scheduler, SelectedEvent selected, bool doStart = true)
    {
        if (selected.Proto == null)
            return;

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

        // TODO: Replace manual fake with vars
        var available = _event.AvailableEvents(true, int.MaxValue, TimeSpan.MaxValue);
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

    private Dictionary<SelectedEvent, float> BuildChaosWeightedDict(SecretDoublePlusComponent scheduler, List<SelectedEvent> events, float targetChaos)
    {
        var adjustedWeights = new Dictionary<SelectedEvent, float>();
        foreach (var selected in events)
        {
            // We adjust each event's weight down by checking how far away from the target score it is
            var ratio = MathF.Abs(selected.ChaosScore / targetChaos);
            var rawFitScore = MathF.Min(ratio, 1 / ratio);
            var fitScore = MathF.Pow(rawFitScore, scheduler.ChaosMatchingPower);

            // For a target chaos of 50, -50 and 150 are equidistant, but overshooting and
            // picking 150 is likely better for the round than picking -50 and moving in the
            // wrong direction - so we penalize events with flipped signs by a configurable amount
            if (targetChaos * selected.ChaosScore < 0)
                fitScore *= scheduler.OppositeChaosDirectionEventPenalty;

            adjustedWeights[selected] = selected.Weight * fitScore;
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

    private bool ShouldSkipTick(SecretDoublePlusComponent scheduler)
    {
        if (scheduler.MidRoundSkipChance <= 0f)
            return false;

        var quotient = MathF.Abs(scheduler.ChaosScore) / scheduler.MidRoundSkipRange;
        var halvings = MathF.Max(0, quotient - 1);

        var skipChance = MathF.Min(1f, MathF.Pow(0.5f, halvings) * scheduler.MidRoundSkipChance);
        return _random.Prob(skipChance);
    }

    private SelectedEvent PickAndLog(Dictionary<SelectedEvent, float> weights, float? targetChaos = null)
    {
        var selected = _random.Pick(weights);
        var weightSum = weights.Values.Sum();
        LogMessage(
            $"Attempting to run {selected.Proto?.ID ?? "NoOp"}, \n" +
            $"| Chaos Value: {selected.ChaosScore}, \n" +
            $"| Target Chaos: {targetChaos?.ToString() ?? "N/A"} \n" +
            $"| Raw Weight: {selected.Weight}, \n" +
            $"| Chaos Adjusted Weight: {weights[selected]}, \n" +
            $"| Weight Sum: {weightSum}, \n" +
            $"| Roll Chance: {weights[selected] / weightSum * 100}%"
        );

        return selected;
    }

    /// <summary>
    ///   Count all the players on the server.
    /// </summary>
    private int GetTotalPlayerCount(IList<ICommonSession> pool)
    {
        return _antagSelection.GetTotalPlayerCount(pool) + _event.PlayerCountBias;
    }

    /// <summary>
    ///   Count the active players and ghosts on the server to determine how chaos changes.
    /// </summary>
    private PlayerCount CountActivePlayers()
    {
        var allPlayers = _playerManager.Sessions.ToList();
        var count = new PlayerCount();
        foreach (var player in allPlayers)
        {
            // TODO: A
            if (player.AttachedEntity != null)
            {
                // TODO: Consider a custom component here instead of HumanoidAppearanceComponent to represent
                //        "significant enough to count as a whole player"
                if (HasComp<HumanoidAppearanceComponent>(player.AttachedEntity))
                    count.Players += 1;
                // don't count bodyless ghosts aka say roundstart spectators
                // not reliable but works
                else if (TryComp<GhostComponent>(player.AttachedEntity, out var ghost) && ghost.CanReturnToBody)
                    count.Ghosts += 1;
            }
        }

        count.Players += _event.PlayerCountBias;

        return count;
    }

    private void LogMessage(string message, bool showChat=true)
    {
        // TODO: LogMessage strings all require localization.
        _adminLogger.Add(LogType.SecretDoublePlus, showChat?LogImpact.Medium:LogImpact.High, $"{message}");
        if (showChat)
            _chat.SendAdminAnnouncement("SecretDoublePlus " + message);
    }
}
