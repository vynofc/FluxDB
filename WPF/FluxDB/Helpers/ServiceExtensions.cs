namespace FluxDB.Helpers
{
    public static class ServiceExtensions
    {
        public static IServiceCollection AddFluxDB(this IServiceCollection services)
        {
            // Services
            services.AddSingleton<SettingsService>();
            services.AddSingleton<DatabaseService>(sp =>
            {
                var settings = sp.GetRequiredService<SettingsService>().Load();
                var folder = settings.LastRootFolder;
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                    folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var dbPath = Path.Combine(folder, ".fluxdb");
                return new DatabaseService(dbPath);
            });
            services.AddSingleton<IndexerService>();
            services.AddSingleton<ExportService>();
            services.AddSingleton<ImportService>();

            // ViewModels
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<NavigationViewModel>();
            services.AddSingleton<DashboardViewModel>();

            // Windows & Pages
            services.AddSingleton<MainWindow>();
            services.AddSingleton<SettingsWindow>();
            services.AddSingleton<DashboardPage>();
            services.AddSingleton<FileBrowserPage>();

            // WPF-UI Services
            services.AddSingleton<ISnackbarService, SnackbarService>();
            services.AddSingleton<IContentDialogService, ContentDialogService>();

            return services;
        }
    }
}