using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Hosting;
using Orleans.Configuration;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Runtime.Loader;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.IO;

namespace Backend
{
    public class Program
    {
        private static ISiloHost _silo;
        private static readonly ManualResetEvent _siloStopped = new ManualResetEvent(false);
        
        public static void Main(string[] args)
        {
            var externalAssemblies = LoadExternalAssemblies().ToArray();

            var env = Environment.GetEnvironmentVariable("ORLEANS_CONFIG");

            var builder = new SiloHostBuilder();

            builder
                .ConfigureLogging((context, loggingBuilder) => loggingBuilder.AddConsole())
                .Configure<ProcessExitHandlingOptions>(options =>
                {
                    options.FastKillOnProcessExit = false;
                })
                .Configure<ClusterOptions>(options =>
                {
                    options.ClusterId = "serverlessorleans";
                    options.ServiceId = "serverlessorleans";
                })
                .ConfigureApplicationParts(parts =>
                {
                    foreach(var assembly in externalAssemblies)
                    {
                        System.Console.WriteLine("Loading orleans app parts: " + assembly.FullName);
                        parts.AddApplicationPart(assembly);
                    }
                })
                .UseDashboard(options =>
                {
                    options.CounterUpdateIntervalMs = 5000;
                    options.HostSelf = false;
                });

            if (env == "STORAGE")
            {
                builder
                    .AddAzureBlobGrainStorageAsDefault(options =>
                    {
                        options.ConnectionString = Environment.GetEnvironmentVariable("StorageConnectionString");
                        options.UseJson = true;
                        options.IndentJson = true;
                        options.ContainerName = "actorstate";
                    })
                    .UseAzureStorageClustering(options =>
                    {
                        options.ConnectionString = Environment.GetEnvironmentVariable("StorageConnectionString");
                        options.TableName = "clusterstate";
                    })
                    .ConfigureEndpoints(11111, 30000);
            }
            else if (env == "SQL")
            {
                builder
                    .AddAdoNetGrainStorageAsDefault(options =>
                    {
                        options.Invariant = "System.Data.SqlClient";
                        options.UseJsonFormat = true;
                        options.IndentJson = true;
                        options.ConnectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
                    })
                    .UseAdoNetClustering(options =>
                    {
                        options.Invariant = "System.Data.SqlClient";
                        options.ConnectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
                    })
                    .ConfigureEndpoints(11111, 30000);
            }
            else
            {
                throw new Exception("ORLEANS_CONFIG envvar not defined.");
            }

            _silo = builder.Build();

            Task.Run(StartSilo);

            AssemblyLoadContext.Default.Unloading += context =>
            {
                Task.Run(StopSilo);
                _siloStopped.WaitOne();
            };

            _siloStopped.WaitOne();
        }

        private static async Task StartSilo()
        {
            await _silo.StartAsync();
            Console.WriteLine("Silo started");
        }

        private static async Task StopSilo()
        {
            await _silo.StopAsync();
            Console.WriteLine("Silo stopped");
            _siloStopped.Set();
        }

        private static IEnumerable<Assembly> LoadExternalAssemblies()
        {
            var appPath = AppDomain.CurrentDomain.BaseDirectory + "/app";

            foreach(var assemblyPath in Directory.GetFiles(appPath, "*.dll"))
            {
                yield return Assembly.LoadFrom(assemblyPath);
            }
        }
    }
}
