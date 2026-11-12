namespace SmartX.Api.Topology
{
    /// <summary>
    /// The sample mesh: the gateway and fifteen ESP32 nodes, with their canvas positions and MAC
    /// addresses, joined by 28 links. Costs are milliwatts per packet.
    /// </summary>
    public static class MeshSeed
    {
        public const string GatewayId = "GW-01";

        // Listed in MAC order: the gateway is ...:4C:00, then 01 to 0F down this list.
        private static readonly (string Id, string Name, double X, double Y)[] NodeRows =
        [
            ("GW-01", "Gateway", 450, 62),
            ("ESP32-ENV-01", "Climate Node 1", 215, 150),
            ("ESP32-ACT-01", "Relay 1", 95, 215),
            ("ESP32-PWR-01", "Smart Meter 1", 205, 280),
            ("ESP32-ENV-02", "Climate Node 2", 335, 235),
            ("ESP32-ACT-02", "Relay 2", 320, 345),
            ("ESP32-PWR-02", "Smart Meter 2", 175, 390),
            ("ESP32-ENV-03", "Climate Node 3", 95, 480),
            ("ESP32-PWR-03", "Smart Meter 3", 250, 485),
            ("ESP32-SOIL-01", "Moisture Sensor A", 590, 170),
            ("ESP32-PMP-01", "Pump A", 745, 150),
            ("ESP32-VLV-01", "Valve 1", 650, 285),
            ("ESP32-SOIL-02", "Moisture Sensor B", 810, 380),
            ("ESP32-PMP-02", "Pump B", 665, 420),
            ("ESP32-VLV-02", "Valve 2", 540, 350),
            ("ESP32-TMP-01", "Greenhouse Temp", 775, 495)
        ];

        private static readonly (string From, string To, double CostMw)[] LinkRows =
        [
            ("GW-01", "ESP32-ENV-01", 3.1),
            ("GW-01", "ESP32-SOIL-01", 3.4),
            ("GW-01", "ESP32-ENV-02", 5.2),
            ("GW-01", "ESP32-PMP-01", 7.4),
            ("ESP32-ENV-01", "ESP32-ACT-01", 2.4),
            ("ESP32-ENV-01", "ESP32-PWR-01", 2.9),
            ("ESP32-ENV-01", "ESP32-ENV-02", 3.3),
            ("ESP32-ACT-01", "ESP32-PWR-01", 2.1),
            ("ESP32-PWR-01", "ESP32-PWR-02", 3.0),
            ("ESP32-PWR-01", "ESP32-ENV-02", 4.1),
            ("ESP32-ENV-02", "ESP32-ACT-02", 2.6),
            ("ESP32-ACT-02", "ESP32-PWR-02", 2.8),
            ("ESP32-PWR-02", "ESP32-ENV-03", 3.2),
            ("ESP32-PWR-02", "ESP32-PWR-03", 3.6),
            ("ESP32-ENV-03", "ESP32-PWR-03", 2.5),
            ("ESP32-ACT-02", "ESP32-PWR-03", 4.0),
            ("ESP32-ACT-02", "ESP32-VLV-02", 5.8),
            ("ESP32-SOIL-01", "ESP32-PMP-01", 2.3),
            ("ESP32-SOIL-01", "ESP32-VLV-01", 3.0),
            ("ESP32-PMP-01", "ESP32-VLV-01", 2.2),
            ("ESP32-PMP-01", "ESP32-SOIL-02", 3.5),
            ("ESP32-VLV-01", "ESP32-VLV-02", 3.7),
            ("ESP32-VLV-01", "ESP32-PMP-02", 6.8),
            ("ESP32-VLV-02", "ESP32-PMP-02", 2.7),
            ("ESP32-PMP-02", "ESP32-SOIL-02", 2.9),
            ("ESP32-SOIL-02", "ESP32-TMP-01", 2.6),
            ("ESP32-PMP-02", "ESP32-TMP-01", 3.3),
            ("ESP32-SOIL-01", "ESP32-VLV-02", 4.8)
        ];


        public static MeshGraph Build()
        {
            var graph = new MeshGraph();

            for (int i = 0; i < NodeRows.Length; i++)
            {
                var (id, name, x, y) = NodeRows[i];
                graph.AddNode(new MeshNode(id, name, $"24:6F:28:A1:4C:{i:X2}", x, y));
            }

            foreach (var (from, to, cost) in LinkRows)
                graph.AddLink(from, to, cost);

            return graph;
        }
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
