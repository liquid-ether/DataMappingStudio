using System.Diagnostics.Metrics;

namespace App.Application.Diagnostics;

/// <summary>
/// Built-in metrics (System.Diagnostics.Metrics — no extra packages). Observable out of the box with
/// <c>dotnet-counters monitor --counters MappingStudio</c>, and exportable later by wiring an
/// OpenTelemetry meter provider to the "MappingStudio" meter without touching this code.
/// </summary>
public static class AppMetrics
{
    public const string MeterName = "MappingStudio";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Histogram<double> PublishDuration =
        Meter.CreateHistogram<double>("mappingstudio.publish.duration", "ms", "Duration of a publish (merge + append + snapshot rebuild).");

    public static readonly Counter<long> PublishedChanges =
        Meter.CreateCounter<long>("mappingstudio.publish.changes", "changes", "Field changes appended to the remote log by publishes.");

    public static readonly Histogram<double> RefreshDuration =
        Meter.CreateHistogram<double>("mappingstudio.refresh.duration", "ms", "Duration of a remote refresh (fold + adopt).");

    public static readonly Counter<long> RefreshApplied =
        Meter.CreateCounter<long>("mappingstudio.refresh.applied", "cells", "Remote cell values adopted into local working copies.");

    public static readonly Counter<long> PublishConflicts =
        Meter.CreateCounter<long>("mappingstudio.publish.conflicts", "conflicts", "Merge conflicts surfaced to users at publish time.");
}
