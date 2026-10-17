using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using SmartX.Shared.Contracts;
using SmartX.Shared.Models;

namespace SmartX.Api.Services
{
    /// <summary>
    /// Background service that stands in for real hardware: a fleet of fifteen simulated ESP32 nodes.
    ///
    /// Part 2 rewrite: in Part 1 this wrote readings straight into the in-memory store. The brief asks
    /// for simulated data to "hit the API like it was a device", so now every device is a genuine HTTP
    /// client of the gateway. Each provisions itself with POST /api/sensors, uploads its backlog
    /// with POST .../telemetry/batch, then reports with POST .../telemetry - through the same routing,
    /// validation, queues and 202 receipts a physical board would get. Actuators behave like real
    /// device-shadow clients: they poll GET /api/stream/shadow for their desired state, switch, and
    /// report the new state back, which is how a command shows as "pending" and then "confirmed".
    ///
    /// The devices also share a small physical model, so commands have consequences: running Pump A
    /// through an open valve raises soil moisture in its bays, evaporation lowers it, and closing the
    /// valves cuts irrigation off. Faults are injected the way they happen in the field - occasional
    /// power spikes, a probe reading hot, nodes losing power for half a minute - plus a 25% packet drop
    /// on routine heartbeats, as in Part 1.
    /// </summary>
    public sealed class TelemetrySimulationService(
        IHttpClientFactory httpClientFactory,
        IServer server,
        IHostApplicationLifetime lifetime,
        SimulationControl control,
        ILogger<TelemetrySimulationService> logger) : BackgroundService
    {
        public const string HttpClientName = "device-fleet";

        private const double PacketDropChance = 0.25;
        private const int ReportEverySeconds = 3;
        private const int StormParallelism = 24;

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };

        private sealed record SimulatedDevice(string Id, string DisplayName, SensorCategory Category, string Location, int Phase);

        private static readonly SimulatedDevice[] Fleet =
        [
            new("ESP32-ENV-01", "Climate Node 1", SensorCategory.Environmental, "Facility A / Grow Room / NFT Channel 1", 0),
            new("ESP32-PWR-01", "Smart Meter 1", SensorCategory.PowerConsumption, "Facility A / Grow Room / NFT Channel 1", 1),
            new("ESP32-ACT-01", "Relay 1", SensorCategory.Actuator, "Facility A / Grow Room / NFT Channel 1", 2),
            new("ESP32-ENV-02", "Climate Node 2", SensorCategory.Environmental, "Facility A / Grow Room / NFT Channel 2", 1),
            new("ESP32-PWR-02", "Smart Meter 2", SensorCategory.PowerConsumption, "Facility A / Grow Room / NFT Channel 2", 2),
            new("ESP32-ACT-02", "Relay 2", SensorCategory.Actuator, "Facility A / Grow Room / NFT Channel 2", 0),
            new("ESP32-ENV-03", "Climate Node 3", SensorCategory.Environmental, "Facility A / Nutrient Room / Reservoir", 2),
            new("ESP32-PWR-03", "Smart Meter 3", SensorCategory.PowerConsumption, "Facility A / Nutrient Room / Reservoir", 0),
            new("ESP32-SOIL-01", "Moisture Sensor A", SensorCategory.SoilMoisture, "Facility B / Hydroponics / Bay 1", 0),
            new("ESP32-PMP-01", "Pump A", SensorCategory.WaterPump, "Facility B / Hydroponics / Bay 1", 1),
            new("ESP32-VLV-01", "Valve 1", SensorCategory.Valve, "Facility B / Hydroponics / Bay 1", 2),
            new("ESP32-SOIL-02", "Moisture Sensor B", SensorCategory.SoilMoisture, "Facility B / Hydroponics / Bay 2", 1),
            new("ESP32-PMP-02", "Pump B", SensorCategory.WaterPump, "Facility B / Hydroponics / Bay 2", 2),
            new("ESP32-VLV-02", "Valve 2", SensorCategory.Valve, "Facility B / Hydroponics / Bay 2", 0),
            new("ESP32-TMP-01", "Greenhouse Temp", SensorCategory.Temperature, "Facility B / Hydroponics / Bay 2", 1)
        ];

