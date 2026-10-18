<h3 align="center">SmartX Part 1</h3>
  <p align="center">
    PROG7312 Repo for SmartX, a application for monitoring and managing distributed environments
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
      Student Number: 10438312<br/>
      <a href="https://github.com/AndreFourie12">@AndreFourie12</a>
    </td>
  </tr>
</table>



##  Table of Contents
<ol>
  <li>
    <a href="#project-overview">Project Overview</a>
    <ul>
      <li><a href="#research-task">Research Task</a></li>
      <li><a href="#built-with">Built With</a></li>
    </ul>
  </li>
   <li>
    <a href="#running-the-build">Running the build</a>
    <ul>
      <li><a href="#restore-and-build-everything">Restore and Build everything</a></li>
      <li><a href="#start-the-api">Start the API</a></li>
      <li><a href="#start-the-WPF-client">Start the WPF client</a></li>
    </ul>
  </li>
  <li><a href="#layout">layout</a></li>
  <li><a href="#what-to-try">What to try</a></li>
  <li><a href="#where-to-find-each-technical-requirement">Where to find each technical requirement</a></li>
  <li><a href="#architecture-notes">Architecture Notes</a></li>
  <li><a href="#video-demonstration">Video Demonstration</a></li>
</ol>





## Project Overview
This project is a .NET 10 solution simulating the Smart-X IoT ingestion gateway: a WPF desktop client
for registering sensors, simulating telemetry, and monitoring mesh health, backed by
an ASP.NET Core Web API that ingests, stores, and validates that telemetry.


| Project | What it is |
|---|---|
| `SmartX.Api` | ASP.NET Core Minimal API - the gateway. Ingests telemetry through a two-lane queue, keeps live device state, raises alerts, executes override commands, and runs the suggestion engine. Also hosts a simulated fleet of 15 ESP32 devices that talk to it over real HTTP. |
| `SmartX.Client` | WPF desktop console with three pillars: **Sensor Data Ingestion & Telemetry** (Part 1), **Real-Time Command Stream & History** (Part 2) and **Network Topology & Mesh Routing** (final PoE). |
| `SmartX.Shared` | Types both sides use: generic telemetry packets, contracts (DTOs), alert thresholds, device-key rules. |
| `SmartX.Tests` | 34 xUnit tests covering the Part 2 data structures and the Part 1 generics/arrays. |


### Research Task

The research part of the project can be found here ->  [View the research paper](ST10438312_PROG7312_Part_1_Research.pdf)


### Prerequisites 
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows (the client is a WPF app and only runs on Windows)
- Docker Desktop, only if you want to run the API in a container instead of with `dotnet run`
<p align="right">(<a href="#readme-top">back to top</a>)</p>

## What's new in Part 2

- **Navigation shell** - the pillars are reachable from the top bar; the third, Network Topology, is locked until the final PoE. Views are created once and cached, so filters, selections, scroll positions and the event feed survive switching; hidden views pause their polling. The pillars link to each other ("Open in Command Stream" / "Open in Ingestion"), and a red dot on the Command Stream tab shows active critical alerts from any pillar.
- **Real-Time Command Stream & History page** - live queue KPIs, a searchable/filterable device fleet, a threshold-banded timeline for the selected device, manual override commands with an undo stack, Suggested Actions, and a live event stream.
- **Two-lane ingestion** - every packet is classified on arrival: in-range readings join a FIFO Queue<T>, threshold breaches go into a PriorityQueue that is always drained first.
- **Live device dictionary**, **SortedDictionary timelines**, **HashSets** of unique faults and disconnected nodes, and a Stack-based command history.
- **Predictive Action engine** that learns which commands operators use after which alerts , predicts the next action, falls back on zone rules where there is no history yet, and forecasts threshold crossings from trends - ranked with a top-K heap.
- **Device simulation that hits the API like hardware** - the 15 simulated devices register, upload and report over HTTP, and actuators obey commands through a device shadow. A small physical model means commands have consequences: running Pump A raises soil moisture; closing the valves stops it.
- New sensor categories (Soil Moisture, Temperature, Water Pump, Valve), friendly device names, and a set of Part 1 fixes.

<p align="right">(<a href="#readme-top">back to top</a>)</p>


## Running the build
Can be done in terminal, which is shown below, OR by going to startup configuration, 
selecting multiple startup projects and setting both SmartX.Api and SmartX.Client to
startup projects

All commands run from the `SmartX` folder (the one containing `SmartX.slnx`):

