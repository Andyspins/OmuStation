using Content.Goobstation.Common.CCVar;
using Content.Server.Administration.Logs;
using Content.Server.Antag;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking.Rules;
using Content.Server.StationEvents;
using Content.Shared.Database;
using Content.Shared.GameTicking.Components;
using JetBrains.Annotations;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Omu.Server.StationEvents.SecretDoublePlus;

[UsedImplicitly]
public sealed class SecretDoublePlusSystem : GameRuleSystem<SecretDoublePlusComponent>
{
    [Dependency] private readonly AntagSelectionSystem _antagSelection = default!;
    [Dependency] private readonly EventManagerSystem _event = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly ILogManager _log = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

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
    }

    /// <summary>
    ///   Count all the players on the server.
    /// </summary>
    public int GetTotalPlayerCount(IList<ICommonSession> pool)
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
