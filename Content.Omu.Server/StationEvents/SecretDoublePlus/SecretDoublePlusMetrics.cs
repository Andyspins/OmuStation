using Prometheus;

namespace Content.Omu.Server.StationEvents.SecretDoublePlus;

public static class SecretDoublePlusMetrics
{
    public static readonly Gauge ChaosScore = Metrics.CreateGauge(
        "secret_double_plus_chaos_score",
        "Chaos score in SecretDoublePlus"
    );

    public static readonly Counter ChaosFactor = Metrics.CreateCounter(
        "secret_double_plus_chaos_factor",
        "Chaos contribution per chaos factor for SecretDoublePlus",
        "chaos_factor_name",
        "chaos_factor_sign"
    );

    public static readonly Counter EventSelected = Metrics.CreateCounter(
        "secret_double_plus_event_selected",
        "Events selected by SecretDoublePlus",
        "event_name"
    );

    public static readonly Counter SkippedEvents = Metrics.CreateCounter(
        "secret_double_plus_skipped_events",
        "Event ticks skipped by SecretDoublePlus"
    );
}
