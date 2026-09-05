using System;
using System.Collections.Generic;
using System.Text;

namespace SmartX.Shared.Models
{
    /// <summary>
    /// File attachment that can be added to sensor
    /// </summary>
   
    public sealed class SensorAttachment
    {
        public required string Id { get; init; }
        public required string SensorId { get; init; }
        public required string FileName { get; init; }
        public required string ContentType { get; init; }
        public required long SizeBytes { get; init; }
        public required DateTimeOffset UploadedAtUtc { get; init; }
    }
}

//------------------------------------------- End Of File -------------------------------------------//

