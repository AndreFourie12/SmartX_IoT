<a id="readme-top"></a>

<div align="center">
  <h3 align="center">SmartX</h3>
  <p align="center">
    PROG7312 POE: SmartX, an application for monitoring and managing distributed IoT environments
    <br />
    <b>Part 2: Real-Time Processing &amp; Device Management</b>
    <br />
    <br />
  </p>
</div>



## Personal Information
<table>
  <tr>
    <td align="center"><img src="https://github.com/AndreFourie12.png" width="80"/></td>
    <td>
      <b>Andre Fourie</b><br/>
      Student Number: ST10438312<br/>
      <a href="https://github.com/AndreFourie12">@AndreFourie12</a>
    </td>
  </tr>
</table>



## Table of Contents
<ol>
  <li>
    <a href="#project-overview">Project Overview</a>
    <ul>
      <li><a href="#projects-in-the-solution">Projects in the Solution</a></li>
      <li><a href="#built-with">Built With</a></li>
      <li><a href="#research-task">Research Task</a></li>
    </ul>
  </li>
  <li>
    <a href="#getting-started">Getting Started</a>
    <ul>
      <li><a href="#prerequisites">Prerequisites</a></li>
      <li><a href="#option-1-visual-studio">Option 1: Visual Studio</a></li>
      <li><a href="#option-2-terminal">Option 2: Terminal</a></li>
      <li><a href="#option-3-api-in-docker">Option 3: API in Docker</a></li>
    </ul>
  </li>
  <li><a href="#whats-new-in-part-2">What's New in Part 2</a></li>
  <li>
    <a href="#using-the-application">Using the Application</a>
    <ul>
      <li><a href="#navigation">Navigation</a></li>
      <li><a href="#ingestion-page-part-1">Ingestion Page (Part 1)</a></li>
      <li><a href="#command-stream-page-part-2">Command Stream Page (Part 2)</a></li>
      <li><a href="#simulated-device-fleet">Simulated Device Fleet</a></li>
    </ul>
  </li>
  <li><a href="#part-2-walkthrough">Part 2 Walkthrough</a></li>
  <li><a href="#where-to-find-each-technical-requirement">Where to Find Each Technical Requirement</a></li>
  <li><a href="#architecture-notes">Architecture Notes</a></li>
  <li><a href="#video-demonstration">Video Demonstration</a></li>
</ol>



## Project Overview

SmartX is a .NET 10 solution that simulates the Smart-X IoT ingestion gateway. A WPF desktop client is used to register sensors, monitor telemetry and send commands to devices. Behind it, an ASP.NET Core Web API ingests, processes and stores that telemetry.

Part 1 built the static ingestion gateway. Part 2 turns it into a real-time processing engine: readings are queued and processed as they arrive, critical alerts skip the line, operators can send commands to pumps, valves and relays and undo them, and a suggestion engine recommends the next action based on how operators have handled the same situation before.

### Projects in the Solution

| Project | What it is |
|---|---|
| `SmartX.Api` | ASP.NET Core Minimal API, the gateway. Ingests telemetry through a two-lane queue, keeps live device state, raises alerts, runs override commands and the suggestion engine. Also hosts a simulated fleet of 15 ESP32 devices that talk to it over real HTTP. |
| `SmartX.Client` | WPF desktop app with three pillars: **Sensor Data Ingestion & Telemetry** (Part 1), **Real-Time Command Stream & History** (Part 2) and **Network Topology & Mesh Routing** (final POE). |
| `SmartX.Shared` | Types both sides use: generic telemetry packets, contracts (DTOs), alert thresholds and device ID rules. |

### Built With

- C# and .NET 10
- WPF for the desktop client
- ASP.NET Core Minimal API for the gateway
- Docker (optional, for running the API in a container)

### Research Task

The research part of the project can be found here -> [View the research paper](ST10438312_PROG7312_Part_1_Research.pdf)

<p align="right">(<a href="#readme-top">back to top</a>)</p>



## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows (the client is a WPF app and only runs on Windows)
- Visual Studio with .NET 10 support, if you want to run it from the IDE
- Docker Desktop, only if you want to run the API in a container

Both the API and the client have to be running. The client connects to the API at `http://localhost:5224`. If the client opens before the API is ready, it shows **Gateway unreachable** and connects on its own once the API is up.

> If you download the repository as a ZIP, Windows can nest the folder inside itself and push the build past the 260-character path limit. `SmartX/Directory.Build.props` handles this automatically, so no extra steps are needed.

### Option 1: Visual Studio

1. Open `SmartX/SmartX.slnx`.
2. Right-click the solution → **Configure Startup Projects** → **Multiple startup projects**.
3. Set both **SmartX.Api** and **SmartX.Client** to **Start**.
4. Press **F5**.

### Option 2: Terminal

All commands run from the `SmartX` folder (the one containing `SmartX.slnx`):

```bash
cd SmartX
```

**1. Restore and build everything**

```bash
dotnet restore SmartX.slnx
dotnet build SmartX.slnx
```

**2. Start the API**

```bash
dotnet run --project SmartX.Api
```

This starts the gateway on **http://localhost:5224**. The 15 simulated devices then register themselves, upload some history and start reporting. When you see *"Smart-X device fleet online: 15 simulated nodes reporting … over HTTP"*, it's ready. Leave this terminal running.

**3. Start the WPF client** (in a second terminal)

```bash
dotnet run --project SmartX.Client
```

The top bar should show **Gateway connected** and the current messages per second.

### Option 3: API in Docker

From the `SmartX` folder, instead of step 2 above:

```bash
docker compose up --build
```

It's the same gateway at the same address: the container listens on 8080 and compose maps it to **5224** on your machine, so the client can't tell the difference. `docker compose down` stops it and `docker compose logs -f` follows the simulation.

Only the API runs in Docker. The client is a WPF desktop app, and a container has no screen to show a window on, so start the client with step 3 as usual.

If you run the API on a different address, set the `SMARTX_API_URL` environment variable before starting the client (for example `http://localhost:6000`). Otherwise the client uses `http://localhost:5224`.

All data is kept in memory, so restarting the API (or the container) starts from a clean, freshly seeded state.

<p align="right">(<a href="#readme-top">back to top</a>)</p>



## What's New in Part 2

- **Main menu and navigation** - the three pillars are tabs along the top bar (Ctrl+1 / Ctrl+2 / Ctrl+3). Each page is created once and kept, so searches, filters, selections and the event feed are still there when you come back, and hidden pages stop polling. The pillars link to each other (**Open in Command Stream** / **Open in Ingestion**), and a red dot on the Command Stream tab shows when there are critical alerts, whichever page you're on.
- **Real-Time Command Stream & History page** - live queue figures, a searchable and filterable device list, a timeline for the selected device, manual override commands with an undo stack, suggested actions, and a live event stream.
- **Two-lane ingestion** - every reading is checked as it arrives. Normal readings go into a `Queue<T>`; readings that cross a threshold go into a `PriorityQueue` that is always processed first.
- **Live device `Dictionary`**, **`SortedDictionary` timelines**, **`HashSet`s** of active faults and offline devices, and a **`Stack`** for undo.
- **Predictive Action engine** - learns which commands operators use after which alerts, predicts the next action, falls back on zone rules when there is no history yet, and forecasts when a reading will cross a threshold. Suggestions are ranked with a top-K min-heap.
- **Simulated devices that use the API like real hardware** - the 15 simulated devices register, upload and report over HTTP, and pumps, valves and relays follow commands through a device shadow. A small physics model means commands have an effect: running Pump A raises soil moisture, and closing the valves stops it.
- **New sensor categories** (Soil Moisture, Temperature, Water Pump, Valve), a new facility (Facility B, the hydroponics bays) and friendly device names.

<p align="right">(<a href="#readme-top">back to top</a>)</p>



## Using the Application

### Navigation

