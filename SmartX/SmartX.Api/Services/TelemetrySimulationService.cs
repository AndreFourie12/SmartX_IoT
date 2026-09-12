using SmartX.Api.Data;
using SmartX.Shared.Models;
using SmartX.Shared.Telemetry;

namespace SmartX.Api.Services
{
    public sealed class TelemetrySimulationService(SensorRepository sensors, TelemetryStore telemetry, ILogger<TelemetrySimulationService> logger) : BackgroundService
    {
        private const double PacketDropChance = 0.25;
        private static readonly Random Rng = new();

        private readonly string[] _environmentalIds = ["ESP32-ENV-01", "ESP32-ENV-02", "ESP32-ENV-03"];
        private readonly string[] _powerIds = ["ESP32-PWR-01", "ESP32-PWR-02", "ESP32-PWR-03"];
        private readonly string[] _actuatorIds = ["ESP32-ACT-01", "ESP32-ACT-02"];


        /// <summary>
        /// 
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            SeedSensors();
            SeedHistoricalBatches();

            logger.LogInformation("Smart-X simulation seeded {Count} sensors with mock telemetry.", sensors.GetAll().Count);

            while (!stoppingToken.IsCancellationRequested)
            {
                GenerateLiveTick();

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        private void SeedSensors()
        {
            var locations = new[]
            {
                "Facility A / Zone 1 / Sub-Zone A",
                "Facility A / Zone 1 / Sub-Zone B",
                "Facility A / Zone 2 / Sub-Zone A"
            };

            int next = 0;

            string NextLocation() => locations[next++ % locations.Length];

            foreach (var id in _environmentalIds)
                Register(id, SensorCategory.Environmental, NextLocation());

            foreach (var id in _powerIds)
                Register(id, SensorCategory.PowerConsumption, NextLocation());

            foreach (var id in _actuatorIds)
                Register(id, SensorCategory.Actuator, NextLocation());
        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        private void Register(string id, SensorCategory category, string location)
        {
            sensors.Register(new SensorDevice
            {
                Id = id,
                Location = location,
                Category = category,
                RegisteredAtUtc = DateTimeOffset.UtcNow
            });
        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        private void SeedHistoricalBatches()
        {
            foreach(var id in _environmentalIds)
            {
                int batchCount = Rng.Next(8, 12);
                var batchs = new float[batchCount][];

                for(int b = 0; b <batchCount; b++)
                {
                    int sampleCount = Rng.Next(3, 7);
                    batchs[b] = new float[sampleCount];

                    for (int s = 0; s < sampleCount; s++)
                        batchs[b][s] = 40f + (float)Rng.NextDouble() * 30f;
                }

                telemetry.AddRange(id, BatchTelemetryProcessor.ProcessRawBatches(id, batchs, "%"));
            }

            foreach (var id in _powerIds)
            {
                for (int i = 0; i < 10; i++)
                    telemetry.Add(id, new TelemetryPacket<int>(id, 150 + Rng.Next(-20, 20), "W"));
            }

            foreach (var id in _actuatorIds)
            {
                for (int i = 0; i < 10; i++)
                    telemetry.Add(id, new TelemetryPacket<bool>(id, i % 4 == 0, string.Empty));
            }
        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        private void GenerateLiveTick()
        {
            foreach (var id in _environmentalIds)
            {
                if (PacketDropped())
                    continue;

                telemetry.Add(id, new TelemetryPacket<float>(id, 40f + (float)Rng.NextDouble() * 30f, "%"));
            }

            foreach (var id in _powerIds)
            {
                if (PacketDropped())
                    continue;

                telemetry.Add(id, new TelemetryPacket<int>(id, 150 + Rng.Next(-25, 25), "W"));
            }

            foreach (var id in _actuatorIds)
            {
                if (PacketDropped())
                    continue;

                telemetry.Add(id, new TelemetryPacket<bool>(id, Rng.NextDouble() < 0.5, string.Empty));
            }
        }
        //----------------------------------------------------------//


        private static bool PacketDropped() => Rng.NextDouble() < PacketDropChance;
    }

}
