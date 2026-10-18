using Microsoft.Win32;
using SmartX.Shared.Contracts;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SmartX.Client.Dialogs;
using SmartX.Client.Navigation;
using SmartX.Shared.Models;
using SmartX.Shared.Telemetry;
using SmartX.Shared.Validation;
namespace SmartX.Client.Views
{
    /// <summary>
    /// Interaction logic for SensorIngestionView.xaml
    ///
    /// Part 2: the navigation shell now caches this view for the whole session, so it implements
    /// IModuleView - polling pauses while another pillar is showing and resumes (with a refresh) on
    /// return, and the tree scope, selection and last batch are all kept in between. It also accepts
    /// IDeviceFocus requests, so the Command Stream can open a device here directly.
    /// </summary>
    public partial class SensorIngestionView : UserControl, IModuleView, IDeviceFocus
    {
        private readonly ObservableCollection<SensorDto> _rows = [];
        private readonly DispatcherTimer _liveTimer = new() { Interval = TimeSpan.FromSeconds(3) };

        public INavigator? Navigator { get; set; }

        private List<SensorDto> _allSensors = [];
        private List<DeploymentNode> _treeRoots = [];

        private string? _scopePath;

        private string? _deviceScopeId;

        private string? _selectedSensorId;

        private LastBatch? _lastBatch;

        // True while this view is the one on screen.
        private bool _active;


        /// <summary>
        /// Builds the view and binds the sensor grid to its row collection.
        /// Wires up a three second timer that keeps the engagement panel refreshed, and picks up any change
        /// to the set of registered sensors.
        /// </summary>
        public SensorIngestionView()
        {
            InitializeComponent();
            SensorsGrid.ItemsSource = _rows;

            _liveTimer.Tick += async (_, _) =>
            {
                // A slow gateway must not let ticks pile up on top of each other.
                if (_tickRunning)
                    return;

                _tickRunning = true;
                try
                {
                    await RefreshSensorsIfChangedAsync();
                    await RefreshEngagementAsync();
                }
                finally
                {
                    _tickRunning = false;
                }
            };
        }
        //----------------------------------------------------------//

        private bool _tickRunning;



