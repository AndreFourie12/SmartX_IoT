using System;
using System.Collections.Generic;
using System.Text;

namespace SmartX.Shared.Models
{

    /// <summary>
    /// Meta Data that will define each device 
    /// What allows each device to be distinguised from one another
    /// </summary>
   
    public sealed class SensorDevice
    {
        public required string Id { get; init; } // MAC address / unique node identifier
        public required string Location { get; set; } // e.g. "Facility A / Zone 1 / Sub-Zone B"
        public required SensorCategory Category { get; set; }
        public required DateTimeOffset RegisteredAtUtc { get; init; }

        public Dictionary<string, SensorAttachment> Attachments { get; } = new();
    }
}

//------------------------------------------- End Of File -------------------------------------------//

