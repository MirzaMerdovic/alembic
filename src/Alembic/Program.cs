﻿using System.IO;
using System.Threading.Tasks;
using Alembic.Common.Resiliency;
using Alembic.Common.Services;
using Alembic.Docker;
using Alembic.Docker.Client;
using Alembic.Reporting.Slack;
using Alembic.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Alembic
{
    internal static class Program
    {
        internal static Task Main(string[] args)
        {
            return CreateHostBuilder(args).Build().RunAsync();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((context, builder) =>
                {
                    builder.SetBasePath(Directory.GetCurrentDirectory());
                    builder.AddJsonFile("appsettings.json", false, true);
                })
                .ConfigureServices((context, services) =>
                {
                    services.Configure<ManagedHandlerFactoryOptions>(context.Configuration.GetSection("DockerClientFactoryOptions"));
                    services.Configure<RetryProviderOptions>(context.Configuration.GetSection("RetryProviderOptions"));
                    services.Configure<WebHookReporterOptions>(context.Configuration.GetSection("WebHookReporterOptions"));
                    services.Configure<DockerMonitorOptions>(context.Configuration.GetSection("DockerMonitorOptions"));

                    services.AddLogging(x => x.AddConsole());

                    services.AddHttpClient();

                    services.AddSingleton<IReporter, WebHookReporter>();
                    services.AddSingleton<IRetryProvider, RetryProvider>();
                    services.AddSingleton<IManagedHandlerFactory, ManagedHandlerFactory>();
                    services.AddSingleton<IDockerClient, DockerClient>();
                    services.AddSingleton<IDockerApi, DockerApi>();
                    services.AddSingleton<IContainerRetryTracker, ContainerRetryTracker>();
                    services.AddTransient<IDockerMonitor, DockerMonitor>();

                    services.AddHostedService<AlembicHost>();
                });
    }
}