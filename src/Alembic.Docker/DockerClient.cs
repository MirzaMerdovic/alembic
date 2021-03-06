﻿using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Alembic.Docker.Client;

namespace Alembic.Docker
{
    public interface IDockerClient
    {
        Task<(HttpStatusCode responseStatus, string responseBody)> MakeRequestAsync(HttpMethod method, string path, string queryString, TimeSpan timeout, CancellationToken cancellation);

        Task<Stream> MakeRequestForStreamAsync(HttpMethod method, string path, string queryString, TimeSpan timeout, CancellationToken cancellation);
    }

    public sealed class DockerClient : IDockerClient
    {
        private const string UserAgent = "Alembic";

        private static readonly TimeSpan InfiniteTimeout = TimeSpan.FromMilliseconds(Timeout.Infinite);
        private static readonly Version ApiVersion = new Version("1.40");

        private readonly IManagedHandlerFactory _factory;

        public DockerClient(IManagedHandlerFactory factory)
        {
            _factory = factory;
        }

        public async Task<(HttpStatusCode responseStatus, string responseBody)> MakeRequestAsync(
            HttpMethod method,
            string path,
            string queryString,
            TimeSpan timeout,
            CancellationToken cancellation)
        {
            var httpClient = _factory.GetOrCreate();
            var request = PrepareRequest(method, httpClient.BaseAddress, path, queryString);

            var response = await PrivateMakeRequestAsync(request, timeout, cancellation);

            await HandleIfErrorResponseAsync(response.StatusCode, response, cancellation);

            var responseBody = await response.Content.ReadAsStringAsync();

            return (response.StatusCode, responseBody);
        }

        public async Task<Stream> MakeRequestForStreamAsync(
            HttpMethod method,
            string path,
            string queryString,
            TimeSpan timeout,
            CancellationToken cancellation)
        {
            var httpClient = _factory.GetOrCreate();
            var request = PrepareRequest(method, httpClient.BaseAddress, path, queryString);

            var response = await PrivateMakeRequestAsync(request, timeout, cancellation);

            await HandleIfErrorResponseAsync(response.StatusCode, response, cancellation);

            return await response.Content.ReadAsStreamAsync();
        }

        private async Task<HttpResponseMessage> PrivateMakeRequestAsync(
            HttpRequestMessage request,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            // If there is a timeout, we turn it into a cancellation token. At the same time, we need to link to the caller's
            // cancellation token. To avoid leaking objects, we must then also dispose of the CancellationTokenSource. To keep
            // code flow simple, we treat it as re-entering the same method with a different CancellationToken and no timeout.
            if (timeout != InfiniteTimeout)
            {
                using var timeoutTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                timeoutTokenSource.CancelAfter(timeout);

                // We must await here because we need to dispose of the CTS only after the work has been completed.
                return await PrivateMakeRequestAsync(request, InfiniteTimeout, timeoutTokenSource.Token);
            }

            var httpClient = _factory.GetOrCreate();
            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);

            return response;
        }

        private static HttpRequestMessage PrepareRequest(
            HttpMethod method,
            Uri baseUri,
            string path,
            string queryString)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path));

            var url = $"{baseUri}v{ApiVersion}/{path}";

            if (!string.IsNullOrWhiteSpace(queryString))
                url = $"{url}?{queryString}";

            var request = new HttpRequestMessage(method, new Uri(url))
            {
                Version = new Version(1, 40)
            };

            request.Headers.Add("User-Agent", UserAgent);

            return request;
        }

        private static async Task HandleIfErrorResponseAsync(
            HttpStatusCode statusCode,
            HttpResponseMessage response,
            CancellationToken cancellation)
        {
            bool isErrorResponse = statusCode < HttpStatusCode.OK || statusCode >= HttpStatusCode.BadRequest;

            if (!isErrorResponse)
                return;

            var responseBody = await response.Content.ReadAsStringAsync(cancellation);

            throw new DockerApiException(statusCode, responseBody);
        }
    }
}