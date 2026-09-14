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



## Running the build
<p align="right">(<a href="#readme-top">back to top</a>)</p>

### Prerequisites 
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows (the client is a WPF app and only runs on Windows)
- Docker Desktop, only if you want to run the API in a container instead of with `dotnet run`

### 1. Restore and Build everything
```bash
dotnet restore SmartXApp.slnx
dotnet build SmartXApp.slnx
```
<p align="right">(<a href="#readme-top">back to top</a>)</p>


### 2. Start the API
```bash
dotnet run --project SmartXApp.Api
```

This boots the gateway on **http://localhost:5224** (the `http` launch profile) and
immediately seeds 8 mock sensors with historical telemetry, then keeps generating new
live readings every 3 seconds - the gateway is never "empty," exactly as the brief's
simulation note asks for. Leave this running in its own terminal.

**Optional**: run it in Docker instead (same port, no SDK required on the host):

```bash
docker compose up --build
```
<p align="right">(<a href="#readme-top">back to top</a>)</p>


### 3. Start the WPF client

In a second terminal:

```bash
dotnet run --project SmartXApp.Client
```

The client talks to `http://localhost:5224` (hardcoded in `SmartXApp.Client/App.xaml.cs`
change `ApiBaseAddress` there if you run the API on a different port). From the
landing page, select **"Sensor Data Ingestion & Telemetry"** in the left sidebar; the
other two pillars are intentionally disabled with a tooltip explaining they arrive in
Part 2 and the final PoE.

<p align="right">(<a href="#readme-top">back to top</a>)</p>


## Where to find each technical requirement
<p align="right">(<a href="#readme-top">back to top</a>)</p>


## Architecture Notes
<p align="right">(<a href="#readme-top">back to top</a>)</p>


## Video Demonstration

https://youtu.be/gwcMaRGh6Yg

<p align="right">(<a href="#readme-top">back to top</a>)</p>


## References 






<!-- MARKDOWN LINKS & IMAGES -->
[CSharp.cs]: https://img.shields.io/badge/C%23-239120?style=for-the-badge&logo=c-sharp&logoColor=white
[CSharp-url]: https://learn.microsoft.com/en-us/dotnet/csharp/
[VisualStudio.vs]: https://img.shields.io/badge/Visual%20Studio-5C2D91?style=for-the-badge&logo=visual-studio&logoColor=white
[VisualStudio-url]: https://visualstudio.microsoft.com/
[WPF.wpf]: https://img.shields.io/badge/WPF-0078D4?style=for-the-badge&logo=windows&logoColor=white
[WPF-url]: https://learn.microsoft.com/en-us/dotnet/desktop/wpf/
[AspNetCore.aspnet]: https://img.shields.io/badge/ASP.NET_Core-5C2D91?style=for-the-badge&logo=dotnet&logoColor=white
[AspNetCore-url]: https://dotnet.microsoft.com/en-us/apps/aspnet
[Docker.docker]: https://img.shields.io/badge/Docker-2496ED?style=for-the-badge&logo=docker&logoColor=white
[Docker-url]: https://www.docker.com/
