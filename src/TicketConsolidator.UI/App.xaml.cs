using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Windows;
using TicketConsolidator.Application.Configurations;
using TicketConsolidator.Application.Interfaces;
using TicketConsolidator.Infrastructure.Services;
using TicketConsolidator.UI.Views;
using MaterialDesignThemes.Wpf;

namespace TicketConsolidator.UI
{
    public partial class App : System.Windows.Application
    {
        public IServiceProvider ServiceProvider { get; private set; }
        public IConfiguration Configuration { get; private set; }

        public App()
        {
            try
            {
               // This method loads App.xaml, so XamlParseException will happen here
               // Note: InitializeComponent is generated in the obj folder but typically called here or separate Main.
               // Actually, usually Main calls new App().InitializeComponent().
               // But let's see if we can catch it here. 
               // Wait, InitializeComponent is defined in the partial class (generated).
               // If I don't call it, Main calls it.
               // Main: App app = new App(); app.InitializeComponent(); app.Run();
            }
            catch(Exception ex) 
            {
               File.WriteAllText("pre_startup_error.txt", ex.ToString());
               throw; // rethrow to crash but we have the log
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // Enable Multicore JIT Optimization to speed up startup and dialogs
            try
            {
                System.Runtime.ProfileOptimization.SetProfileRoot(Directory.GetCurrentDirectory());
                System.Runtime.ProfileOptimization.StartProfile("Startup.profile");
            }
            catch { /* Ignored */ }





            // Global Exception Hooks
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;

            try
            {
                var builder = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

                Configuration = builder.Build();

                var serviceCollection = new ServiceCollection();
                serviceCollection.AddSingleton<IConfiguration>(Configuration);
                ConfigureServices(serviceCollection);

                ServiceProvider = serviceCollection.BuildServiceProvider();

                try
                {
                    var settings = ServiceProvider.GetRequiredService<TicketConsolidator.Infrastructure.Services.SettingsService>();
                    // Theme logic reverted
                }
                catch { /* Ignored */ }

                var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
                mainWindow.Show();
            }
            catch (Exception ex)
            {
                // Fallback logging if DI fails
                File.WriteAllText("startup_critical_error.txt", ex.ToString());
                MessageBox.Show($"Startup Error: {ex.Message}\n\nCheck 'startup_critical_error.txt' for details.", "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            LogAndReport(e.Exception, "Unhandled UI Exception");
            e.Handled = true; // Prevent immediate crash if possible, but state might be corrupt
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if(e.ExceptionObject is Exception ex)
            {
                LogAndReport(ex, "Fatal Non-UI Exception");
            }
        }

        private void LogAndReport(Exception ex, string context)
        {
            try
            {
                // Try to use our LoggerService
                if (ServiceProvider != null)
                {
                    var logger = ServiceProvider.GetService<ILoggerService>();
                    if (logger != null)
                    {
                        logger.LogError($"[{context}] {ex.Message}\n{ex.StackTrace}");
                    }
                }
                else
                {
                     // Fallback
                     File.AppendAllText("crash_dump.txt", $"[{DateTime.Now}] [{context}] {ex}\n\n");
                }
            }
            finally
            {
                 MessageBox.Show($"An unexpected error occurred.\n\nError: {ex.Message}\n\nThe error has been recorded in the Logs.", 
                     "Application Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // Configuration
            var emailConfig = Configuration.GetSection("EmailSettings").Get<EmailConfiguration>() ?? new EmailConfiguration();
            services.AddSingleton(emailConfig);

            // Core Services
            services.AddSingleton<IEncryptionService, EncryptionService>();
            services.AddSingleton<IEmailService, EmailService>();
            services.AddSingleton<ISqlParserService, SqlParserService>();
            services.AddSingleton<IScriptValidatorService, ScriptValidatorService>();
            services.AddSingleton<IConsolidationService, ConsolidationService>();
            services.AddSingleton<ILoggerService, LoggerService>(); // NEW
            services.AddSingleton<SettingsService>();

            // ViewModels
            services.AddTransient<MainWindowViewModel>();
            services.AddSingleton<DashboardViewModel>();
            services.AddTransient<LogsViewModel>(); // NEW
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<HelpViewModel>();

            // Views
            services.AddSingleton<MainWindow>();
            services.AddSingleton<DashboardView>();
            services.AddSingleton<SettingsView>();
            services.AddSingleton<LogsView>();
            services.AddSingleton<HelpView>();
        }
    }
}