using SmartX.Shared.Contracts;
using SmartX.Shared.Models;
using SmartX.Api.Data;
using SmartX.Api.Services;

namespace SmartX.Api.Endpoints
{
    /// <summary>
    /// Defines the HTTP routes for sensor file attachments.
    /// Covers uploading a file, listing what is attached to a sensor, and downloading one back out. 
    /// </summary>
    public static class AttachmentEndpoints
    {
        private static readonly string UploadsRoot = Path.Combine(AppContext.BaseDirectory, "App_Data", "uploads");


        /// <summary>
        /// Registers the attachment routes under the sensor group, covering upload, list and download.
        /// Uploaded files are encrypted before they are written to disk and decrypted again on the way back out.
        /// </summary>
        public static void MapAttachmentEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/sensors/{id}/attachments").WithTags("Attachments");


            group.MapPost("/", async (string id, IFormFile file, SensorRepository sensors, FileEncryptionService encryption) =>
            {
                if (!sensors.TryGet(id, out var sensor))
                    return Results.NotFound($"No sensor registered with id '{id}'.");

                if (file.Length == 0)
                    return Results.BadRequest("Uploaded file is empty.");

                Directory.CreateDirectory(UploadsRoot);

                var attachmentId = Guid.NewGuid().ToString("N");
                var storageFileName = $"{attachmentId}.enc";
                var destinationPath = Path.Combine(UploadsRoot, storageFileName);

                await using var sourceStream = file.OpenReadStream();
                long encryptedSize = await encryption.EncryptToFileAsync(sourceStream, destinationPath);

                var attachment = new SensorAttachment
                {
                    Id = attachmentId,
                    SensorId = id,
                    FileName = file.FileName,
                    ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                    SizeBytes = encryptedSize,
                    UploadedAtUtc = DateTimeOffset.UtcNow,
                    StorageFileName = storageFileName
                };

                sensor.Attachments[attachmentId] = attachment;

                return Results.Created($"/api/sensors/{id}/attachments/{attachmentId}", ToDto(attachment));

            })
            .DisableAntiforgery();
            //----------------------------------------------------------//


            //
            group.MapGet("/", (string id, SensorRepository sensors) =>
            {
                if (!sensors.TryGet(id, out var sensor))
                    return Results.NotFound($"No sensor registered with id '{id}'.");

                var dtos = sensor.Attachments.Values.Select(ToDto).OrderByDescending(a => a.UploadedAtUtc).ToList();
                return Results.Ok(dtos);
            });
            //----------------------------------------------------------//


            //
            group.MapGet("/{attachmentId}/download", async (string id, string attachmentId, SensorRepository sensors, FileEncryptionService encryption) =>
            {
                if (!sensors.TryGet(id, out var sensor))
                    return Results.NotFound($"No sensor registered with id '{id}'.");

                if (!sensor.Attachments.TryGetValue(attachmentId, out var attachment))
                    return Results.NotFound($"No attachment '{attachmentId}' for sensor '{id}'.");

                var storedPath = Path.Combine(UploadsRoot, attachment.StorageFileName);

                if (!File.Exists(storedPath))
                    return Results.NotFound("Attachment file is missing from storage.");

                var decrypted = await encryption.DecryptFromFileAsync(storedPath);

                return Results.File(decrypted, attachment.ContentType, attachment.FileName);

            });
            //----------------------------------------------------------//
        }


        /// <summary>
        /// Converts a stored SensorAttachment into the DTO the client receives.
        /// Leaves out StorageFileName on purpose so the on-disk blob name is never exposed.
        /// </summary>
        private static AttachmentDto ToDto(SensorAttachment attachment) => new(
        attachment.Id,
        attachment.FileName,
        attachment.ContentType,
        attachment.SizeBytes,
        attachment.UploadedAtUtc);
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//