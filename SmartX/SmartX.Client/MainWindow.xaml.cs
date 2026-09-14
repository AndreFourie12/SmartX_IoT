using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using SmartX.Client.Views;

namespace SmartX.Client
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(3) };



        /// <summary>
        /// Sets up the window and drops the sensor ingestion view into the main content area.
        /// Starts a three second timer that keeps the gateway status strip up to date.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();

            MainContent.Content = new SensorIngestionView();

            _statusTimer.Tick += async (_, _) => await RefreshGatewayStatusAsync();
            Loaded += async (_, _) =>
            {
                await RefreshGatewayStatusAsync();
                _statusTimer.Start();
            };
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Asks the API for its current status and updates the connection dot and throughput text.
        /// Any failure is treated as the gateway being unreachable, which is exactly what this check is meant to detect.
        /// </summary>
        private async Task RefreshGatewayStatusAsync()
        {
            try
            {
                var status = await App.ApiClient.GetGatewayStatusAsync();
                if (status is null)
                    return;

                ConnectionDot.Fill = (Brush)FindResource("StatusGreenDot");
                ConnectionText.Text = "Gateway connected";
                ThroughputText.Text = $"{status.MessagesPerSecond:F1} msg/s · up {status.Uptime:hh\\:mm\\:ss}";
            }
            catch
            {
                ConnectionDot.Fill = (Brush)FindResource("StatusRed");
                ConnectionText.Text = "Gateway unreachable";
                ThroughputText.Text = "start SmartXApp.Api";
            }
        }
        //----------------------------------------------------------//
    }
}


//------------------------------------------- End Of File -------------------------------------------//