```bash
cd SmartX
```


### 1. Restore and Build everything
```bash
dotnet restore SmartX.slnx
dotnet build SmartX.slnx
```

### 2.1 Start the API
```bash
dotnet run --project SmartX.Api
```

This boots the gateway on **http://localhost:5224** and
immediately seeds 8 mock sensors with historical telemetry, then keeps generating new
live readings every 3 seconds. Leave this running in its own terminal.

### 2.2 Docker
For Docker -> From the `SmartX` folder, instead of the command above:

```bash
docker compose up --build
```

Same gateway, same address: the container listens on 8080 internally and compose maps it to
**5224** on the host, which is the address the client already has hardcoded, so the client
cannot tell the difference. `docker compose down` stops it and `docker compose logs -f`
follows the simulation.

Only the API is containerised. The client is a WPF desktop app and a container has no display
surface to put a window on, so step 3 below is the same either way. The gateway keeps its
sensors and telemetry in memory by design, so restarting the container reseeds it from scratch
exactly as restarting `dotnet run` does.

### 3. Start the WPF client

In a second terminal:

```bash
dotnet run --project SmartX.Client
```

The client talks to `http://localhost:5224` (hardcoded in `SmartX.Client/App.xaml.cs`
change `ApiBaseAddress` there if you run the API on a different port). From the
landing page, select **"Sensor Data Ingestion & Telemetry"** in the left sidebar; the
other two pillars are intentionally disabled with a tooltip explaining they arrive in
Part 2 and the final PoE.

<p align="right">(<a href="#readme-top">back to top</a>)</p>


## Layout

- **Left sidebar** - the deployment hierarchy, plus *Add Sensor* and *Validate Hierarchy*.
- **Dark header** - the "Telemetry Pulse" engagement widget.
- **Registered sensors** - the device table, scoped by the tree selection.
- **Bottom** - readings and attachments for the selected device, then the meter aggregation strip.
  
<p align="right">(<a href="#readme-top">back to top</a>)</p>


## Using the application

### Navigation

| Pillar | Shortcut | Notes |
|---|---|---|
| Sensor Data Ingestion & Telemetry | Ctrl+1 | Part 1. Select a sensor and use **Open in Command Stream** to jump to its live view. |
| Real-Time Command Stream & History | Ctrl+2 | Part 2. A red dot appears on this tab, from any pillar, while a critical alert is active. |
| Network Topology & Mesh Routing | - | Locked until the final PoE: the tab is greyed out with a padlock, and Ctrl+3 does nothing. |

Switching pillars never reloads anything: each view is created once and cached. Set a filter on the Command Stream, select a device, switch to Ingestion, come back - the filter, selection, scroll position and event history are still there. Hidden views stop polling, so only the visible pillar talks to the API.

### The Real-Time Command Stream page

