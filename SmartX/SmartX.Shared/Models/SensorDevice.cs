using System;
using System.Collections.Concurrent;
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
        // MAC address / unique node identifier
        public required string Id { get; init; }

        // e.g. "Facility A / Zone 1 / Sub-Zone B"
        public required string Location { get; set; } 

        // Category of device, in SensorCategory
        public required SensorCategory Category { get; set; }

        // Time device was registered
        public required DateTimeOffset RegisteredAtUtc { get; init; }

        // Attachment added to the sensor, information of attachment stored in SensorAttachment
        public ConcurrentDictionary<string, SensorAttachment> Attachments { get; } = new();
    }
}

//------------------------------------------- End Of File -------------------------------------------//

