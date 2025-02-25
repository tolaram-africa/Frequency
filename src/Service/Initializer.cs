using System.Net;
using Frequency.Common.Config;
using Frequency.Extensions;
using Serilog;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Frequency;

internal static class Initializer {
    internal static ILogger GetLogger<T>() {
        var loggerFactory = LoggerFactory.Create(builder => {
            builder.ClearProviders();
            builder.AddSerilog();
        });

        return loggerFactory.CreateLogger<T>();
    }

    private static IConfigurationBuilder LoadConfigurations(this IConfigurationBuilder configs) {
        configs.AddYamlFile("config.yaml", false, true);
        var logger = GetLogger<IConfigurationBuilder>();
        var files = new List<string> { "proxy", "server", "network" };
        files.ForEach(n => {
            try {
                configs.AddYamlFile($"config.{n}.yaml", true, true);
            }
            catch (Exception e) {
                logger.LogError("Configuration contains error: {error}", e);
                throw;
            }
        });

        return configs;
    }

    private static WebApplicationBuilder SetupLogger(this WebApplicationBuilder builder) {
        Log.Logger = new LoggerConfiguration().WriteTo
            .Console()
            .WriteTo.File("logs/app.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog();
        builder.Host.UseSerilog();

        return builder;
    }

    internal static WebApplicationBuilder RegisterConfigurations(this WebApplicationBuilder builder) {
        builder.Host.UseSystemd();
        builder.SetupLogger();
        builder.Configuration.LoadConfigurations();
        builder.Services.AddAutoMapper(typeof(Program));

        var config = builder.Configuration;
        builder.Services.AddOptions<SystemConfig>().Bind(config.GetSection(SystemConfig.Key));
        builder.Services.AddOptions<ServerConfig>().Bind(config.GetSection(ServerConfig.Key));
        builder.Services.AddOptions<TerminalConfig>().Bind(config.GetSection(TerminalConfig.Key));
        builder.Services.AddOptions<QueueConfig>().Bind(config.GetSection(QueueConfig.Key));
        builder.Services.AddOptions<DatabaseConfig>().Bind(config.GetSection(DatabaseConfig.Key));
        builder.Services.AddOptions<List<SerialConfig>>().Bind(config.GetSection(SerialConfig.Key));
        builder.Services
            .AddOptions<List<NetConfig>>()
            .Bind(config.GetSection(NetConfig.Key));

        return builder;
    }

    internal static WebApplicationBuilder RegisterHostOptions(this WebApplicationBuilder builder) {
        var config = builder.Configuration;
        var logger = GetLogger<WebApplicationBuilder>();
        var defaultOptions = new SystemConfig();
        var serverOptions = new ServerConfig();
        var proxyOptions = new TerminalConfig();
        var queueOptions = new QueueConfig();

        config.GetSection(SystemConfig.Key).Bind(defaultOptions);
        config.GetSection(ServerConfig.Key).Bind(serverOptions);
        config.GetSection(TerminalConfig.Key).Bind(proxyOptions);
        config.GetSection(QueueConfig.Key).Bind(queueOptions);

        var proxy = defaultOptions!.Terminal;
        var port = proxy ? proxyOptions.Port : serverOptions.Port;
        var host = proxy ? proxyOptions.Host : serverOptions.Host;

        logger.LogInformation("Setting up host options..");
        builder.WebHost.UseKestrel(o => {
            o.Limits.MaxConcurrentConnections = 1024;
            o.Limits.MaxConcurrentUpgradedConnections = 1024;
            o.Limits.MaxRequestBodySize = 52428800;
            o.Listen(defaultOptions.Web ? host : IPAddress.None, port);
        });
        builder.RegisterQueueHost();
        builder.Host.ConfigureHostOptions(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));

        return builder;
    }
}