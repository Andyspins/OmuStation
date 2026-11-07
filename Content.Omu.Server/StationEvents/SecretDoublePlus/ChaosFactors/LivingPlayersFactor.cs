using System.Linq;
using Content.Shared.Humanoid;
using JetBrains.Annotations;
using Robust.Server.Player;

namespace Content.Omu.Server.StationEvents.SecretDoublePlus.ChaosFactors;

[UsedImplicitly]
public sealed partial class LivingPlayersFactor : ChaosFactor
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    /// <summary>
    ///   How much to change chaos per second per player in control of a humanoid.
    /// </summary>
    [DataField]
    public float LivingChaosChange;

    public override float CalculateChaos(ChaosContext context)
    {
        var playerCount = 0;
        foreach (var player in _playerManager.Sessions.ToList())
        {
            if (player.AttachedEntity == null)
                continue;

            if (_entityManager.HasComponent<HumanoidAppearanceComponent>(player.AttachedEntity))
                playerCount++;
        }

        return playerCount * LivingChaosChange * context.Ramping;
    }
}
