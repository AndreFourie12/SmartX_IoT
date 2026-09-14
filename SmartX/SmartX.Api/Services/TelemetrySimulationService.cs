using SmartX.Api.Data;
using SmartX.Shared.Models;
using SmartX.Shared.Telemetry;

namespace SmartX.Api.Services
{
    /// <summary>
    /// Background service that stands in for real hardware.
    /// Seeds mock sensors and history when the API starts, then keeps producing live readings so the dashboard always has something to show.
    /// </summary>
    public sealed class TelemetrySimulationService(SensorRepository sensors, TelemetryStore telemetry, ILogger<TelemetrySimulationService> logger) : BackgroundService
    {
        private const double PacketDropChance = 0.25;
        private static readonly Random Rng = new();

        private readonly string[] _environmentalIds = new[] { "ESP32-ENV-01", "ESP32-ENV-02", "ESP32-ENV-03" };
        private readonly string[] _powerIds = new[] { "ESP32-PWR-01", "ESP32-PWR-02", "ESP32-PWR-03" };
        private readonly string[] _actuatorIds = new[] { "ESP32-ACT-01", "ESP32-ACT-02" };


        /// <summary>
        /// Background loop that seeds mock sensors and history at startup, then generates a fresh round of readings every three seconds.
        /// A seeding failure stops the service, while a failed tick is logged and the loop carries on.
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                SeedSensors();
                SeedHistoricalBatches();

                logger.LogInformation("Smart-X simulation seeded {Count} sensors with mock telemetry.", sensors.GetAll().Count);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to seed simulation data.");
                return; // stop the background service loop if seeding failed
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    GenerateLiveTick();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error generating live tick.");
                }

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
        /// Registers the fixed set of environmental, power and actuator sensors used for the demo.
        /// Locations are handed out in rotation so the deployment tree ends up with more than one branch.
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
        /// Adds a single sensor to the repository with the given id, category and location, stamped with the current time.
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
        /// Gives every sensor some starting history so the dashboard is not empty on first load.
        /// Environmental sensors get jagged float batches through the batch processor, while power and actuator sensors get simple individual readings.
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
        /// Produces one round of fresh readings across every sensor.
        /// Each sensor has a random chance of being skipped, which imitates packet loss on a real mesh.
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


        /// <summary>
        /// Random chance that a reading is dropped on this tick, imitating packet loss.
        /// </summary>
        private static bool PacketDropped() => Rng.NextDouble() < PacketDropChance;
    }

}

//------------------------------------------- End Of File -------------------------------------------//