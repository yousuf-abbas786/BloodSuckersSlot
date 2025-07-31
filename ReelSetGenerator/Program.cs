using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;

namespace ReelSetGenerator
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("=== BloodSuckers Slot Reel Set Generator with Threading ===");
            Console.WriteLine($"Starting at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"Processor Count: {Environment.ProcessorCount}");
            Console.WriteLine($"OS: {Environment.OSVersion}");
            Console.WriteLine($"Framework: {Environment.Version}");
            Console.WriteLine();

            var host = CreateHostBuilder(args).Build();
            
            // Add performance monitoring
            var startTime = DateTime.UtcNow;
            Console.WriteLine($"Generation started at: {startTime:yyyy-MM-dd HH:mm:ss}");
            
            try
            {
                host.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during generation: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            finally
            {
                var endTime = DateTime.UtcNow;
                var totalTime = endTime - startTime;
                Console.WriteLine();
                Console.WriteLine($"Generation completed at: {endTime:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"Total execution time: {totalTime.TotalMinutes:F2} minutes ({totalTime.TotalHours:F2} hours)");
                Console.WriteLine("=== Generation Complete ===");
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.SetMinimumLevel(LogLevel.Information);
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<Worker>();
                });
    }
}
