using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace DosBoxHardDiskActivity;

class Program
{
    static async Task Main(string[] args)
    {
        // Load YAML configuration manually
        var configData = LoadYamlConfiguration("config.yaml");
        
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(configData!);
            })
            .ConfigureServices((context, services) =>
            {
                services.Configure<AppConfiguration>(context.Configuration.GetSection("config"));
                services.Configure<SoundsConfiguration>(context.Configuration.GetSection("sounds"));
                services.AddSingleton<AudioPlayer>(sp => 
                {
                    var logger = sp.GetRequiredService<ILogger<AudioPlayer>>();
                    var appConfig = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AppConfiguration>>().Value;
                    var basePath = AppContext.BaseDirectory;
                    return new AudioPlayer(logger, basePath, appConfig.Volume);
                });
                services.AddSingleton<GpioController>();
                services.AddHostedService<FileSystemMonitorService>();
            })
            .ConfigureLogging((context, logging) =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .Build();

        await host.RunAsync();
    }

    private static Dictionary<string, string?> LoadYamlConfiguration(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Configuration file not found: {filePath}");
        }

        var lines = File.ReadAllLines(filePath);
        var config = new Dictionary<string, string?>();
        var currentSection = "";
        var listIndex = 0;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                continue;

            var indent = line.Length - line.TrimStart().Length;
            var trimmedLine = line.Trim();

            if (indent == 0 && trimmedLine.EndsWith(":"))
            {
                // Top-level section
                currentSection = trimmedLine.TrimEnd(':');
                listIndex = 0;
            }
            else if (indent == 2 && trimmedLine.StartsWith("-"))
            {
                // List item
                var value = trimmedLine.Substring(1).Trim().Trim('"');
                config[$"{currentSection}:directories:{listIndex}"] = value;
                listIndex++;
            }
            else if (indent == 2 && trimmedLine.Contains(":"))
            {
                // Key-value pair under section
                var parts = trimmedLine.Split(':', 2);
                var key = parts[0].Trim();
                var value = parts[1].Trim().Trim('"');
                
                // Convert snake_case to PascalCase for proper binding
                var configKey = ConvertToPascalCase(key);
                config[$"{currentSection}:{configKey}"] = value;
            }
        }

        return config;
    }

    private static string ConvertToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;
            
        var parts = input.Split('_');
        var result = string.Concat(parts.Select(part => 
            char.ToUpper(part[0]) + part.Substring(1).ToLower()));
        return result;
    }
}
