using SmartX.Shared.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace SmartX.Shared.Contracts
{
    /// <summary>
    /// What the client submits when creating a sensor
    /// Contains what the clients know/controls about the sensor
    /// Client -> API
    /// </summary>
    public sealed record SensorRegistrationRequest(
        string id,
        string Location,
        SensorCategory Category);
    //----------------------------------------------------------//


    /// <summary>
    /// What the server sends back when client asks to see a sensor
    /// Contains everything currentrly true about the sensor
    /// </summary>
    public sealed record SensorDto(
        string Id,
        string Location,
        SensorCategory Category,
        DateTimeOffset RegisteredAtUtc,
        int AttachmentCount,
        int TelemetryReadingCount);
    //----------------------------------------------------------//



    /// <summary>
    /// What the server sends when asked for a sensors attachment
    /// Contains the information of the selected sensors attachments.
    /// </summary>
    public sealed record AttachmentDto(
        string Id,
        string FileName,
        string ContentType,
        long SizeBytes,
        DateTimeOffset UploadedAtUtc);
    //----------------------------------------------------------//

}

//------------------------------------------- End Of File -------------------------------------------//
