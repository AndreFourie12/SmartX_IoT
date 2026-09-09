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
        // Unique ID of the attachment
        public required string Id { get; init; }

        // ID of the sensor where the attachment is on
        public required string SensorId { get; init; }

        // Name given to the attachment
        public required string FileName { get; init; }

        // Type of file e.g. doc, pdf, doc, html
        public required string ContentType { get; init; }

        // Size of the attachment, prevents files that are too big being saved
        public required long SizeBytes { get; init; }

        // Time the attachment was added
        public required DateTimeOffset UploadedAtUtc { get; init; }
    }
}

//------------------------------------------- End Of File -------------------------------------------//