| Pillar | Shortcut | Notes |
|---|---|---|
| Sensor Data Ingestion & Telemetry | Ctrl+1 | Part 1. Select a sensor and click **Open in Command Stream** to jump to its live view. |
| Real-Time Command Stream & History | Ctrl+2 | Part 2. A red dot appears on this tab while a critical alert is active, from any pillar. |
| Network Topology & Mesh Routing | Ctrl+3 | Marked with a padlock and **FINAL POE** badge. Opens a placeholder page that lists what will be built in the final POE (mesh graph, BFS/DFS, minimum spanning tree, self-balancing tree history). |

Switching pillars never reloads anything, because each page is created once and kept in a `Dictionary`. Search for something on the Command Stream, select a device, switch to Ingestion and come back: the search, selection and event history are all still there. Hidden pages stop polling, so only the page you're looking at talks to the API.

### Ingestion Page (Part 1)

- **Left sidebar** - the deployment hierarchy tree (Facility → Zone → Sub-Zone), plus **Add Sensor** and **Validate Hierarchy**.
- **Dark header** - the **Telemetry Pulse** health gauge.
- **Registered sensors** - the device table, filtered by what is selected in the tree. **Open in Command Stream ↗** opens the selected device on the Part 2 page.
- **Bottom** - telemetry readings (with **Simulate Batch**) and attachments for the selected device, then the **Smart Meter Aggregation** strip, which uses the overloaded `+` operator.

### Command Stream Page (Part 2)

