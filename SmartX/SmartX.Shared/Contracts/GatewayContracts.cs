namespace SmartX.Shared.Contracts
{
    /// <summary>
    /// Backs the connection strip in the top right
    /// Shows that link is up, that the throughoutput is being measured, and the uptime is the gateway process's own
    /// CriticalAlerts drives the red dot on the Command Stream tab, visible from every pillar.
    /// </summary>
    public sealed record GatewayStatusDto(
        double MessagesPerSecond,
        TimeSpan Uptime,
        int RegisteredSensors,
        int CriticalAlerts = 0);
}

//------------------------------------------- End Of File -------------------------------------------//
