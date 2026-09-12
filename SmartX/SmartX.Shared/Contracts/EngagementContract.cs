using System;
using System.Collections.Generic;
using System.Text;

namespace SmartX.Shared.Contracts
{
    /// <summary>
    /// Snapshot behind the "Telemetry Pulse" dashboard widget
    /// Recomputed from live telemetry each time its requested
    /// </summary>
    public sealed record EngagementContractDto(
        int HealthScore,
        int CleanStreak,
        IReadOnlyList<string> ActiveAlerts,
        DateTimeOffset GeneratedAtUtc);
}

//------------------------------------------- End Of File -------------------------------------------//