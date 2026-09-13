using SmartX.Client.Services;
using System.Configuration;
using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Markup;

namespace SmartX.Client
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private const string ApiBaseAddress = "http://localhost:5224";

        public static SmartXApiClient ApiClient { get; } = new(ApiBaseAddress);


        /// <summary>
        /// 
        /// </summary>
        protected override void OnStartup(StartupEventArgs e)
        {
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
    }
}

//------------------------------------------- End Of File -------------------------------------------//