        public static IReadOnlyCollection<string> FleetIds { get; } = Fleet.Select(device => device.Id).ToArray();

        // ---- Physical state of the simulated site. Only the tick loop touches it. ----
        private readonly Dictionary<string, bool> _actuatorOn = new(DeviceIdentifier.Comparer)
        {
            ["ESP32-ACT-01"] = true,
            ["ESP32-ACT-02"] = true,
            ["ESP32-PMP-01"] = false,
            ["ESP32-PMP-02"] = false,
            ["ESP32-VLV-01"] = true,
            ["ESP32-VLV-02"] = true
        };

        private readonly Dictionary<string, double> _humidity = new() { ["ESP32-ENV-01"] = 52, ["ESP32-ENV-02"] = 57, ["ESP32-ENV-03"] = 49 };
        private readonly Dictionary<string, double> _powerBase = new() { ["ESP32-PWR-01"] = 150, ["ESP32-PWR-02"] = 165, ["ESP32-PWR-03"] = 140 };
        private readonly Dictionary<string, DateTimeOffset> _offlineUntil = new(DeviceIdentifier.Comparer);

        // Moisture Sensor B starts just above the warning line so the moisture scenario plays out within
        // a couple of minutes of launch: warning, a forecast, then a critical drop unless someone acts.
        private double _moistureA = 58;
        private double _moistureB = 31;

        private const double EvaporationA = 0.05;   // % per second
        private const double EvaporationB = 0.08;
        private const double IrrigationRate = 0.25;

        private DateTimeOffset _nextOutageAt = DateTimeOffset.UtcNow.AddSeconds(Random.Shared.Next(70, 110));
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private Task _storm = Task.CompletedTask;


        /// <summary>
        /// Waits for the gateway to start listening, provisions the fleet, uploads each device's backlog,
        /// then runs a one-second tick for as long as the API is up. A failed tick is logged and the next
        /// one carries on.
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            HttpClient http;

            try
            {
                await WaitForServerStartAsync(stoppingToken);
                http = CreateClient();

                await ProvisionFleetAsync(http, stoppingToken);
                await SeedHistoryAsync(http, stoppingToken);

                logger.LogInformation("Smart-X device fleet online: {Count} simulated nodes reporting to {Address} over HTTP.", Fleet.Length, http.BaseAddress);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to bring the simulated device fleet online.");
                return;
            }

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            long tick = 0;

            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    try
                    {
                        await TickAsync(http, tick++, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Device fleet tick failed.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Shutting down.
            }

            await _storm;
        }
        //----------------------------------------------------------//


