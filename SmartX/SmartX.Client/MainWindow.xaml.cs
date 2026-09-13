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
        /// 
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
        /// 
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
                // Reaching the endpoint at all is the connection test, so a failure here is
                // exactly what "disconnected" means - surface it rather than swallowing it.
                ConnectionDot.Fill = (Brush)FindResource("StatusRed");
                ConnectionText.Text = "Gateway unreachable";
                ThroughputText.Text = "start SmartXApp.Api";
            }
        }
        //----------------------------------------------------------//
    }
}


//------------------------------------------- End Of File -------------------------------------------//