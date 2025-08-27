// App.xaml.cs
using MahApps.Metro.Controls.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using WS_Setup_6.Common.Interfaces;
using WS_Setup_6.Common.Logging;
using WS_Setup_6.Core.Interfaces;
using WS_Setup_6.Core.Services;
using WS_Setup_6.UI.Services;
using WS_Setup_6.UI.ViewModels;
using WS_Setup_6.UI.ViewModels.Pages;
using WS_Setup_6.UI.Windows;
using WS_Setup_6.UI.Windows.Pages;

namespace WS_Setup_6.UI
{
    [SupportedOSPlatform("windows")]
    public partial class App : Application
    {
        public static IServiceProvider ServiceProvider { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var services = new ServiceCollection();

            //
            // 1) Core / Infrastructure
            //
            services.AddSingleton<IOnboardService, OnboardService>();
            services.AddSingleton<IBaselineService, BaselineService>();
            services.AddTransient<IDomainCredentialsDialogService, DomainCredentialsDialogService>();
            services.AddSingleton<IDialogCoordinator>(_ => DialogCoordinator.Instance);
            services.AddSingleton<IHelpersService, HelpersService>();
            services.AddSingleton<IUninstallService, UninstallService>();
            services.AddTransient<IOemRemovalService, OemRemovalService>();
            services.AddSingleton<IAppInventoryService, AppInventoryService>();
            services.AddSingleton<IUninstallScanner, RegistryUninstallScanner>();

            // Logging
            services.AddSingleton<ILogServiceWithHistory>(sp =>
            {
                var exePath = Environment.ProcessPath;
                var baseDir = Path.GetDirectoryName(exePath)!;
                var logPath = Path.Combine(baseDir, "onboard.log");
                return new LogManager(logPath);
            });
            services.AddSingleton<ILogService>(sp => sp.GetRequiredService<ILogServiceWithHistory>());

            //
            // 2) Navigation
            //
            // a) Register the concrete so its ctor gets IServiceProvider
            services.AddSingleton<NavigationService>(sp => new NavigationService(sp));

            // b) Expose only the Core interface everywhere
            services.AddSingleton<INavigationService>(sp => sp.GetRequiredService<NavigationService>());

            //
            // 3) ViewModels
            //
            services.AddSingleton<LogViewModel>();          // for live log UI
            services.AddSingleton<MainWindowModel>();         // shell VM
            services.AddTransient<HomePageViewModel>();
            services.AddSingleton<ConfigurationPageViewModel>();
            services.AddSingleton<BaselinePageViewModel>();
            services.AddTransient<UninstallViewModel>();

            //
            // 4) Pages
            //
            services.AddTransient<HomePage>();
            services.AddTransient<ConfigurationPage>();
            services.AddSingleton<BaselinePage>();
            services.AddTransient<UninstallPage>();
            services.AddTransient<LogPage>();

            //
            // 5) Shell window
            //
            services.AddSingleton<MainWindow>();

            ServiceProvider = services.BuildServiceProvider();

            //
            // 6) Configure routes
            //
            var nav = ServiceProvider.GetRequiredService<INavigationService>();
            nav.Register("HomePage", typeof(HomePage));
            nav.Register("ConfigurationPage", typeof(ConfigurationPage));
            nav.Register("BaselinePage", typeof(BaselinePage));
            nav.Register("UninstallPage", typeof(UninstallPage));
            nav.Register("LogPage", typeof(LogPage));
            

            //
            // 7) Show shell and land on HomePage
            //
            var shell = ServiceProvider.GetRequiredService<MainWindow>();
            shell.Show();
            nav.NavigateTo("HomePage");
        }
    }
}