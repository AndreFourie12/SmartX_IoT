using SmartX.Shared.Contracts;
using SmartX.Shared.Validation;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace SmartX.Client.Services
{
    /// <summary>
    /// This class acts as a thin wrapper around the smart-X gateway API
    /// Every call is awaited on the UI thread's synchronization context but never blocks it
    /// </summary>
    public sealed class SmartXApiClient
    {
        /// <summary>
        /// 
        /// </summary>
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {

        };
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        public SmartXApiClient(string baseAddress)
        {

        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        public async Task<SensorDto?> RegisterSensorAsync(SensorRegistrationRequest request, CancellationToken ct = default)
        {

        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        public async Task<BatchTelemetryIngestResponse?> IngestBatchAsync(string sensorId, BatchTelemetryIngestRequest request, CancellationToken ct = default)
        {

        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        public async Task<AttachmentDto?> UploadAttachmentAsync(string sensorId, string filePath, CancellationToken ct = default)
        {

        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        public async Task<DeploymentValidationResult?> ValidateDeploymentAsync(List<DeploymentNode> roots, CancellationToken ct = default)
        {

        }
        //----------------------------------------------------------//
    }
}


//------------------------------------------- End Of File -------------------------------------------//