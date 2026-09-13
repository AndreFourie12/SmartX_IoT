using Microsoft.Win32;
using SmartX.Shared.Contracts;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SmartX.Client.Dialogs;
using SmartX.Shared.Models;
using SmartX.Shared.Telemetry;
using SmartX.Shared.Validation;
namespace SmartX.Client.Views
{
    /// <summary>
    /// Interaction logic for SensorIngestionView.xaml
    /// </summary>
    public partial class SensorIngestionView : UserControl
    {
        private readonly ObservableCollection<SensorDto> _rows = [];
        private readonly DispatcherTimer _liveTimer = new() { Interval = TimeSpan.FromSeconds(3) };

        private List<SensorDto> _allSensors = [];
        private List<DeploymentNode> _treeRoots = [];

        private string? _scopePath;

        private string? _deviceScopeId;

        private string? _selectedSensorId;

        private LastBatch? _lastBatch;


        /// <summary>
        /// 
        /// </summary>
        public SensorIngestionView()
        {
            InitializeComponent();
            SensorsGrid.ItemsSource = _rows;

            _liveTimer.Tick += async (_, _) => await RefreshEngagementAsync();
            Loaded += OnLoadedAsync;
        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        public async void OnLoadedAsync(object sender, RoutedEventArgs e)
        {
            await RefreshSensorsAsync();
            await RefreshEngagementAsync();
            _liveTimer.Start();
        }
        //----------------------------------------------------------//


        /// <summary>
        /// 
        /// </summary>
        private async void OnRefreshClick(object sender, RoutedEventArgs e) 
            => await RefreshSensorsAsync();
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        private async Task RefreshSensorsAsync()
        {
            try
            {
                _allSensors = await App.ApiClient.GetSensorAsync();
                BuildTree();
                ApplyScope();
            }
            catch (Exception ex)
            {
                ActionStatusText.Text = $"Could not reach the gateway: {ex.Message}";
            }
        }
        //----------------------------------------------------------//



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



        /// <summary>
        /// 
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
        /// 
        /// </summary>
        private static DeploymentNodeType NodeTypeForDepth(int depth) => depth switch
        {
            0 => DeploymentNodeType.Facility,
            1 => DeploymentNodeType.Zone,
            _ => DeploymentNodeType.SubZone
        };
        //----------------------------------------------------------//



        /// <summary>
        /// 
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
        /// 
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
        /// 
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
        /// 
        /// </summary>
        private void OnShowAllClick(object sender, RoutedEventArgs e)
        {
            _scopePath = null;
            _deviceScopeId = null;
            ApplyScope();
        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
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
        /// 
        /// </summary>
        private void UpdateBatchButton()
        {
            var sensor = SensorsGrid.SelectedItem as SensorDto;

            SimulateBatchButton.IsEnabled = sensor is not null;
            SimulateBatchButton.ToolTip = sensor is null
                ? "Select a sensor in the table above."
                : $"Send {sensor.Id} a jagged {BatchShapeFor(sensor.Category)} batch of sampling windows.";
        }
        //----------------------------------------------------------//


        /// <summary>
        /// 
        /// </summary>
        private void SelectSensorRow(string sensorId)
        {
            _selectedSensorId = sensorId;
            RestoreRowSelection();
        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
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
        /// 
        /// </summary>
        private async void OnSensorSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SensorsGrid.SelectedItem is not SensorDto sensor)
                return;

            _selectedSensorId = sensor.Id;
            ActionStatusText.Text = $"{sensor.Id} selected · {sensor.Location}";
            await LoadSensorDetailAsync(sensor.Id);
        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        private async Task LoadSensorDetailAsync(string sensorId)
        {
            try
            {
                RenderLastBatch(sensorId);

                var history = await App.ApiClient.GetTelemetryAsync(sensorId, take: 40);
                TelemetryHistoryList.ItemsSource = history.Select(ToRow).ToList();
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
        /// 
        /// </summary>
        private TelemetryRow ToRow(TelemetryDto reading) =>
            new(reading.TimestampUtc,
                reading.Value,
                reading.Unit,
                _lastBatch is not null
                    && _lastBatch.SensorId == reading.SensorId
                    && _lastBatch.Timestamps.Contains(reading.TimestampUtc));
        //----------------------------------------------------------//



        /// <summary>
        /// 
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
        /// 
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

            var request = sensor.Category switch
            {
                SensorCategory.Environmental => new BatchTelemetryIngestRequest(TelemetryDataType.Float,JaggedWindows(
                    windowCount, () => 35f + (float)rng.NextDouble() * 35f),null, null,"%"),

                SensorCategory.PowerConsumption => new BatchTelemetryIngestRequest(
                    TelemetryDataType.Int, null,JaggedWindows(windowCount, () => 150 + rng.Next(-25, 25)),null,"W"),

                _ => new BatchTelemetryIngestRequest(
                    TelemetryDataType.Bool,null,null,JaggedWindows(windowCount, () => rng.NextDouble() < 0.5),string.Empty)
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

        private static string BatchShapeFor(SensorCategory category) => category switch
        {
            SensorCategory.Environmental => "float[][]",
            SensorCategory.PowerConsumption => "int[][]",
            _ => "bool[][]"
        };


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


        /// <summary>
        /// 
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
        /// 
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

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
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


        private sealed record LastBatch(
            string SensorId,
            DateTimeOffset RanAtLocal,
            int[] WindowSampleCounts,
            int PacketsIngested,
            int ReadingsBefore,
            int ReadingsAfter,
            HashSet<DateTimeOffset> Timestamps);
    }

    public sealed record TelemetryRow(DateTimeOffset TimestampUtc, string Value, string Unit, bool FromBatch);
}


//------------------------------------------- End Of File -------------------------------------------//