| Area | What it shows |
|---|---|
| **Header** | A **Live** pill while the once-a-second refresh reaches the gateway (**Gateway unreachable** when it doesn't). **ROUTINE QUEUE** depth and average wait; **ALERT LANE** depth and average wait (hover the wait to see how many packets the last alert jumped); **THROUGHPUT** against the 200/s budget; **DUPLICATES BLOCKED** by the HashSets, with active faults and offline devices underneath. The **Simulate critical alert** and **Reconnect storm (load test)** buttons trigger those scenarios on demand. |
| **Device fleet** (left) | Search box, **Anticipated** devices the engine predicts you'll need, a **Category** dropdown and an **Alert state** dropdown (each "All" or one value), **Clear filters**, and the fleet sorted most urgent first. A **✦ Insight** tag marks devices the engine has flagged before anyone searched for them. |
| **Selected device** (middle) | Current value, a **1m / 5m / 15m** timeline with the warning and critical bands shaded, or the **Log** view (readings in time order with the change from the previous one). **Open in Ingestion ↗** jumps to the Part 1 page. |
| **Manual override** (middle) | **Turn ON / Turn OFF / Toggle** for the selected pump, valve or relay; group commands (**Shut down all valves**, **Stop all pumps**, **Emergency stop**); **Undo** and the **UNDO STACK**, with the next undo highlighted. |
| **Suggested actions** (right) | Up to six ranked cards (LEARNED, SEQUENCE, ZONE RULE, FORECAST), each with its confidence, the reason behind it, and **Run** / **Show device**. **Learned patterns** expands to show the strongest condition → response patterns the engine has learned. |
| **Command stream** (right) | Every alert, recovery, outage, command, undo and confirmation, newest first. Alerts that came through the alert lane carry a red **Priority lane** tag with their latency and how many packets they jumped. |

### Simulated Device Fleet

Part 1 had 8 devices in Facility A. Part 2 is about sending commands and reacting to what happens, so it needed devices that can be controlled and sensors that they affect. The 7 new devices are all in Facility B, the hydroponics bays.

| Device | ID | Category | Location |
|---|---|---|---|
| Climate Node 1 | `ESP32-ENV-01` | Environmental | Facility A / Grow Room / NFT Channel 1 |
| Smart Meter 1 | `ESP32-PWR-01` | Power | Facility A / Grow Room / NFT Channel 1 |
| Relay 1 | `ESP32-ACT-01` | Smart Relay | Facility A / Grow Room / NFT Channel 1 |
| Climate Node 2 | `ESP32-ENV-02` | Environmental | Facility A / Grow Room / NFT Channel 2 |
| Smart Meter 2 | `ESP32-PWR-02` | Power | Facility A / Grow Room / NFT Channel 2 |
| Relay 2 | `ESP32-ACT-02` | Smart Relay | Facility A / Grow Room / NFT Channel 2 |
| Climate Node 3 | `ESP32-ENV-03` | Environmental | Facility A / Nutrient Room / Reservoir |
| Smart Meter 3 | `ESP32-PWR-03` | Power | Facility A / Nutrient Room / Reservoir |
| Moisture Sensor A | `ESP32-SOIL-01` | Soil Moisture | Facility B / Hydroponics / Bay 1 |
| Pump A | `ESP32-PMP-01` | Water Pump | Facility B / Hydroponics / Bay 1 |
| Valve 1 | `ESP32-VLV-01` | Valve | Facility B / Hydroponics / Bay 1 |
| Moisture Sensor B | `ESP32-SOIL-02` | Soil Moisture | Facility B / Hydroponics / Bay 2 |
| Pump B | `ESP32-PMP-02` | Water Pump | Facility B / Hydroponics / Bay 2 |
| Valve 2 | `ESP32-VLV-02` | Valve | Facility B / Hydroponics / Bay 2 |
| Greenhouse Temp | `ESP32-TMP-01` | Temperature | Facility B / Hydroponics / Bay 2 |

How the simulation behaves:

- Each device reports every 3 seconds. As in Part 1, a quarter of routine readings are randomly dropped, but a pump, valve or relay changing state is always reported.
- Pump A feeds both bays and Pump B only feeds Bay 2. Water only reaches a bay through its valve, and moisture slowly evaporates when nothing is watering it.
- **Moisture Sensor B starts at 31%** and is drying out, so the moisture scenario (warning, forecast, suggestion) plays out within a couple of minutes of starting the API.
- Faults happen the way they would in the field: occasional power spikes, humidity and temperature readings out of range, and every couple of minutes a random device loses power for 25-45 seconds.

<p align="right">(<a href="#readme-top">back to top</a>)</p>



## Part 2 Walkthrough

Each feature below can be tried from the UI, and most also from [`SmartX/SmartX.Api/SmartX.Api.http`](SmartX/SmartX.Api/SmartX.Api.http). Open it in Visual Studio and click **Send request** above a block. The `curl` commands are written for **Command Prompt or Git Bash**. For PowerShell, use the `Invoke-RestMethod` versions (PowerShell quotes JSON differently, and in Windows PowerShell `curl` is a different command).

### 1. Queue&lt;T&gt; and PriorityQueue - critical alerts skip the line

**From the UI**
1. Open **Real-Time Command Stream** (Ctrl+2).
2. Click **Reconnect storm (load test)**. The simulated fleet sends 1,200 buffered readings at once. **ROUTINE QUEUE** jumps into the hundreds and its average wait climbs to a few seconds, because the gateway only processes 200 routine readings a second.
3. Halfway through, the storm includes one severe power spike. Within a moment a red **CRITICAL** banner appears, saying how many milliseconds after arrival it was processed and how many waiting packets it went ahead of. The same alert shows in the command stream with a **Priority lane** tag.
4. For a single alert on demand, click **Simulate critical alert** and pick **Severe power spike**, **Critical moisture drop** or **Overheating**. The status line shows which lane the API routed it to.

**From the command line** - a normal reading, then a spike:

```bash
curl -X POST http://localhost:5224/api/sensors/ESP32-PWR-02/telemetry -H "Content-Type: application/json" -d "{\"dataType\":\"Int\",\"intValue\":152,\"unit\":\"W\"}"
```

```bash
curl -X POST http://localhost:5224/api/sensors/ESP32-PWR-02/telemetry -H "Content-Type: application/json" -d "{\"dataType\":\"Int\",\"intValue\":870,\"unit\":\"W\"}"
```

Both return **202 Accepted**. The first receipt says `"lane":"Routine"`; the second says `"lane":"Alert","severity":"Critical"`. In PowerShell:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5224/api/sensors/ESP32-PWR-02/telemetry -ContentType 'application/json' -Body '{"dataType":"Int","intValue":870,"unit":"W"}'
```

To see a spike overtake a backlog from the command line, start a storm first (`curl -X POST http://localhost:5224/api/simulator/storm`), send the spike, then check `GET /api/stream/pipeline` (`lastBypassCount`) or `GET /api/stream/history` (`"viaPriorityLane":true`, `"bypassed":…`).

**Thresholds that send a reading to the alert lane**

| Category | Warning | Critical |
|---|---|---|
| Power (smart meters) | ≥ 250 W | ≥ 400 W |
| Soil moisture | < 30 % or > 90 % | < 20 % |
| Temperature | ≥ 35 °C | ≥ 40 °C |
| Humidity (environmental) | < 25 % or > 85 % | - |

Inside the alert lane, Critical is processed before Warning, and alerts of the same severity in the order they arrived.

### 2. Stack&lt;T&gt; - undo for manual overrides

1. In the fleet, select **Pump A** and click **Turn ON**. The row shows *ON pending* until the simulated pump picks up the command and reports back (about a second), then the command stream shows *"Pump A confirmed ON"*.
2. Click **Shut down all valves**. The **UNDO STACK** now holds both commands, with the newest on top marked **NEXT UNDO**.
3. Click **Undo**: both valves open again (each one goes back to its own previous state). Click **Undo** again: Pump A switches off. Last in, first out.
4. Click **Shut down all valves** twice. The second one is refused because nothing would change, and it is **not** added to the stack, so Undo never lands on an empty step.

### 3. Dictionary&lt;TKey, TValue&gt; - O(1) device lookup

Every reading is matched to its device through a dictionary keyed by MAC address or node ID. Keys are normalised, so every spelling of a MAC address is the same key. Register a device with a lower-case, dash-separated MAC, then try the colon form:

```bash
curl -X POST http://localhost:5224/api/sensors -H "Content-Type: application/json" -d "{\"id\":\"24-6f-28-aa-bb-cc\",\"location\":\"Facility A / Grow Room / NFT Channel 3\",\"category\":\"Valve\",\"displayName\":\"Valve 3\"}"
```

```bash
curl -X POST http://localhost:5224/api/sensors -H "Content-Type: application/json" -d "{\"id\":\"24:6F:28:AA:BB:CC\",\"location\":\"x\",\"category\":\"Valve\"}"
```

The first returns **201 Created** with id `24:6F:28:AA:BB:CC`; the second returns **409 Conflict**, because it's the same key. Device IDs are also case-insensitive (`esp32-pmp-01` finds Pump A).

### 4. SortedDictionary - history in time order

- Select any device: the timeline and **Log** view are read straight out of its `SortedDictionary`, already in time order.
- Run a **Reconnect storm**: the buffered readings carry their original timestamps (up to 30 seconds old) and arrive out of order, but every timeline stays in the right order.
- Send a deliberately late reading, stamped 40 seconds in the past, then open Climate Node 1's **Log** view. It sits in its correct place, not at the top.

```powershell
$body = @{ dataType = 'Float'; floatValue = 61.5; unit = '%'; timestampUtc = (Get-Date).ToUniversalTime().AddSeconds(-40).ToString('o') } | ConvertTo-Json
```

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5224/api/sensors/ESP32-ENV-01/telemetry -ContentType 'application/json' -Body $body
```

(The `.http` file has the same request. Readings more than 24 hours old or stamped in the future are rejected with 400.) Each reading's key is its timestamp plus a sequence number, so two readings with the same timestamp never overwrite each other.

### 5. HashSet&lt;T&gt; - no duplicate alerts

Cut the power to Climate Node 3 for 45 seconds:

```bash
curl -X POST "http://localhost:5224/api/simulator/outage/ESP32-ENV-03?seconds=45"
```

After 15 seconds of silence the command stream shows **one** *"Climate Node 3 went offline"*. The health check runs every 2 seconds and keeps finding it silent, but instead of repeating the alert, **DUPLICATES BLOCKED** in the header goes up. When the device comes back you get one *"reconnected"* message. Faults work the same way: a soil probe stuck below 20% reports every 3 seconds but only raises its alert once. `GET /api/stream/alerts` shows what is in the sets and the suppressed count.

### 6. Search and filter

Type in the search box: the text is matched, in any case, anywhere in a device's name, ID, zone or category, for example `pump`, `bay 2`, `soil` or `pmp-01`. Combine it with the two dropdowns, e.g. **Water Pump** with **Critical** shows only pumps that are critical. The gateway answers a filter from its index sets (the category set and the alert-state set are looked up and intersected) instead of scanning every device.

API form: `GET /api/stream/devices?category=WaterPump&alert=Critical`. The API also accepts several of each, e.g. `&category=Valve&alert=Offline`; values in the same group are combined, and the two groups are intersected.

### 7. Predictive Action engine

The engine starts with a few days of seeded operator history, so it has patterns to work with from the first second. Moisture Sensor B starts at 31% and is drying out:

1. Shortly after the API starts, B drops below 30% and raises a **Moisture low** warning. Without anyone searching, **Pump A** is tagged **✦ Insight** in the fleet and shows under **Anticipated**, and **Suggested actions** shows **Turn ON Pump A**: *"Operators responded with this in 5 of 6 similar episodes"*. A **FORECAST** card predicts when B will drop below 20%.
2. Type `Moisture Sensor B` in the search box and pause. The search is logged, and the engine predicts the next step from your last action.
3. Click **Run** on *Turn ON Pump A*. The command is tagged *from suggestion*, Pump A confirms ON, and B's moisture starts **rising** on the timeline. The card's evidence now ends with *"(1 this session)"*, because it learned from what you did.
4. Leave Pump A running: Moisture Sensor A (also fed by Pump A) eventually goes above 90%, and the engine suggests **Turn OFF Pump A** (learned 3 of 3).
5. Click **Shut down all valves** while B is dry: a **ZONE RULE** card points out that *Valve 2 is closed, so no pump can get water to this bay until it opens*. This one needs no history; it works from where the devices are installed.
6. Expand **Learned patterns** under the cards to see the strongest condition → response patterns behind the suggestions, and how often each one held.

The cards come from four sources: **LEARNED** (what operators did after the same condition), **SEQUENCE** (what usually follows your last action), **ZONE RULE** (the nearest pump, valve or relay in the same sub-zone, for when there is no history) and **FORECAST** (a trend heading for a threshold).

<p align="right">(<a href="#readme-top">back to top</a>)</p>



## Where to Find Each Technical Requirement

### Part 1

| Requirement | Implementation |
|---|---|
| Generics (`TelemetryPacket<T>`) | [`TelemetryPacket.cs`](SmartX/SmartX.Shared/Telemetry/TelemetryPacket.cs) - constrained to `struct`; value formatting and numeric conversion use `typeof(T)` branches so the value is never boxed. |
| Operator overloading | [`PowerReading.cs`](SmartX/SmartX.Shared/Telemetry/PowerReading.cs) - `+`, `-`, `<`, `>`, `==` and friends; used for the Smart Meter Aggregation strip in [`SensorIngestionView.xaml.cs`](SmartX/SmartX.Client/Views/SensorIngestionView.xaml.cs). |
| Jagged arrays and List&lt;T&gt; | [`BatchTelemetryProcessor.cs`](SmartX/SmartX.Shared/Telemetry/BatchTelemetryProcessor.cs) - `T[][]` sampling windows into a pre-sized `List<TelemetryPacket<T>>`. |
| Recursion | [`DeploymentValidator.cs`](SmartX/SmartX.Shared/Validation/DeploymentValidator.cs) - recursive Facility → Zone → Sub-Zone → Device validation with a depth guard. |
| API integration | Minimal API in [`SmartX/SmartX.Api/Endpoints`](SmartX/SmartX.Api/Endpoints), used by [`SmartXApiClient.cs`](SmartX/SmartX.Client/Services/SmartXApiClient.cs). |
| Sensor registration | [`AddSensorDialog.xaml.cs`](SmartX/SmartX.Client/Dialogs/AddSensorDialog.xaml.cs) → `POST /api/sensors` ([`SensorEndpoints.cs`](SmartX/SmartX.Api/Endpoints/SensorEndpoints.cs)). |
| Media / log attachments | [`AttachmentEndpoints.cs`](SmartX/SmartX.Api/Endpoints/AttachmentEndpoints.cs), AES-256 encrypted on disk by [`FileEncryptionService.cs`](SmartX/SmartX.Api/Services/FileEncryptionService.cs). |
| Engagement feature | "Telemetry Pulse" - [`EngagementService.cs`](SmartX/SmartX.Api/Services/EngagementService.cs) and the hand-drawn [`HealthGaugeControl`](SmartX/SmartX.Client/Controls/HealthGaugeControl.xaml.cs). |
| Custom collection | [`TelemetryRingBuffer<T>`](SmartX/SmartX.Shared/Collections/TelemetryRingBuffer.cs) - also reused in Part 2 for the queue wait-time samples. |

### Part 2

| Requirement | Implementation |
|---|---|
| Main menu navigation, active pillar state | [`MainWindow.xaml.cs`](SmartX/SmartX.Client/MainWindow.xaml.cs) keeps one view per pillar in a `Dictionary<Pillar, FrameworkElement>`; views implement [`IModuleView`](SmartX/SmartX.Client/Navigation/Navigation.cs) to pause and resume polling, and `IDeviceFocus` for the links between pillars. |
| Queue&lt;T&gt; - FIFO telemetry processing | [`TelemetryIngestionPipeline.cs`](SmartX/SmartX.Api/Streaming/TelemetryIngestionPipeline.cs) routine lane, processed by [`TelemetryProcessingService.cs`](SmartX/SmartX.Api/Streaming/TelemetryProcessingService.cs) at up to 20 readings every 100 ms. |
| Priority queue - critical alerts bypass the queue | Same pipeline: `PriorityQueue<IngestEnvelope, AlertPriority>` ([`IngestEnvelope.cs`](SmartX/SmartX.Api/Streaming/IngestEnvelope.cs) defines the severity-then-arrival order). Always processed first; a semaphore wakes the worker as soon as an alert arrives. |
| Stack&lt;T&gt; - undo for override commands | [`CommandHistoryService.cs`](SmartX/SmartX.Api/Commands/CommandHistoryService.cs) - the undo stack, with each command storing every device's previous state. |
| Dictionary - live devices by MAC / node ID | [`LiveDeviceRegistry.cs`](SmartX/SmartX.Api/Streaming/LiveDeviceRegistry.cs), with key normalisation in [`DeviceIdentifier.cs`](SmartX/SmartX.Shared/Models/DeviceIdentifier.cs). Index sets (category → IDs, alert state → IDs) drive the filters and group commands. |
| SortedDictionary - history by timestamp | [`TelemetryTimelineStore.cs`](SmartX/SmartX.Api/Streaming/TelemetryTimelineStore.cs) per device, keyed by [`TimelineKey`](SmartX/SmartX.Api/Streaming/TimelineKey.cs) (timestamp, sequence); the event history in [`StreamEventLog.cs`](SmartX/SmartX.Api/Streaming/StreamEventLog.cs). Drawn by [`TimelineChart.cs`](SmartX/SmartX.Client/Controls/TimelineChart.cs). |
| HashSet - unique faults and disconnected devices | [`AlertTracker.cs`](SmartX/SmartX.Api/Streaming/AlertTracker.cs), checked every 2 seconds by [`DeviceHealthMonitorService.cs`](SmartX/SmartX.Api/Streaming/DeviceHealthMonitorService.cs). |
| Search and filter by category or alert state | `Query` in [`LiveDeviceRegistry.cs`](SmartX/SmartX.Api/Streaming/LiveDeviceRegistry.cs) (index set lookups and intersection, then a case-insensitive text match); UI in [`CommandStreamView.xaml`](SmartX/SmartX.Client/Views/CommandStreamView.xaml). |
| Dashboard and manual overrides | [`CommandStreamView.xaml.cs`](SmartX/SmartX.Client/Views/CommandStreamView.xaml.cs); endpoints in [`CommandStreamEndpoints.cs`](SmartX/SmartX.Api/Endpoints/CommandStreamEndpoints.cs). |
| Predictive Action engine | [`ActionRecommendationEngine.cs`](SmartX/SmartX.Api/Insights/ActionRecommendationEngine.cs) - sliding-window `Queue` of alert episodes, co-occurrence and Markov `Dictionary`s, a `HashSet` per episode, top-K `PriorityQueue`; [`TrendAnalysis.cs`](SmartX/SmartX.Api/Insights/TrendAnalysis.cs) (least squares) for forecasts; history seeded by [`OperatorHistorySeeder.cs`](SmartX/SmartX.Api/Insights/OperatorHistorySeeder.cs). |
| Simulated data hits the API like a device | [`TelemetrySimulationService.cs`](SmartX/SmartX.Api/Services/TelemetrySimulationService.cs) - an `HttpClient` for the fleet, device-shadow polling and the physics model; storm and outage controls in [`SimulatorEndpoints.cs`](SmartX/SmartX.Api/Endpoints/SimulatorEndpoints.cs). |
| Real-time updates | One `GET /api/stream/snapshot` per second ([`CommandStreamService.cs`](SmartX/SmartX.Api/Streaming/CommandStreamService.cs)) carries the current state and the latest events; `RefreshAsync` and `MergeEvents` in [`CommandStreamView.xaml.cs`](SmartX/SmartX.Client/Views/CommandStreamView.xaml.cs) apply them and raise the critical banner for a new critical event. |

<p align="right">(<a href="#readme-top">back to top</a>)</p>



## Architecture Notes

- **WPF client and a separate Web API.** The brief requires simulated telemetry to hit the API like a real device, which only makes sense if the API is its own HTTP service. Logic both sides need lives in `SmartX.Shared`.
- **Enqueue, acknowledge, process.** The ingest endpoint only classifies and enqueues a reading, then returns 202 Accepted, the way a real IoT gateway acknowledges a device before processing. A device never waits for processing, and one background worker handles everything in order.
- **One poll for everything.** The Command Stream page fetches one snapshot per second with everything it draws, including the latest events. A critical alert reaches the screen within a second of being processed, the numbers on screen always match each other, and a hidden page stops polling completely.
- **Device shadow.** A command sets a *desired* state; the device picks it up and confirms by reporting its new state. This is how AWS IoT and Azure IoT Hub handle commands to devices that may be offline, and it's why a command to an offline pump stays *pending* instead of pretending it worked.
- **Thread safety.** `Queue`, `PriorityQueue`, `Dictionary`, `SortedDictionary`, `HashSet` and `Stack` are not thread-safe, so each class that owns one guards it with a short `System.Threading.Lock`, and no lock is held while calling into another class.
- **Bounded memory.** Timelines keep the latest 1,500 readings per device and the event log keeps 500. The routine queue isn't capped: the worker clears it at a steady 200 readings a second, so even the 1,200-reading reconnect storm clears in about six seconds.
- **In memory by design.** As in Part 1, all state is in memory and reseeded on startup. Attachments are AES-256 encrypted on disk with a key that only lasts as long as the process.
- **Looking ahead to the final POE.** `SortedDictionary` is a red-black tree, but .NET doesn't expose a "seek to key" operation, so reading a time range walks from the start. The self-balancing tree in the final POE is meant to close that gap.

<p align="right">(<a href="#readme-top">back to top</a>)</p>



## Video Demonstration

- Part 1: https://youtu.be/gwcMaRGh6Yg
- Part 2: https://youtu.be/4H8_9ItZpOU

<p align="right">(<a href="#readme-top">back to top</a>)</p>






