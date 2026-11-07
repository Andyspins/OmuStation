namespace Content.Omu.Server.StationEvents.SecretDoublePlus.ChaosFactors;

[ImplicitDataDefinitionForInheritors]
public abstract partial class ChaosFactor
{
    public virtual void Initialize(IEntitySystemManager systemManager)
    {
        IoCManager.InjectDependencies(this);
    }

    public abstract float CalculateChaos(ChaosContext context);
}

public sealed record ChaosContext(float Ramping);
