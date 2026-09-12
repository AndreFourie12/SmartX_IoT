using SmartX.Shared.Models;
using System.Collections.Concurrent;

namespace SmartX.Api.Data
{
    /// <summary>
    /// Thread-safe in-memory repository for SensorDeviceObjects
    /// Acts as a lookup table keyed by sensorId
    /// Safe to use from multiple threads
    /// </summary>
    public sealed class SensorRepository
    {

        private readonly ConcurrentDictionary<string, SensorDevice> _sensors = new();

        /// <summary>
        /// Adds new sensor OR overwrites an existing one with the same ID
        /// </summary>
        public SensorDevice Register(SensorDevice sensor)
        {
            _sensors[sensor.Id] = sensor;
            return sensor;
        }
        //----------------------------------------------------------//


        // Returns true/false depending if sensor exists, and ouputs the matching sensor
        public bool TryGet(string id, out SensorDevice sensor) => _sensors.TryGetValue(id, out sensor!);


        // Returns snapshot copy of all stored sensors as a readonly list
        public IReadOnlyCollection<SensorDevice> GetAll() => _sensors.Values.ToList();


        // Simple existence check by ID
        public bool Exists(string id) => _sensors.ContainsKey(id);
    }
}

//------------------------------------------- End Of File -------------------------------------------//