using SmartX.Shared.Models;

namespace SmartX.Shared.Contracts
{
    /// <summary>
    /// What the client submits when creating a sensor
    /// Contains what the clients know/controls about the sensor
    /// DisplayName is optional so Part 1 callers that only send id/location/category keep working.
    /// Client -> API
    /// </summary>
    public sealed record SensorRegistrationRequest(
        string id,
        string Location,
        SensorCategory Category,
        string? DisplayName = null);
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
        int TelemetryReadingCount,
        string DisplayName = "");
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
