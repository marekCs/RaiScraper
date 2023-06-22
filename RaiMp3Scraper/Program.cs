using RaiMp3Scraper.Helpers;
using Serilog;
using System.Diagnostics;

namespace RaiMp3Scraper
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Process? currentProcess = Process.GetCurrentProcess();
            ProcessModule? processModule = currentProcess.MainModule ?? throw new Exception("I can't run the program because the process path in the ProcessModule is null.");
            string? pathToExe = processModule.FileName;
            string? pathToContentRoot = Path.GetDirectoryName(pathToExe) ?? throw new Exception("Cannot find a relative path to the program, it is null.");

            Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(new ConfigurationBuilder()
                .SetBasePath(pathToContentRoot)
                .AddJsonFile("appsettings.json")
                .Build())
            .CreateLogger();

            try
            {
                CreateHostBuilder(args).Build().Run();
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseWindowsService()
            .ConfigureAppConfiguration((hostingContext, config) =>
            {
                var env = hostingContext.HostingEnvironment;
                config.SetBasePath(env.ContentRootPath);
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                config.AddEnvironmentVariables();
            })
            .UseSerilog()
            .ConfigureServices((hostContext, services) =>
            {
                var configuration = hostContext.Configuration;
                var appSettingsSection = configuration.GetSection("AppSettings");
                var appSettings = appSettingsSection.Get<AppSettings>();

                services.Configure<AppSettings>(appSettingsSection);
                if (appSettings != null)
                {
                    services.AddSingleton(appSettings);
                }
                services.AddHostedService<Worker>();
            });
    }

}