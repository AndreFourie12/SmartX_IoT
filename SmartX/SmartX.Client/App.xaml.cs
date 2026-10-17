using SmartX.Client.Services;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;

namespace SmartX.Client
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private const string DefaultApiBaseAddress = "http://localhost:5224";

        /// <summary>
        /// The gateway's address. Defaults to the address the API's launch profile and Docker Compose
        /// both expose; set the SMARTX_API_URL environment variable to point the client elsewhere
        /// without recompiling.
        /// </summary>
        public static string ApiBaseAddress { get; } =
            Environment.GetEnvironmentVariable("SMARTX_API_URL") is { Length: > 0 } configured ? configured : DefaultApiBaseAddress;

        public static SmartXApiClient ApiClient { get; } = new(ApiBaseAddress);


        private readonly HashSet<string> _reportedErrors = [];


        /// <summary>
        /// Pins the UI to one culture so numbers, dates and the API's invariant-formatted text read consistently,
        /// and installs a last-resort handler so an unexpected UI exception is reported instead of closing the app.
        /// </summary>
        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += OnUnhandledUiException;

            var culture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;

            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

            base.OnStartup(e);
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Every view already handles the failures it expects (gateway down, request rejected). This catches
        /// anything that slips through: it is written to %LOCALAPPDATA%\SmartX\client-errors.log and shown once
        /// per distinct message, and the app keeps running rather than closing on the operator.
        /// </summary>
        private void OnUnhandledUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;

            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmartX");
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "client-errors.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}{Environment.NewLine}{Environment.NewLine}");
            }
            catch
            {
                // Logging is best-effort; never let the handler itself fail.
            }

            if (_reportedErrors.Add(e.Exception.Message))
            {
                MessageBox.Show(
                    $"Something unexpected went wrong, but Smart-X is still running.\n\n{e.Exception.Message}",
                    "Smart-X",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
