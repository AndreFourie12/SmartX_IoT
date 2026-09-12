using System;
using System.Collections.Generic;
using System.Text;

namespace SmartX.Shared.Contracts
{
    /// <summary>
    /// Backs the connection strip in the top right
    /// Shows that link is up, that the throughoutput is being measured, and the uptime is the gateway process's own
    /// </summary>
    public sealed record GatewayStatusDTO(
        double MessagesPerSecond,
        TimeSpan Uptime,
        int RegisteredSensors);
}

//------------------------------------------- End Of File -------------------------------------------//