        /// <summary>
        /// Runs each time the navigation shell shows this view, pulling in the sensor list and the engagement summary.
        /// Starts the live timer only after that first load has finished - and only if the view is still
        /// showing, since the operator may have switched away while the loads were awaited.
        /// (Part 1 did this on Loaded, which WPF raises again every time a cached view is re-attached.)
        /// </summary>
        public async Task OnActivatedAsync()
        {
            _active = true;

            await RefreshSensorsAsync();
            await RefreshEngagementAsync();

            if (_active)
                _liveTimer.Start();
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Another pillar is showing: stop polling until this view comes back.
        /// </summary>
        public void OnDeactivated()
        {
            _active = false;
            _liveTimer.Stop();
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Another module asked for a device: clear the tree scope so it is visible and remember it as the
        /// selection. The refresh that follows activation selects its row and loads its readings.
        /// </summary>
        public void FocusDevice(string deviceId)
        {
            _scopePath = null;
            _deviceScopeId = null;
            _selectedSensorId = deviceId;
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Opens the selected device on the Real-Time Command Stream pillar.
        /// </summary>
        private async void OnOpenInCommandStreamClick(object sender, RoutedEventArgs e)
        {
            if (Navigator is not null && SensorsGrid.SelectedItem is SensorDto sensor)
                await Navigator.NavigateAsync(Pillar.CommandStream, sensor.Id);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Re-fetches the sensor list when the refresh button is clicked.
        /// </summary>
        private async void OnRefreshClick(object sender, RoutedEventArgs e) 
            => await RefreshSensorsAsync();
        //----------------------------------------------------------//



        /// <summary>
        /// Pulls the full sensor list from the gateway, rebuilds the hierarchy tree, and re-applies the current scope filter.
        /// A connection failure is reported in the status line instead of bringing down the view.
        /// </summary>
        private async Task RefreshSensorsAsync()
        {
            try
            {
                _allSensors = await App.ApiClient.GetSensorAsync();
                BuildTree();
                ApplyScope();
            }
            catch (Exception)
            {
                ActionStatusText.Text = "Waiting for the gateway - retrying every 3 seconds. Is SmartX.Api running?";
            }
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Called on every live tick. Re-fetches the sensor list but only rebuilds the tree and table when the
        /// set of devices has actually changed, so the table doesn't flicker or lose its selection every 3 seconds.
        ///
        /// This is what fills the page on a cold start: the client often opens before the API is listening,
        /// and the simulated devices register themselves over HTTP a moment after it is, so the first fetch
        /// can come back empty. (Part 1 only fetched the list once, so the table stayed empty until Refresh.)
        /// </summary>
        private async Task RefreshSensorsIfChangedAsync()
        {
            try
            {
                var latest = await App.ApiClient.GetSensorAsync();

                if (SameDevices(latest, _allSensors))
                {
                    RefreshRowCounts(latest);
                    return;
                }

                bool wasEmpty = _allSensors.Count == 0;

                _allSensors = latest;
                BuildTree();
                ApplyScope();

                if (wasEmpty && latest.Count > 0)
                    ActionStatusText.Text = $"Gateway online - {latest.Count} sensors loaded.";
            }
            catch
            {
                // Still unreachable; the status strip already says so and the next tick tries again.
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Same devices, newer figures: swaps in only the rows whose counts changed (SensorDto is a record,
        /// so != compares every field) and puts the selection back on the same device. The selection
        /// handler ignores this, so the readings panel and status line are not reloaded every 3 seconds,
        /// and nothing is scrolled.
        /// </summary>
        private void RefreshRowCounts(List<SensorDto> latest)
        {
            var byId = latest.ToDictionary(sensor => sensor.Id);
            _allSensors = latest;
            _refreshingRows = true;

            try
            {
                for (int i = 0; i < _rows.Count; i++)
                {
                    if (byId.TryGetValue(_rows[i].Id, out var fresh) && fresh != _rows[i])
                        _rows[i] = fresh;
                }

                var selected = _rows.FirstOrDefault(row => row.Id == _selectedSensorId);
                if (selected is not null && !ReferenceEquals(SensorsGrid.SelectedItem, selected))
                    SensorsGrid.SelectedItem = selected;
            }
            finally
            {
                _refreshingRows = false;
            }
        }
        //----------------------------------------------------------//

        private bool _refreshingRows;


        /// <summary>
        /// True when both lists describe the same devices in the same places. Compared as sets, so order
        /// doesn't matter; reading counts are deliberately ignored because they change on every packet.
        /// </summary>
        private static bool SameDevices(List<SensorDto> latest, List<SensorDto> current)
        {
            static string Key(SensorDto sensor) => $"{sensor.Id}|{sensor.Location}|{sensor.Category}|{sensor.DisplayName}";

            return latest.Count == current.Count && latest.Select(Key).ToHashSet().SetEquals(current.Select(Key));
        }
        //----------------------------------------------------------//


        // <summary>
        /// Turns each sensor's location path into a facility, zone and sub-zone hierarchy, adding the sensor itself as a device leaf.
        /// Rolls the device counts back up the tree and rebinds the result to the tree view.
        /// </summary>
        private void BuildTree()
        {
            _treeRoots = [];

            foreach (var sensor in _allSensors.OrderBy(s => s.Id, StringComparer.Ordinal))
            {
                var parts = sensor.Location.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                if (parts.Length == 0)
                    continue;

                var level = _treeRoots;
                DeploymentNode? parent = null;

                for (int depth = 0; depth < parts.Length; depth++)
                {
                    var existing = level.FirstOrDefault(node => node.Name == parts[depth]);

                    if (existing is null)
                    {
                        existing = new DeploymentNode
                        {
                            Name = parts[depth],
                            NodeType = NodeTypeForDepth(depth)
                        };

                        level.Add(existing);

                    }

                    parent = existing;
                    level = existing.Children;
                }

                parent!.Children.Add(new DeploymentNode
                {
                    Name = sensor.Id,
                    NodeType = DeploymentNodeType.Device,
                    DeviceMacAddress = sensor.Id,
                    DeviceCount = 1
                });

            }

            foreach (var root in _treeRoots)
                CountDevices(root);

            HierarchyTree.ItemsSource = null;
            HierarchyTree.ItemsSource = _treeRoots;
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Recursively totals how many devices sit under a node and stores the result on that node.
        /// A device node counts as one and stops the descent.
        /// </summary>
        private static int CountDevices(DeploymentNode node)
        {
            if (node.NodeType == DeploymentNodeType.Device)
                return node.DeviceCount = 1;

            int total = 0;
            foreach (var child in node.Children)
                total += CountDevices(child);

            return node.DeviceCount = total;
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Maps a position in the location path to a tree level.
        /// The first segment becomes a facility, the second a zone, and anything deeper a sub-zone.
        /// </summary>
        private static DeploymentNodeType NodeTypeForDepth(int depth) => depth switch
        {
            0 => DeploymentNodeType.Facility,
            1 => DeploymentNodeType.Zone,
            _ => DeploymentNodeType.SubZone
        };
        //----------------------------------------------------------//



        /// <summary>
        /// Handles a node being picked in the deployment tree.
        /// Picking a device scopes the grid to that one sensor, while picking a facility, zone or sub-zone scopes it to everything beneath that path.
        /// </summary>
        private void OnHierarchySelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is not DeploymentNode node)
                return;

            if(node.NodeType == DeploymentNodeType.Device)
            {
                _deviceScopeId = node.Name;
                _scopePath = _allSensors.FirstOrDefault(s => s.Id == node.Name)?.Location;
                _selectedSensorId = node.Name;
            }
            else
            {
                _deviceScopeId = null;
                _scopePath = PathTo(node);
            }

            ApplyScope();
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Works out the full location path of a node by searching each root until the node is found.
        /// Returns null if the node is not in the tree.
        /// </summary>
        private string? PathTo(DeploymentNode target)
        {
            foreach (var root in _treeRoots)
            {
                var path = FindPath(root, target, root.Name);
                if (path is not null)
                    return path;
            }

            return null;
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Recursive helper that walks down from a node looking for the target, building the path as it goes.
        /// Returns the path once the target is reached, or null if this branch does not contain it.
        /// </summary>
        private static string? FindPath(DeploymentNode node, DeploymentNode target, string pathSoFar)
        {
            if (ReferenceEquals(node, target))
                return pathSoFar;

            foreach (var child in node.Children)
            {
                var found = FindPath(child, target, $"{pathSoFar} / {child.Name}");

                if (found is not null)
                    return found;
            }

            return null;
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Clears both the path and device filters so the grid shows every sensor again.
        /// </summary>
        private void OnShowAllClick(object sender, RoutedEventArgs e)
        {
            _scopePath = null;
            _deviceScopeId = null;
            ApplyScope();
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Filters the sensor list down to the current scope and refills the grid with the result.
        /// Also updates the scope label, restores the previously selected row, and refreshes the batch button state.
        /// </summary>
        private void ApplyScope()
        {
            IEnumerable<SensorDto> visible = _allSensors;

            if (_scopePath is not null)
                visible = visible.Where(s => s.Location.StartsWith(_scopePath, StringComparison.OrdinalIgnoreCase));

            if (_deviceScopeId is not null)
                visible = visible.Where(s => s.Id == _deviceScopeId);

            var ordered = visible.OrderBy(s => s.Id, StringComparer.Ordinal).ToList();

            _rows.Clear();
            foreach (var sensor in ordered)
                _rows.Add(sensor);

            string scopeLabel = _deviceScopeId is not null
                ? $"{_scopePath} / {_deviceScopeId}"
                : _scopePath ?? "All facilities";

            ScopeText.Text = $"· {scopeLabel} · {ordered.Count} " + (ordered.Count == 1 ? "device" : "devices");

            ShowAllButton.IsEnabled = _scopePath is not null;

            if (_selectedSensorId is not null)
                RestoreRowSelection();

            UpdateBatchButton();

        }
        //----------------------------------------------------------//


        /// <summary>
        /// Enables the simulate batch button only while a sensor is selected.
        /// Sets the tooltip to describe the array shape that sensor's category will produce.
        /// </summary>
        private void UpdateBatchButton()
        {
            var sensor = SensorsGrid.SelectedItem as SensorDto;

            SimulateBatchButton.IsEnabled = sensor is not null;
            OpenInCommandStreamButton.IsEnabled = sensor is not null;
            SimulateBatchButton.ToolTip = sensor is null
                ? "Select a sensor in the table above."
                : $"Send {sensor.Id} a jagged {BatchShapeFor(sensor.Category)} batch of sampling windows.";
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Remembers a sensor as the selected one and selects its row in the grid.
        /// Used after registering a new sensor so it is highlighted straight away.
        /// </summary>
        private void SelectSensorRow(string sensorId)
        {
            _selectedSensorId = sensorId;
            RestoreRowSelection();
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Re-selects the remembered sensor's row after the grid has been refilled and scrolls it into view.
        /// Does nothing if that sensor falls outside the current scope.
        /// </summary>
        private void RestoreRowSelection()
        {
            var match = _rows.FirstOrDefault(row => row.Id == _selectedSensorId);
           
            if (match is not null && !ReferenceEquals(SensorsGrid.SelectedItem, match))
            {
                SensorsGrid.SelectedItem = match;
                SensorsGrid.ScrollIntoView(match);
            }
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Handles a row being selected in the sensor grid.
        /// Updates the status line and loads that sensor's telemetry history and attachments.
        /// </summary>
        private async void OnSensorSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // A background count refresh re-selecting the same device is not the operator picking one.
            if (_refreshingRows || SensorsGrid.SelectedItem is not SensorDto sensor)
                return;

            _selectedSensorId = sensor.Id;
            ActionStatusText.Text = $"{sensor.DisplayName} ({sensor.Id}) selected · {sensor.Location}";
            UpdateBatchButton();
            await LoadSensorDetailAsync(sensor.Id);
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Fills the detail panels for one sensor with its last batch summary, recent telemetry, and attachments.
        /// Empty state messages are shown where there is nothing to display, and failures are reported in the status line.
        /// </summary>
        private async Task LoadSensorDetailAsync(string sensorId)
        {
            try
            {
                RenderLastBatch(sensorId);

                // The ring buffer returns readings in arrival order; a device flushing old readings after a
                // reconnect makes that differ from time order, so sort by timestamp for display.
                var history = await App.ApiClient.GetTelemetryAsync(sensorId, take: 40);
                TelemetryHistoryList.ItemsSource = history.OrderByDescending(reading => reading.TimestampUtc).Select(ToRow).ToList();
                TelemetryEmptyState.Visibility = history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                var attachments = await App.ApiClient.GetAttachmentsAsync(sensorId);
                AttachmentsList.ItemsSource = attachments
                    .Select(a => $"{a.FileName}  ({a.SizeBytes:N0} B)")
                    .ToList();
                AttachmentsEmptyState.Visibility = attachments.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                AttachmentsEmptyState.Text = "No attachments on this sensor yet.";
            }
            catch (Exception ex)
            {
                ActionStatusText.Text = $"Could not load sensor detail: {ex.Message}";
            }
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Converts a telemetry reading into the row shape the history list binds to.
        /// Flags the row when its timestamp came from the most recent simulated batch so it can be highlighted.
        /// </summary>
        private TelemetryRow ToRow(TelemetryDto reading) =>
            new(reading.TimestampUtc.ToLocalTime(),
                DisplayValue(reading),
                reading.Unit,
                _lastBatch is not null
                    && _lastBatch.SensorId == reading.SensorId
                    && _lastBatch.Timestamps.Contains(reading.TimestampUtc));
        //----------------------------------------------------------//


        /// <summary>
        /// Actuator readings arrive as True/False; show them the way the device's category speaks
        /// (ON/OFF, OPEN/CLOSED) so this list agrees with the Command Stream.
        /// </summary>
        private string DisplayValue(TelemetryDto reading)
        {
            var sensor = _allSensors.FirstOrDefault(s => s.Id == reading.SensorId);

            return sensor is not null && sensor.Category.IsActuator() && bool.TryParse(reading.Value, out bool on)
                ? sensor.Category.StateLabel(on)
                : reading.Value;
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Opens the add sensor dialog and, if a sensor was registered, refreshes the list and selects the new row.
        /// </summary>
        private async void OnAddSensorClick(object sender, RoutedEventArgs e)
        {
            var dialog = new AddSensorDialog { Owner = Window.GetWindow(this) };

            if (dialog.ShowDialog() != true || dialog.RegisteredSensor is null)
                return;

            ActionStatusText.Text = $"Registered {dialog.RegisteredSensor.Id}.";
            await RefreshSensorsAsync();
            SelectSensorRow(dialog.RegisteredSensor.Id);

        }
        //----------------------------------------------------------//



        /// <summary>
        /// Builds a jagged batch of sampling windows matching the selected sensor's category and sends it to the gateway.
        /// Records what the batch did to the reading count, then refreshes the sensor list and detail panels to show the result.
        /// </summary>
        private async void OnSimulateBatchClick(object sender, RoutedEventArgs e)
        {
            if (SensorsGrid.SelectedItem is not SensorDto sensor)
            {
                ActionStatusText.Text = "Select a sensor first.";
                return;
            }

            int readingsBefore = sensor.TelemetryReadingCount;

            var rng = Random.Shared;
            int windowCount = rng.Next(4, 8);

            // The wire type, unit and believable range all come from the shared category metadata, so the
            // Part 2 categories (soil moisture, temperature, pumps, valves) batch correctly too.
            var (min, max) = sensor.Category.NormalRange();
            string unit = sensor.Category.Unit();

            var request = sensor.Category.DataType() switch
            {
                TelemetryDataType.Float => new BatchTelemetryIngestRequest(TelemetryDataType.Float, JaggedWindows(
                    windowCount, () => (float)(min + rng.NextDouble() * (max - min))), null, null, unit),

                TelemetryDataType.Int => new BatchTelemetryIngestRequest(
                    TelemetryDataType.Int, null, JaggedWindows(windowCount, () => (int)(min + rng.NextDouble() * (max - min))), null, unit),

                _ => new BatchTelemetryIngestRequest(
                    TelemetryDataType.Bool, null, null, JaggedWindows(windowCount, () => rng.NextDouble() < 0.5), unit)
            };

            try
            {
                var response = await App.ApiClient.IngestBatchAsync(sensor.Id, request);
                if (response is null)
                {
                    ActionStatusText.Text = "The gateway accepted the batch but sent nothing back to show.";
                    return;
                }

                await RefreshSensorsAsync();

                _lastBatch = new LastBatch(
                    sensor.Id,
                    DateTimeOffset.Now,
                    response.WindowSampleCounts,
                    response.PacketsIngested,
                    readingsBefore,
                    _allSensors.FirstOrDefault(s => s.Id == sensor.Id)?.TelemetryReadingCount ?? readingsBefore,
                    response.IngestedReadings.Select(r => r.TimestampUtc).ToHashSet());

                await LoadSensorDetailAsync(sensor.Id);

                ActionStatusText.Text =
                    $"{sensor.Id} · ingested {response.PacketsIngested} readings from a {response.WindowCount}-window " +
                    $"jagged {BatchShapeFor(sensor.Category)} batch.";
            }
            catch (Exception ex)
            {
                ActionStatusText.Text = $"Batch ingest failed: {ex.Message}";
            }
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Builds a jagged array of sampling windows, each holding a random number of samples from the given generator.
        /// The uneven lengths are what make the batch genuinely jagged.
        /// </summary>
        private static T[][] JaggedWindows<T>(int windowCount, Func<T> nextSample) where T : struct
        {
            var windows = new T[windowCount][];

            for (int w = 0; w < windowCount; w++)
            {
                windows[w] = new T[Random.Shared.Next(2, 6)];

                for (int s = 0; s < windows[w].Length; s++)
                    windows[w][s] = nextSample();
            }

            return windows;
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Returns the array shape a sensor category's batch will use, for the status line and tooltip.
        /// </summary>
        private static string BatchShapeFor(SensorCategory category) => category.DataType() switch
        {
            TelemetryDataType.Float => "float[][]",
            TelemetryDataType.Int => "int[][]",
            _ => "bool[][]"
        };
        //----------------------------------------------------------//


        /// <summary>
        /// Shows the summary strip for the most recent simulated batch on this sensor, or hides it if the last batch belonged to another sensor.
        /// The tooltip explains whether the jump in reading count came from this batch or from the ring buffer rolling over.
        /// </summary>

        private void RenderLastBatch(string sensorId)
        {
            if (_lastBatch is not { } batch || batch.SensorId != sensorId)
            {
                LastBatchStrip.Visibility = Visibility.Collapsed;
                return;
            }

            LastBatchSummary.Text = $"{batch.RanAtLocal:HH:mm:ss} · {batch.WindowSampleCounts.Length} windows";

            int added = batch.ReadingsAfter - batch.ReadingsBefore;
            bool rolledOut = added < batch.PacketsIngested;

            LastBatchCountJump.Text = rolledOut
                ? $"readings {batch.ReadingsBefore} → {batch.ReadingsAfter} (+{batch.PacketsIngested}, buffer full)"
                : $"readings {batch.ReadingsBefore} → {batch.ReadingsAfter} (+{batch.PacketsIngested} from this batch)";

            LastBatchCountJump.ToolTip = rolledOut
                ? $"{batch.PacketsIngested} readings were ingested, but this sensor's history is at capacity - the oldest rolled out of the ring buffer to make room."
                : added > batch.PacketsIngested
                    ? $"{batch.PacketsIngested} of the {added} readings added came from this batch; the rest is the background simulation."
                    : $"All {added} readings added came from this batch.";

            LastBatchStrip.Visibility = Visibility.Visible;
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Asks the user to pick a file and uploads it against the selected sensor, where the API encrypts it at rest.
        /// Refreshes the attachment list afterwards and reports any failure in the status line.
        /// </summary>
        private async void OnAttachFileClick(object sender, RoutedEventArgs e)
        {
            if (SensorsGrid.SelectedItem is not SensorDto sensor)
            {
                ActionStatusText.Text = "Select a sensor first.";
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = "Attach a config file, deployment photo, or hardware log",
                Filter = "All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                ActionStatusText.Text = "Uploading and encrypting...";
                await App.ApiClient.UploadAttachmentAsync(sensor.Id, dialog.FileName);
                ActionStatusText.Text = $"Attached '{System.IO.Path.GetFileName(dialog.FileName)}' to {sensor.Id} (AES-256 encrypted at rest).";

                await RefreshSensorsAsync();
                await LoadSensorDetailAsync(sensor.Id);
            }
            catch (Exception ex)
            {
                ActionStatusText.Text = $"Upload failed: {ex.Message}";
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Sends the deployment tree to the API for recursive validation.
        /// Turns the result into either a green valid badge or a red badge listing every offending node.
        /// </summary>
        private async void OnValidateHierarchyClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = await App.ApiClient.ValidateDeploymentAsync(_treeRoots);
                if (result is null)
                    return;

                if (result.IsValid)
                {
                    ValidityDot.Fill = (Brush)FindResource("StatusGreenDot");
                    ValidityText.Text = "Valid";
                    ValidityText.Foreground = (Brush)FindResource("StatusGreen");
                    ValidationErrorsList.Visibility = Visibility.Collapsed;
                    ActionStatusText.Text = "Deployment hierarchy validated - every device is correctly nested.";
                }
                else
                {
                    ValidityDot.Fill = (Brush)FindResource("StatusRed");
                    ValidityText.Text = $"{result.Errors.Count} issue{(result.Errors.Count == 1 ? "" : "s")}";
                    ValidityText.Foreground = (Brush)FindResource("StatusRed");
                    ValidationErrorsList.ItemsSource = result.Errors;
                    ValidationErrorsList.Visibility = Visibility.Visible;
                    ActionStatusText.Text = "Deployment hierarchy has misconfigured nodes.";
                }
            }
            catch (Exception ex)
            {
                ActionStatusText.Text = $"Validation failed: {ex.Message}";
            }
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Polls the engagement summary and updates the health gauge, clean streak and alert list.
        /// A dropped poll is ignored on purpose, since the next tick retries and the nav bar reports a genuinely down gateway.
        /// </summary>
        private async Task RefreshEngagementAsync()
        {
            try
            {
                var engagement = await App.ApiClient.GetEngagementSummaryAsync();
                if (engagement is null)
                    return;

                Gauge.Score = engagement.HealthScore;

                StreakText.Text = engagement.CleanStreak == 1
                    ? "1 clean sweep in a row"
                    : $"{engagement.CleanStreak} clean sweeps in a row";
                StreakText.Foreground = engagement.CleanStreak > 0
                    ? (Brush)FindResource("StatusGreenDot")
                    : (Brush)FindResource("StatusAmberDot");

                AlertsList.ItemsSource = engagement.ActiveAlerts.Take(3).ToList();

                RefreshMeterAggregation();
            }
            catch
            {
                // A dropped poll shouldn't disturb the dashboard
                // the next tick retries, and the nav bar's connection strip is what reports a genuinely down gateway.
            }
        }
        //----------------------------------------------------------//

        /// <summary>
        /// Takes the latest reading from the first two power meters and adds them together using the PowerReading operator.
        /// Shows both meters and the combined total in the aggregation panel, and quietly does nothing if there are fewer than two meters.
        /// </summary>
        private async void RefreshMeterAggregation()
        {
            var meters = _allSensors
            .Where(s => s.Category == SensorCategory.PowerConsumption)
            .OrderBy(s => s.Id, StringComparer.Ordinal)
            .Take(2)
            .ToList();

            if (meters.Count < 2)
                return;

            try
            {
                var readingA = (await App.ApiClient.GetTelemetryAsync(meters[0].Id, take: 1)).FirstOrDefault();
                var readingB = (await App.ApiClient.GetTelemetryAsync(meters[1].Id, take: 1)).FirstOrDefault();

                if (readingA is null || readingB is null)
                    return;

                if (!double.TryParse(readingA.Value, CultureInfo.InvariantCulture, out double wattsA) ||
                    !double.TryParse(readingB.Value, CultureInfo.InvariantCulture, out double wattsB))
                    return;

                var meterA = new PowerReading(meters[0].Id, wattsA);
                var meterB = new PowerReading(meters[1].Id, wattsB);
                var combined = meterA + meterB;

                MeterAName.Text = meterA.MeterId;
                MeterAValue.Text = $"{meterA.Watts:F1} W";
                MeterBName.Text = meterB.MeterId;
                MeterBValue.Text = $"{meterB.Watts:F1} W";
                MeterTotalValue.Text = $"{combined.Watts:F1} W";

                MeterAggregationText.Visibility = Visibility.Collapsed;
                MeterAggregationPanel.Visibility = Visibility.Visible;
            }
            catch
            {
                // Same rationale as the polling tick above.
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Snapshot of the last simulated batch on one sensor, kept so the detail panel can describe what it changed.
        /// </summary>
        private sealed record LastBatch(
            string SensorId,
            DateTimeOffset RanAtLocal,
            int[] WindowSampleCounts,
            int PacketsIngested,
            int ReadingsBefore,
            int ReadingsAfter,
            HashSet<DateTimeOffset> Timestamps);
        //----------------------------------------------------------//
    }


    /// <summary>
    /// Row shape the telemetry history list binds to, with a flag marking readings that came from the last batch.
    /// </summary>
    public sealed record TelemetryRow(DateTimeOffset TimestampUtc, string Value, string Unit, bool FromBatch);
    //----------------------------------------------------------//
}


//------------------------------------------- End Of File -------------------------------------------//