        /// <summary>
        /// The devices can only call the API once Kestrel is accepting connections.
        /// </summary>
        private async Task WaitForServerStartAsync(CancellationToken ct)
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var onStarted = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
            using var onStopping = ct.Register(() => started.TrySetCanceled(ct));
            await started.Task;
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Points the device fleet at whatever address the gateway actually bound to - localhost:5224 from
        /// Visual Studio, port 8080 inside the Docker container - preferring plain HTTP like an ESP32 would.
        /// Wildcard bindings (http://+:8080, http://[::]:8080) are reached through localhost.
        /// </summary>
        private HttpClient CreateClient()
        {
            var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [];

            string address = addresses.FirstOrDefault(a => a.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                ?? addresses.FirstOrDefault()
                ?? "http://localhost:5224";

            foreach (var wildcard in new[] { "://+", "://*", "://[::]", "://0.0.0.0" })
                address = address.Replace(wildcard, "://localhost", StringComparison.Ordinal);

            var http = httpClientFactory.CreateClient(HttpClientName);
            http.BaseAddress = new Uri(address);
            return http;
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Every device registers itself, as a freshly flashed board would on first boot.
        /// A 409 just means it is already registered.
        /// </summary>
        private static async Task ProvisionFleetAsync(HttpClient http, CancellationToken ct)
        {
            foreach (var device in Fleet)
            {
                var request = new SensorRegistrationRequest(device.Id, device.Location, device.Category, device.DisplayName);

                for (int attempt = 1; ; attempt++)
                {
                    try
                    {
                        using var response = await http.PostAsJsonAsync("/api/sensors", request, JsonOptions, ct);

                        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Conflict)
                            break;

                        response.EnsureSuccessStatusCode();
                    }
                    catch (HttpRequestException) when (attempt < 5)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(attempt), ct);
                    }
                }
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Each device uploads the readings it buffered before the gateway came up, as one jagged batch -
        /// the Part 1 batch endpoint, now flowing through the Part 2 queue.
        /// </summary>
        private async Task SeedHistoryAsync(HttpClient http, CancellationToken ct)
        {
            foreach (var device in Fleet)
            {
                var request = device.Category.DataType() switch
                {
                    TelemetryDataType.Float => new BatchTelemetryIngestRequest(TelemetryDataType.Float, SeedWindows(device), null, null, device.Category.Unit()),
                    TelemetryDataType.Int => new BatchTelemetryIngestRequest(TelemetryDataType.Int, null,
                        Windows(5, () => (int)Math.Round(_powerBase[device.Id] + Random.Shared.Next(-18, 18))), null, "W"),
                    _ => new BatchTelemetryIngestRequest(TelemetryDataType.Bool, null, null,
                        Windows(3, () => _actuatorOn[device.Id]), string.Empty)
                };

                using var response = await http.PostAsJsonAsync($"/api/sensors/{device.Id}/telemetry/batch", request, JsonOptions, ct);
                response.EnsureSuccessStatusCode();
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Float history. Soil moisture is seeded as the steady evaporation curve that led to its current
        /// level, so the trend forecaster has a believable slope from the first second.
        /// </summary>
        private float[][] SeedWindows(SimulatedDevice device)
        {
            if (device.Category != SensorCategory.SoilMoisture)
                return Windows(Random.Shared.Next(8, 12), () => (float)Read(device));

            bool isA = device.Id == "ESP32-SOIL-01";
            double current = isA ? _moistureA : _moistureB;
            double rate = isA ? EvaporationA : EvaporationB;

            var windows = Windows(10, () => 0f);
            int total = windows.Sum(window => window.Length);
            int k = 0;

            foreach (var window in windows)
            {
                for (int s = 0; s < window.Length; s++, k++)
                    window[s] = (float)(current + rate * (total - k) + Noise(0.15));
            }

            return windows;
        }
        //----------------------------------------------------------//


        /// <summary>
        /// One second of the simulated site: apply any new commands from the shadow, advance the physics,
        /// handle outages and storms, and have every device that is due send its reading.
        /// </summary>
        private async Task TickAsync(HttpClient http, long tick, CancellationToken ct)
        {
            var now = DateTimeOffset.UtcNow;

            var switched = await ApplyShadowAsync(http, now, ct);
            AdvancePhysics();
            ScheduleOutages(now);

            if (control.TryTakeStorm(out int stormPackets))
                _storm = RunStormAsync(http, stormPackets, ct);

            var sends = new List<Task>();

            foreach (var device in Fleet)
            {
                if (IsOffline(device.Id, now))
                    continue;

                bool heartbeatDue = tick % ReportEverySeconds == device.Phase;
                bool stateChanged = switched.Contains(device.Id);

                if (!heartbeatDue && !stateChanged)
                    continue;

                // A state change is delivered reliably (QoS 1 in MQTT terms); routine readings are best-effort.
                if (!stateChanged && Random.Shared.NextDouble() < PacketDropChance)
                    continue;

                sends.Add(SendReadingAsync(http, device, ct));
            }

            await Task.WhenAll(sends);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Actuators poll the device shadow and obey any desired state that differs from their own.
        /// An offline actuator can't hear the command, which is how a command ends up stuck as pending.
        /// </summary>
        private async Task<HashSet<string>> ApplyShadowAsync(HttpClient http, DateTimeOffset now, CancellationToken ct)
        {
            var switched = new HashSet<string>(DeviceIdentifier.Comparer);
            var desired = await http.GetFromJsonAsync<Dictionary<string, bool>>("/api/stream/shadow", JsonOptions, ct) ?? [];

            foreach (var (id, on) in desired)
            {
                if (_actuatorOn.TryGetValue(id, out bool current) && current != on && !IsOffline(id, now))
                {
                    _actuatorOn[id] = on;
                    switched.Add(id);
                }
            }

            return switched;
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Pump A feeds the header line both bays draw from; Pump B only feeds Bay 2. Water only reaches a
        /// bay through its valve, so "Shut down all valves" really does stop irrigation.
        /// </summary>
        private void AdvancePhysics()
        {
            bool pumpA = _actuatorOn["ESP32-PMP-01"];
            bool pumpB = _actuatorOn["ESP32-PMP-02"];
            bool irrigateBay1 = pumpA && _actuatorOn["ESP32-VLV-01"];
            bool irrigateBay2 = (pumpA || pumpB) && _actuatorOn["ESP32-VLV-02"];

            _moistureA = Math.Clamp(_moistureA - EvaporationA + (irrigateBay1 ? IrrigationRate : 0), 6, 97);
            _moistureB = Math.Clamp(_moistureB - EvaporationB + (irrigateBay2 ? IrrigationRate : 0), 6, 97);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Every minute and a half or so, one node loses power for half a minute. Forced outages from the
        /// simulator endpoint are applied here too.
        /// </summary>
        private void ScheduleOutages(DateTimeOffset now)
        {
            while (control.TryTakeOutage(out var forced))
                _offlineUntil[forced.DeviceId] = now + forced.Duration;

            if (now < _nextOutageAt)
                return;

            var victim = Fleet[Random.Shared.Next(Fleet.Length)];
            _offlineUntil[victim.Id] = now.AddSeconds(Random.Shared.Next(25, 45));
            _nextOutageAt = now.AddSeconds(Random.Shared.Next(90, 150));
        }
        //----------------------------------------------------------//


        private bool IsOffline(string id, DateTimeOffset now) =>
            _offlineUntil.TryGetValue(id, out var until) && now < until;


        /// <summary>
        /// One device's current reading, with the occasional field fault mixed in.
        /// </summary>
        private double Read(SimulatedDevice device)
        {
            double roll = Random.Shared.NextDouble();

            switch (device.Category)
            {
                case SensorCategory.Environmental:
                    double humidity = _humidity[device.Id];
                    humidity += (55 - humidity) * 0.15 + Noise(3);
                    _humidity[device.Id] = humidity;
                    return roll < 0.01 ? 86 + Random.Shared.NextDouble() * 6 : humidity;

                case SensorCategory.PowerConsumption:
                    double baseline = _powerBase[device.Id];
                    return roll switch
                    {
                        < 0.012 => 420 + Random.Shared.Next(0, 460),   // severe spike   -> critical, alert lane
                        < 0.032 => 260 + Random.Shared.Next(0, 100),   // heavy load     -> warning, alert lane
                        < 0.042 => 205 + Random.Shared.Next(0, 35),    // odd but legal  -> stays under the alarm limit
                        _ => baseline + Random.Shared.Next(-20, 21)
                    };

                case SensorCategory.SoilMoisture:
                    return (device.Id == "ESP32-SOIL-01" ? _moistureA : _moistureB) + Noise(0.3);

                case SensorCategory.Temperature:
                    double seconds = _clock.Elapsed.TotalSeconds;
                    double temperature = 23 + 2.5 * Math.Sin(2 * Math.PI * seconds / 1800) + Noise(0.2);
                    return roll < 0.008 ? 41 + Random.Shared.NextDouble() * 3 : temperature;

                default:
                    return _actuatorOn[device.Id] ? 1 : 0;
            }
        }
        //----------------------------------------------------------//


        private async Task SendReadingAsync(HttpClient http, SimulatedDevice device, CancellationToken ct)
        {
            try
            {
                using var response = await PostReadingAsync(http, device, Read(device), timestampUtc: null, ct);
            }
            catch (HttpRequestException ex)
            {
                logger.LogDebug(ex, "{Device} could not reach the gateway.", device.Id);
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// POSTs one reading exactly as a device would, typed by the device's category.
        /// </summary>
        private static Task<HttpResponseMessage> PostReadingAsync(HttpClient http, SimulatedDevice device, double value, DateTimeOffset? timestampUtc, CancellationToken ct)
        {
            var request = device.Category.DataType() switch
            {
                TelemetryDataType.Float => new TelemetryIngestRequest(TelemetryDataType.Float, (float)value, null, null, device.Category.Unit(), timestampUtc),
                TelemetryDataType.Int => new TelemetryIngestRequest(TelemetryDataType.Int, null, (int)Math.Round(value), null, device.Category.Unit(), timestampUtc),
                _ => new TelemetryIngestRequest(TelemetryDataType.Bool, null, null, value >= 0.5, string.Empty, timestampUtc)
            };

            return http.PostAsJsonAsync($"/api/sensors/{device.Id}/telemetry", request, JsonOptions, ct);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// A mesh reconnect storm: after an outage every node flushes its buffer at once, so the gateway
        /// is hit with a burst far beyond its processing budget and the routine queue backs up. The
        /// buffered readings carry their original timestamps and arrive out of order, which the sorted
        /// timelines absorb. Halfway through, one meter reports a severe spike - the packet that should
        /// overtake the whole backlog through the priority lane.
        /// </summary>
        private async Task RunStormAsync(HttpClient http, int packets, CancellationToken ct)
        {
            var routineSources = Fleet
                .Where(device => device.Category is SensorCategory.Environmental or SensorCategory.PowerConsumption or SensorCategory.Temperature)
                .ToArray();
            var meters = Fleet.Where(device => device.Category == SensorCategory.PowerConsumption).ToArray();

            int spikeAt = packets / 2;
            int sent = 0, rejected = 0;
            var started = Stopwatch.StartNew();
            var flushTime = DateTimeOffset.UtcNow;

            // Buffered readings sit close to what each device was actually reading. Copy the levels now,
            // before the first await, because the tick loop keeps updating the live dictionaries while
            // the storm's parallel senders run.
            var humidity = new Dictionary<string, double>(_humidity);
            var power = new Dictionary<string, double>(_powerBase);
            double temperature = 23 + 2.5 * Math.Sin(2 * Math.PI * _clock.Elapsed.TotalSeconds / 1800);

            try
            {
                await Parallel.ForAsync(0, packets + 1, new ParallelOptions { MaxDegreeOfParallelism = StormParallelism, CancellationToken = ct }, async (i, token) =>
                {
                    SimulatedDevice device;
                    double value;
                    DateTimeOffset? timestamp;

                    if (i == spikeAt)
                    {
                        device = meters[Random.Shared.Next(meters.Length)];
                        value = 650 + Random.Shared.Next(0, 300);
                        timestamp = null;
                    }
                    else
                    {
                        device = routineSources[Random.Shared.Next(routineSources.Length)];
                        value = device.Category switch
                        {
                            SensorCategory.Environmental => humidity[device.Id] + Noise(2),
                            SensorCategory.PowerConsumption => power[device.Id] + Random.Shared.Next(-15, 16),
                            _ => temperature + Noise(0.3)
                        };
                        timestamp = flushTime.AddSeconds(-Random.Shared.Next(1, 30));
                    }

                    try
                    {
                        using var response = await PostReadingAsync(http, device, value, timestamp, token);

                        if (response.IsSuccessStatusCode)
                            Interlocked.Increment(ref sent);
                        else
                            Interlocked.Increment(ref rejected);
                    }
                    catch (HttpRequestException)
                    {
                        Interlocked.Increment(ref rejected);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        // The API is shutting down mid-storm.
                    }
                });
            }
            catch (OperationCanceledException)
            {
                // Shutting down mid-storm.
            }
            finally
            {
                control.StormFinished(sent, rejected, started.Elapsed);
            }
        }
        //----------------------------------------------------------//


        private static T[][] Windows<T>(int count, Func<T> sample) where T : struct
        {
            var windows = new T[count][];

            for (int w = 0; w < count; w++)
            {
                windows[w] = new T[Random.Shared.Next(3, 7)];

                for (int s = 0; s < windows[w].Length; s++)
                    windows[w][s] = sample();
            }

            return windows;
        }
        //----------------------------------------------------------//


        private static double Noise(double amplitude) => (Random.Shared.NextDouble() * 2 - 1) * amplitude;
    }

}

//------------------------------------------- End Of File -------------------------------------------//
