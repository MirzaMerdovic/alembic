﻿using Alembic.Docker;
using Alembic.Docker.Contracts;
using Alembic.Docker.Reporting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static Alembic.Docker.DockerApi;

namespace Alembic.Services
{
    public interface IDockerMonitor
    {
        Task<string> Ping(CancellationToken cancellation);

        Task<IEnumerable<ContainerInfo>> GetContainers(CancellationToken cancellation);

        Task<Container> InspectContainer(string id, CancellationToken cancellation);

        Task<HttpStatusCode> RestartContainer(string id, CancellationToken cancellation);

        Task<HttpStatusCode> KillContainer(string id, CancellationToken cancellation);

        Task MonitorHealthStatus(CancellationToken cancellation);
    }

    public class DockerMonitor : IDockerMonitor
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);
        private static readonly IEnumerable<ApiResponseErrorHandlingDelegate> NoErrorHandlers = Enumerable.Empty<ApiResponseErrorHandlingDelegate>();

        private ConcurrentDictionary<string, int> _containerRetries = new ConcurrentDictionary<string, int>();

        private readonly IDockerApi _client;
        private readonly IReporter _reporter;
        private readonly ILogger _logger;

        public DockerMonitor(IDockerApi client, IReporter reporter, ILogger<DockerMonitor> logger)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _reporter = reporter ?? throw new ArgumentNullException(nameof(client));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<string> Ping(CancellationToken cancellation)
        {
            (HttpStatusCode code, string body) = await _client.MakeRequestAsync(NoErrorHandlers, HttpMethod.Get, "_ping", null, null, Timeout, cancellation);

            if (code == HttpStatusCode.OK)
                return body;

            throw new DockerApiException(code, "Unable to ping Docker server");
        }

        public async Task<IEnumerable<ContainerInfo>> GetContainers(CancellationToken cancellation)
        {
            (HttpStatusCode status, string body) = await _client.MakeRequestAsync(NoErrorHandlers, HttpMethod.Get, "containers/json", "all=true", null, Timeout, cancellation);

            if (status == HttpStatusCode.OK)
            {
                var containers = JsonConvert.DeserializeObject<ContainerInfo[]>(body);

                return containers;
            }

            return Enumerable.Empty<ContainerInfo>();
        }

        public async Task<Container> InspectContainer(string id, CancellationToken cancellation)
        {
            (HttpStatusCode status, string body) = await _client.MakeRequestAsync(NoErrorHandlers, HttpMethod.Get, $"containers/{id}/json", null, null, Timeout, cancellation);

            if(status == HttpStatusCode.OK)
            {
                var container = JsonConvert.DeserializeObject<Container>(body);

                return container;
            }

            if(status == HttpStatusCode.NotFound)
                return null;

            return null;
        }

        public Task<HttpStatusCode> RestartContainer(string id, CancellationToken cancellation)
        {
            return RestartContainer(id, $"Container: {id} restarted.", cancellation);
        }

        public async Task<HttpStatusCode> KillContainer(string id, CancellationToken cancellation)
        {
            (HttpStatusCode status, string _) = await _client.MakeRequestAsync(NoErrorHandlers, HttpMethod.Post, $"containers/{id}/kill ", null, null, Timeout, cancellation);

            if (status == HttpStatusCode.NoContent)
                await _reporter.Send(new { text = $"Container: {id} killed." }, cancellation);

            return status;
        }

        public async Task MonitorHealthStatus(CancellationToken cancellation)
        {
            var stream = await _client.MakeRequestForStreamAsync(NoErrorHandlers, HttpMethod.Get, "events", @"filters=%7B%22event%22%3A%7B%22health_status%22%3Atrue%7D%7D", null, Timeout, cancellation);

            using (cancellation.Register(Callback, stream, false))
            {
                using var reader = new StreamReader(stream, new UTF8Encoding(false));

                string line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    var containerHealth = JsonConvert.DeserializeObject<ContainerInfo>(line);

                    if (containerHealth.Status.Split(":")[1].Trim() == "unhealthy")
                    {
                        if (_containerRetries.TryGetValue(containerHealth.Id, out var retryCount))
                            _containerRetries[containerHealth.Id] = _containerRetries[containerHealth.Id] + 1;
                        else
                            _containerRetries[containerHealth.Id] = 1;

                        if (_containerRetries[containerHealth.Id] >= 3)
                        {
                            var killStatus = await KillContainer(containerHealth.Id, cancellation);

                            _logger.LogWarning($"Kill operation preformed on container: {containerHealth.Id} successfully: {killStatus == HttpStatusCode.NoContent}. Note: Containers that stay unhealty after 3 restarts get killed");

                            continue;
                        }

                        var status = await RestartContainer(containerHealth.Id, $"Container: {containerHealth.Id} restarted. Count: {_containerRetries[containerHealth.Id]}", cancellation);
                        _logger.LogInformation($"Container: {containerHealth.Id} restart completed successfully: {status == HttpStatusCode.NoContent}");
                    }
                }
            }
        }

        private async Task<HttpStatusCode> RestartContainer(string id, string reportMessage, CancellationToken cancellation)
        {
            (HttpStatusCode status, _) = await _client.MakeRequestAsync(NoErrorHandlers, HttpMethod.Post, $"containers/{id}/restart ", null, null, Timeout, cancellation);

            if (status == HttpStatusCode.NoContent)
                await _reporter.Send(new { text = reportMessage }, cancellation);

            return status;
        }

        private static readonly Action<object?> Callback = delegate (object? stream)
        {
            ((IDisposable)stream).Dispose();
        };
    }
}