| Area | What it shows |
|---|---|
| **Header** | **LIVE** while the once-a-second refresh is reaching the gateway (red **GATEWAY UNREACHABLE** when it isn't). Routine queue depth (with a fill bar) and average wait; alert-lane depth, average wait and how many packets the last alert overtook; throughput against the 200/s budget; duplicates blocked by the HashSets. **Simulate critical alert ▾** and **Reconnect storm** buttons for testing. |
| **Device fleet** (left) | Search box, *Anticipated* searches predicted by the engine, a **category** dropdown and an **alert-state** dropdown (each "All" or one value), and the fleet - most urgent first, with value and last-seen. An amber **✦ INSIGHT** tag marks devices the engine has flagged before anyone searched for them. |
| **Selected device** (middle) | Big current value, **1m / 5m / 15m** timeline with warning and critical bands shaded, or **Log** view (sorted readings with deltas). **Open in Ingestion** jumps to the Part 1 view. |
| **Manual override** (middle) | Turn ON / OFF / Toggle for the selected pump, valve or relay; group commands (Shut down all valves, Stop all pumps, Emergency stop); **Undo** and the undo stack with the next undo highlighted. |
| **Suggested Actions** (right) | Ranked cards - LEARNED, SEQUENCE, ZONE RULE, FORECAST - each with confidence, evidence, and **Run** / **Show device**. *Learned patterns* expands to show the strongest condition → response patterns the engine has learned. |
| **Live command stream** (right) | Every alert, recovery, outage, command, undo and acknowledgement, newest first. Alerts that came through the priority lane carry a red **PRIORITY LANE** tag with their latency and how many packets they jumped. |

  
<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Testing the Part 2 data structures

Each test below can be done from the UI, and most also from [`SmartX/SmartX.Api/SmartX.Api.http`](SmartX/SmartX.Api/SmartX.Api.http) - open it in Visual Studio and click **Send request** above a block. The `curl` commands are written for **Command Prompt or Git Bash**. In PowerShell use the `Invoke-RestMethod` versions shown alongside them (PowerShell quotes JSON differently, and in Windows PowerShell `curl` is an alias for a different command).

### 1. Queue&lt;T&gt; and the PriorityQueue - simulate a high-priority alert

**From the UI**
1. Open **Real-Time Command Stream** (Ctrl+2).
2. Click **Reconnect storm (load test)**. The simulated fleet flushes 1,200 buffered readings at once. Watch ROUTINE QUEUE jump into the hundreds and its average wait climb to seconds - the gateway only processes 200/s.
3. Halfway through, the storm includes one severe power spike. Within a moment a red CRITICAL banner appears: *"processed 0.1 ms after arrival, ahead of 600+ waiting packets"*. The same appears in the live stream with a PRIORITY LANE tag, and ALERT LANE shows "last one jumped …".
4. For a single alert on demand, click Simulate critical alert and pick Severe power spike, Critical moisture drop or Overheating. The status line shows the receipt the API returned, including ALERT lane.

**From the command line** - a normal reading, then a spike:

```bash
curl -X POST http://localhost:5224/api/sensors/ESP32-PWR-02/telemetry -H "Content-Type: application/json" -d "{\"dataType\":\"Int\",\"intValue\":152,\"unit\":\"W\"}"
```

```bash
curl -X POST http://localhost:5224/api/sensors/ESP32-PWR-02/telemetry -H "Content-Type: application/json" -d "{\"dataType\":\"Int\",\"intValue\":870,\"unit\":\"W\"}"
```

The first receipt says `"lane":"Routine"`; the second says `"lane":"Alert","severity":"Critical"`. In PowerShell:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5224/api/sensors/ESP32-PWR-02/telemetry -ContentType 'application/json' -Body '{"dataType":"Int","intValue":870,"unit":"W"}'
```

To see a spike overtake a backlog from the command line, start a storm first (`curl -X POST http://localhost:5224/api/simulator/storm`), send the spike, then check `GET /api/stream/pipeline` (`lastBypassCount`) or `GET /api/stream/history` (`"viaPriorityLane":true`, `"bypassed":…`).

**Thresholds that route a packet to the alert lane**

| Category | Warning | Critical |
|---|---|---|
| Power (smart meters) | ≥ 250 W | ≥ 400 W |
| Soil moisture | < 30 % or > 90 % | < 20 % |
| Temperature | ≥ 35 °C | ≥ 40 °C |
| Humidity (environmental) | < 25 % or > 85 % | - |

Inside the alert lane, Critical is served before Warning, and equal severities in arrival order - so no alert can be starved.

### 2. Stack&lt;T&gt; - manual override undo

1. In the fleet, select **Pump A** and click **Turn ON**. The row shows *ON pending* until the simulated pump polls its shadow and reports back (about a second), then the stream shows *"Pump A confirmed ON"*.
2. Click **Shut down all valves**. The **UNDO STACK** now holds both commands, newest on top marked **NEXT UNDO**.
3. Click **Undo** - both valves reopen (each restored to its own previous state, including any that were already closed). Click Undo again - Pump A switches off. The stack pops last-in, first-out.
4. Click **Shut down all valves** twice: the second is refused (*"nothing to change"*) and is **not** pushed, so Undo never lands on an empty step.

### 3. Dictionary&lt;TKey, TValue&gt; - O(1) device lookup

Every packet is matched to its device through a dictionary keyed by MAC address / node ID. Keys are normalised, so every spelling of a MAC is one key - register a device with a lower-case, dash-separated MAC, then try the colon form:

```bash
curl -X POST http://localhost:5224/api/sensors -H "Content-Type: application/json" -d "{\"id\":\"24-6f-28-aa-bb-cc\",\"location\":\"Facility A / Grow Room / NFT Channel 3\",\"category\":\"Valve\",\"displayName\":\"Valve 3\"}"
```

```bash
curl -X POST http://localhost:5224/api/sensors -H "Content-Type: application/json" -d "{\"id\":\"24:6F:28:AA:BB:CC\",\"location\":\"x\",\"category\":\"Valve\"}"
```

The first returns 201 with id `24:6F:28:AA:BB:CC`; the second returns **409 Conflict** - same key. Device IDs are also case-insensitive (`esp32-pmp-01` finds Pump A).

### 4. SortedDictionary - timelines in timestamp order

- Select any device: the timeline and **Log** view are read straight out of its `SortedDictionary`, already in time order.
- Run a **Reconnect storm**: the buffered readings carry their original timestamps (up to 30 s old) and arrive out of order, yet every timeline stays correctly ordered.
- Send a deliberately late packet - stamped 40 seconds in the past - then open Climate Node 1's **Log** view: it sits in its correct place, not at the top.

```powershell
$body = @{ dataType = 'Float'; floatValue = 61.5; unit = '%'; timestampUtc = (Get-Date).ToUniversalTime().AddSeconds(-40).ToString('o') } | ConvertTo-Json
```

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5224/api/sensors/ESP32-ENV-01/telemetry -ContentType 'application/json' -Body $body
```

(The `.http` file has the same request. Packets more than 24 h old or stamped in the future are rejected with 400.) Keys are `(timestamp, sequence)` pairs, so two readings with the same timestamp never collide.

### 5. HashSet&lt;T&gt; - no duplicate alerts

```bash
curl -X POST "http://localhost:5224/api/simulator/outage/ESP32-ENV-03?seconds=30"
```

The live stream shows **one** *"Climate Node 3 went offline"*, while **DUPLICATES BLOCKED** in the header climbs on every 2-second health sweep that finds it still silent. When it comes back you get one *"reconnected"*. The same happens for faults: a soil probe stuck below 20 % reports every 3 seconds, but raises its alert once. `GET /api/stream/alerts` shows the sets' contents and the suppressed count.

### 6. Search and filter

Type in the search box: the text is matched, in any case, anywhere in a device's name, ID, zone or category - `pump`, `bay 2`, `soil`, `pmp-01`. Combine it with the two dropdowns: e.g. **Water Pump** with **Critical** shows only pumps that are critical. The gateway answers a filter from its index sets - the category set and the alert-state set are looked up and intersected - rather than scanning the fleet. API form: `GET /api/stream/devices?category=WaterPump&alert=Critical` (the API also accepts several of each, e.g. `&category=Valve&alert=Offline`, unioned within a group and intersected across).

### 7. The Predictive Action engine - a scripted walkthrough

The engine starts with a few days of seeded operator history (as the brief encourages seeding), so it has patterns from the first second. The simulated **Moisture Sensor B** starts at 31 % and is drying out:

1. Within seconds B crosses 30 %  a **Moisture low** warning. Without anyone searching, **Pump A** is highlighted in the fleet (INSIGHT) and **Suggested Actions** shows **Turn ON Pump A** - *"Operators responded with this in 5 of 6 similar episodes"*. A **FORECAST** card predicts when B will cross 20 %, and *Anticipated* shows **Pump A** under the search box.
2. Type `Moisture Sensor B` in the search box and pause - the search is logged. The engine's Markov chain now predicts the next step from your last action.
3. Click **Run** on *Turn ON Pump A*. The command is tagged *from suggestion*, Pump A confirms ON, and B's moisture starts **rising** on the timeline. The card's evidence now reads *"(1 this session)"* - it learned from you.
4. Leave Pump A running: Moisture Sensor A (also fed by Pump A) eventually overwaters, and the engine suggests **Turn OFF Pump A** (learned 3 of 3).
5. Try **Shut down all valves** while B is dry: a **ZONE RULE** card points out that *Valve 2 is closed, so no pump can get water to this bay* - a prerequisite it shows even alongside the learned pump suggestion.
6. Expand **Learned patterns** under the cards to see the strongest condition → response patterns behind the suggestions, with how often each held.

The cards come from four analyses: **LEARNED** (what operators did after the same condition), **SEQUENCE** (what usually follows your last action), **ZONE RULE** (the nearest pump, valve or relay in the same sub-zone, when there is no history to go on) and **FORECAST** (a trend heading for a threshold).


## Where to find each technical requirement

### Part 1

| Requirement | Implementation |
|---|---|
| Generics (`TelemetryPacket<T>`) | [`TelemetryPacket.cs`](SmartX/SmartX.Shared/Telemetry/TelemetryPacket.cs) - constrained to `struct`; value formatting and numeric conversion use JIT-folded `typeof(T)` branches so the value is never boxed. |
| Operator overloading | [`PowerReading.cs`](SmartX/SmartX.Shared/Telemetry/PowerReading.cs) - `+`, `-`, `<`, `>`, `==` and friends; used for the Smart Meter Aggregation strip in [`SensorIngestionView.xaml.cs`](SmartX/SmartX.Client/Views/SensorIngestionView.xaml.cs). |
| Jagged arrays List&lt;T&gt; | [`BatchTelemetryProcessor.cs`](SmartX/SmartX.Shared/Telemetry/BatchTelemetryProcessor.cs) - `T[][]` sampling windows into a pre-sized `List<TelemetryPacket<T>>`. |
| Recursion | [`DeploymentValidator.cs`](SmartX/SmartX.Shared/Validation/DeploymentValidator.cs) - recursive Facility → Zone → Sub-Zone → Device validation with a depth guard. |
| API integration | Minimal API in [`SmartX/SmartX.Api/Endpoints`](SmartX/SmartX.Api/Endpoints), consumed by [`SmartXApiClient.cs`](SmartX/SmartX.Client/Services/SmartXApiClient.cs). |
| Sensor registration | [`AddSensorDialog.xaml.cs`](SmartX/SmartX.Client/Dialogs/AddSensorDialog.xaml.cs) → `POST /api/sensors` ([`SensorEndpoints.cs`](SmartX/SmartX.Api/Endpoints/SensorEndpoints.cs)). |
| Media / log attachments | [`AttachmentEndpoints.cs`](SmartX/SmartX.Api/Endpoints/AttachmentEndpoints.cs), AES-256 at rest via [`FileEncryptionService.cs`](SmartX/SmartX.Api/Services/FileEncryptionService.cs). |
| Engagement feature | "Telemetry Pulse" - [`EngagementService.cs`](SmartX/SmartX.Api/Services/EngagementService.cs) and the hand-drawn [`HealthGaugeControl`](SmartX/SmartX.Client/Controls/HealthGaugeControl.xaml.cs). |
| Custom collection | [`TelemetryRingBuffer<T>`](SmartX/SmartX.Shared/Collections/TelemetryRingBuffer.cs) - also reused in Part 2 for the queue-wait samples. |


### Part 2

| Requirement | Implementation |
|---|---|
| Navigation menu, async, state-preserving | [`MainWindow.xaml.cs`](SmartX/SmartX.Client/MainWindow.xaml.cs) caches one view per pillar in a `Dictionary<Pillar, FrameworkElement>`; views implement [`IModuleView`](SmartX/SmartX.Client/Navigation/Navigation.cs) to pause/resume polling and `IDeviceFocus` for cross-module links. |
| Queue&lt;T&gt; - FIFO telemetry processing | [`TelemetryIngestionPipeline.cs`](SmartX/SmartX.Api/Streaming/TelemetryIngestionPipeline.cs) routine lane, drained by [`TelemetryProcessingService.cs`](SmartX/SmartX.Api/Streaming/TelemetryProcessingService.cs) within a per-cycle budget. |
| Priority queue - critical alerts bypass | Same pipeline: `PriorityQueue<IngestEnvelope, AlertPriority>` ([`IngestEnvelope.cs`](SmartX/SmartX.Api/Streaming/IngestEnvelope.cs) defines the severity-then-arrival ordering). Always dequeued first; a dedicated semaphore wakes the consumer mid-cycle. |
| Stack&lt;T&gt; - undo for override commands | [`CommandHistoryService.cs`](SmartX/SmartX.Api/Commands/CommandHistoryService.cs) - the undo stack, each command storing every device's previous state. |
| Dictionary - live devices by MAC / node ID | [`LiveDeviceRegistry.cs`](SmartX/SmartX.Api/Streaming/LiveDeviceRegistry.cs) with key normalisation in [`DeviceIdentifier.cs`](SmartX/SmartX.Shared/Models/DeviceIdentifier.cs). Secondary index sets (category → ids, alert state → ids) drive the filters and group commands. |
| SortedDictionary - historical logs by timestamp | [`TelemetryTimelineStore.cs`](SmartX/SmartX.Api/Streaming/TelemetryTimelineStore.cs) per device, keyed by [`TimelineKey`](SmartX/SmartX.Api/Streaming/TimelineKey.cs) (timestamp, sequence); the event history in [`StreamEventLog.cs`](SmartX/SmartX.Api/Streaming/StreamEventLog.cs). Drawn by [`TimelineChart.cs`](SmartX/SmartX.Client/Controls/TimelineChart.cs). |
| HashSet - unique faults / disconnected nodes | [`AlertTracker.cs`](SmartX/SmartX.Api/Streaming/AlertTracker.cs); swept by [`DeviceHealthMonitorService.cs`](SmartX/SmartX.Api/Streaming/DeviceHealthMonitorService.cs). |
| Search / filter by category or alert state | `Query` in [`LiveDeviceRegistry.cs`](SmartX/SmartX.Api/Streaming/LiveDeviceRegistry.cs) (index-set lookups and intersection, then a case-insensitive `Contains` text match); UI in [`CommandStreamView.xaml`](SmartX/SmartX.Client/Views/CommandStreamView.xaml). |
| Dashboard + manual overrides | [`CommandStreamView.xaml.cs`](SmartX/SmartX.Client/Views/CommandStreamView.xaml.cs); endpoints in [`CommandStreamEndpoints.cs`](SmartX/SmartX.Api/Endpoints/CommandStreamEndpoints.cs). |
| Predictive Action engine | [`ActionRecommendationEngine.cs`](SmartX/SmartX.Api/Insights/ActionRecommendationEngine.cs) - sliding-window `Queue` of condition episodes, co-occurrence and Markov `Dictionary`s, per-episode `HashSet`s, top-K `PriorityQueue`; [`TrendAnalysis.cs`](SmartX/SmartX.Api/Insights/TrendAnalysis.cs) (least squares) for forecasts; history seeded by [`OperatorHistorySeeder.cs`](SmartX/SmartX.Api/Insights/OperatorHistorySeeder.cs). |
| Simulated data hits the API like a device | [`TelemetrySimulationService.cs`](SmartX/SmartX.Api/Services/TelemetrySimulationService.cs) - an `HttpClient` per fleet, device-shadow polling, physical model; storm/outage controls in [`SimulatorEndpoints.cs`](SmartX/SmartX.Api/Endpoints/SimulatorEndpoints.cs). |
| Real-time updates | One `GET /api/stream/snapshot` per second ([`CommandStreamService.cs`](SmartX/SmartX.Api/Streaming/CommandStreamService.cs)) carries the state and the latest events; `RefreshAsync` and `MergeEvents` in [`CommandStreamView.xaml.cs`](SmartX/SmartX.Client/Views/CommandStreamView.xaml.cs) apply them and raise the critical banner for a new critical event. |


<p align="right">(<a href="#readme-top">back to top</a>)</p>


## Architecture Notes

- **WPF + a separate Web API.** The brief requires telemetry to hit the API like a device, which only makes sense if the API is an independently addressable HTTP service. All shared logic lives in `SmartX.Shared`.
- **Enqueue, acknowledge, process.** The ingest endpoint only classifies and enqueues, then returns 202 Accepted - the way a real IoT gateway acknowledges a device before processing - so request threads never wait on processing. A single consumer applies each device's routine readings in arrival order.
- **One poll for everything.** The dashboard fetches one snapshot per second - everything it draws, including the latest events, in one round trip - so a critical alert reaches the screen within a second of being processed. One channel keeps the client simple and the figures on screen mutually consistent; a hidden view stops polling altogether.
- **Device shadow.** Commands set a *desired* state; actuators poll it and confirm by reporting. That is how AWS IoT and Azure IoT Hub handle commands to intermittently connected devices, and it is why a command to an offline pump shows as pending rather than silently "succeeding".
- **Thread safety.** `Queue`, `PriorityQueue`, `Dictionary`, `SortedDictionary`, `HashSet` and `Stack` are not thread-safe; each owning class guards them with a short `System.Threading.Lock`, and no lock is held while calling into another component.
- **Bounded memory.** Timelines keep the latest 1,500 readings per device and the event log keeps 500. The routine queue is not capped: the processor drains it at a steady 200 packets/s, so even the 1,200-packet reconnect storm clears in about six seconds.
- **In-memory by design.** As in Part 1, all state is in memory and reseeded on startup; attachments are AES-256 encrypted on disk with a per-process key.
- **Looking ahead to the final PoE.** `SortedDictionary` is a red-black tree but .NET does not expose a "seek to key" operation, so range reads walk from the start. The final PoE's AVL/red-black tree for historical range queries will close exactly that gap.


<p align="right">(<a href="#readme-top">back to top</a>)</p>


## Video Demonstration

- Part 1: https://youtu.be/gwcMaRGh6Yg
- Part 2: *add link here*

<p align="right">(<a href="#readme-top">back to top</a>)</p>






