<h3 align="center">SmartX</h3>
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
  <li><a href="#where-to-find-each-technical-requirement">Where to find each technical requirement</a></li>
  <li><a href="#architecture-notes">Architecture Notes</a></li>
  <li><a href="#video-demonstration">Video Demonstration</a></li>
  <li><a href="#references">References</a></li>
</ol>



## Project Overview

This project is a .NET 10 solution simulating the Smart-X IoT ingestion gateway: a WPF desktop client
for registering sensors, simulating telemetry, and monitoring mesh health, backed by
an ASP.NET Core Web API that ingests, stores, and validates that telemetry.


### Prerequisites 
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows (the client is a WPF app and only runs on Windows)
- Docker Desktop, only if you want to run the API in a container instead of with `dotnet run`


## Running the build


### 1. Restore and Build everything
```bash
dotnet restore SmartX.slnx
dotnet build SmartX.slnx
```
<p align="right">(<a href="#readme-top">back to top</a>)</p>


### 2. Start the API
```bash
dotnet run --project SmartX.Api
```

This boots the gateway on **http://localhost:5224** and
immediately seeds 8 mock sensors with historical telemetry, then keeps generating new
live readings every 3 seconds. Leave this running in its own terminal.

<p align="right">(<a href="#readme-top">back to top</a>)</p>


### 3. Start the WPF client

In a second terminal:

```bash
dotnet run --project SmartXA.Client
```

The client talks to `http://localhost:5224` (hardcoded in `SmartXApp.Client/App.xaml.cs`
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

## Where to find each technical requirement
| Requirement | Implementation |
|---|---|
| Generics (`TelemetryPacket<T>`) | [`SmartXApp.Shared/Telemetry/TelemetryPacket.cs`](SmartXApp.Shared/Telemetry/TelemetryPacket.cs) - constrained to `struct`, so float/int/bool readings are never boxed to `object`; the API picks the closed generic in [`TelemetryEndpoints.cs`](SmartXApp.Api/Endpoints/TelemetryEndpoints.cs). |
| Operator overloading | [`SmartXApp.Shared/Telemetry/PowerReading.cs`](SmartXApp.Shared/Telemetry/PowerReading.cs) - a non-generic struct overloading `+`, `-`, `<`, `>`, `==` etc. Used in [`SensorIngestionView.xaml.cs`](SmartXApp.Client/Views/SensorIngestionView.xaml.cs) to combine two live meters (`readings[0] + readings[1]`). |
| Jagged arrays | Jagged `T[][]` batches in [`BatchTelemetryProcessor.cs`](SmartXApp.Shared/Telemetry/BatchTelemetryProcessor.cs) - variable-length sampling windows, transferred straight into a `List<TelemetryPacket<T>>`. Generic over the sample type and constrained to `struct`, so one method serves float, int and bool windows without boxing; the batch endpoint picks the closed generic from the request's `TelemetryDataType`. |
| Recursion | [`DeploymentValidator.cs`](SmartXApp.Shared/Validation/DeploymentValidator.cs) recursively walks the deployment tree (Facility → Zone → Sub-Zone → Device) confirming every device is a leaf at the bottom of a full path, with a depth guard against runaway/cyclic input. The tree it walks is built from the **real** registered sensors (see below), not a fixed sample. |
| API integration layer | ASP.NET Core Minimal API, [`SmartXApp.Api/Endpoints/`](SmartXApp.Api/Endpoints), all async; consumed by [`SmartXApiClient.cs`](SmartXApp.Client/Services/SmartXApiClient.cs) over `HttpClient`. |
| Sensor payload management | Registration modal [`AddSensorDialog.xaml.cs`](SmartXApp.Client/Dialogs/AddSensorDialog.xaml.cs) (MAC/identifier, facility, zone, sub-zone, category) → `POST /api/sensors` → stored as a `SensorDevice` in [`SensorRepository.cs`](SmartXApp.Api/Data/SensorRepository.cs). |
| Hierarchy navigation | `BuildTree()` in [`SensorIngestionView.xaml.cs`](SmartXApp.Client/Views/SensorIngestionView.xaml.cs) splits each sensor's `Location` path into nested `DeploymentNode`s and counts devices bottom-up; selecting a node prefix-filters the sensor table to that subtree. The same tree is posted to `POST /api/deployment/validate`, so one tree serves both display and validation. |
| Media/log attachment | `OpenFileDialog` in the ingestion view → multipart upload handled by [`AttachmentEndpoints.cs`](SmartXApp.Api/Endpoints/AttachmentEndpoints.cs), AES-256 encrypted by [`FileEncryptionService.cs`](SmartXApp.Api/Services/FileEncryptionService.cs) before it touches disk, and decrypted on download. |
| Dynamic engagement feature | "Telemetry Pulse" widget - [`EngagementService.cs`](SmartXApp.Api/Services/EngagementService.cs) derives a health score, a clean-sweep streak, and dropout alerts from live telemetry timestamps; rendered by the hand-drawn arc gauge [`HealthGaugeControl`](SmartXApp.Client/Controls/HealthGaugeControl.xaml.cs) (deliberately not a `ProgressBar`) plus the streak counter and alert feed beside it. |
| Collections + custom data structure | [`TelemetryStore.cs`](SmartXApp.Api/Data/TelemetryStore.cs) pairs the two: storage is the custom [`TelemetryRingBuffer<T>`](SmartXApp.Shared/Collections/TelemetryRingBuffer.cs) (fixed-capacity circular buffer, O(1) eviction, bounds memory under load), while everything crossing its boundary is a standard `List<T>`. |



<p align="right">(<a href="#readme-top">back to top</a>)</p>


## Architecture Notes

- **Why WPF + a separate Web API, and not a monolithic desktop app**: the brief requires
  telemetry to "hit the API like it was a device," which only makes sense if the API is
  a real, independently-addressable HTTP service. WPF was chosen from the brief's
  "Traditional Desktop" option; all business logic (generics, operators, recursion,
  collections) lives in `SmartXApp.Shared` so both the API and any future frontend can
  use the exact same types.
- **In-memory storage, deliberately**: the brief frames this whole part as a simulation
  ("heavily seed your application with mock telemetry data") with no requirement for a
  database. `SensorRepository` and `TelemetryStore` are thread-safe in-memory stores;
  this keeps the code surface focused on the data-structure requirements the rubric
  actually grades, rather than EF Core plumbing.
- **Live simulation, not just seed-once**: `TelemetrySimulationService` (a
  `BackgroundService`) keeps generating readings every 3 seconds for the lifetime of the
  process, dropping a fraction of packets on purpose, so the engagement widget always has
  real state changes to react to during a live demo.
### Attachment storage and encryption

Attachments are AES-256 encrypted before being written to
`SmartXApp.Api/App_Data/uploads/`, saved under a generated id rather than the caller's
filename, with the IV prefixed to each file so decryption needs no side channel. The
plaintext is streamed straight from the request into the encryptor, so it is never
buffered whole in memory nor written to disk unencrypted.

The key is generated fresh per process start (see the comment in
`FileEncryptionService.cs`), which matches the lifetime of the rest of the gateway's
state, attachment metadata is in-memory too, so a restart invalidates every attachment
reference anyway. 

<p align="right">(<a href="#readme-top">back to top</a>)</p>


## Video Demonstration

https://youtu.be/gwcMaRGh6Yg

<p align="right">(<a href="#readme-top">back to top</a>)</p>


## References 



