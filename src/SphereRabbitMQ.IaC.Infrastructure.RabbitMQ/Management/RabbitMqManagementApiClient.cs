using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SphereRabbitMQ.IaC.Domain.Topology;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Configuration;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Management.Interfaces;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Management.Models;

namespace SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Management;

/// <summary>
/// HTTP client for the RabbitMQ Management API.
/// </summary>
public sealed class RabbitMqManagementApiClient : IRabbitMqManagementApiClient
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _serializerOptions;

    /// <summary>
    /// Creates a new management API client.
    /// </summary>
    public RabbitMqManagementApiClient(HttpClient httpClient, RabbitMqManagementOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        _httpClient = httpClient;
        _httpClient.BaseAddress = options.BaseUri;
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}")));

        _serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ManagementVirtualHostModel>> GetVirtualHostsAsync(CancellationToken cancellationToken = default)
        => SendGetAsync<ManagementVirtualHostModel>("vhosts", cancellationToken);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ManagementExchangeModel>> GetExchangesAsync(string virtualHostName, CancellationToken cancellationToken = default)
        => SendGetAsync<ManagementExchangeModel>($"exchanges/{Encode(virtualHostName)}", cancellationToken);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ManagementQueueModel>> GetQueuesAsync(string virtualHostName, CancellationToken cancellationToken = default)
        => SendGetAsync<ManagementQueueModel>($"queues/{Encode(virtualHostName)}", cancellationToken);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ManagementBindingModel>> GetBindingsAsync(string virtualHostName, CancellationToken cancellationToken = default)
        => SendGetAsync<ManagementBindingModel>($"bindings/{Encode(virtualHostName)}", cancellationToken);

    /// <inheritdoc />
    public ValueTask CreateVirtualHostAsync(string virtualHostName, CancellationToken cancellationToken = default)
        => SendPutAsync($"vhosts/{Encode(virtualHostName)}", new { }, cancellationToken);

    /// <inheritdoc />
    public ValueTask DeleteVirtualHostAsync(string virtualHostName, CancellationToken cancellationToken = default)
        => SendDeleteAsync($"vhosts/{Encode(virtualHostName)}", cancellationToken, allowNotFound: true);

    /// <inheritdoc />
    public ValueTask UpsertExchangeAsync(string virtualHostName, ExchangeDefinition exchange, CancellationToken cancellationToken = default)
        => SendPutAsync(
            $"exchanges/{Encode(virtualHostName)}/{Encode(exchange.Name)}",
            new
            {
                type = exchange.Type.ToString().ToLowerInvariant(),
                durable = exchange.Durable,
                auto_delete = exchange.AutoDelete,
                @internal = exchange.Internal,
                arguments = exchange.Arguments,
            },
            cancellationToken);

    /// <inheritdoc />
    public ValueTask DeleteExchangeAsync(string virtualHostName, string exchangeName, CancellationToken cancellationToken = default)
        => SendDeleteAsync($"exchanges/{Encode(virtualHostName)}/{Encode(exchangeName)}", cancellationToken, allowNotFound: true);

    /// <inheritdoc />
    public ValueTask UpsertQueueAsync(string virtualHostName, QueueDefinition queue, CancellationToken cancellationToken = default)
        => SendPutAsync(
            $"queues/{Encode(virtualHostName)}/{Encode(queue.Name)}",
            new
            {
                durable = queue.Durable,
                auto_delete = queue.AutoDelete,
                arguments = queue.Arguments,
                @params = new { },
            },
            cancellationToken);

    /// <inheritdoc />
    public ValueTask DeleteQueueAsync(string virtualHostName, string queueName, CancellationToken cancellationToken = default)
        => SendDeleteAsync($"queues/{Encode(virtualHostName)}/{Encode(queueName)}", cancellationToken, allowNotFound: true);

    /// <inheritdoc />
    public ValueTask CreateBindingAsync(string virtualHostName, BindingDefinition binding, CancellationToken cancellationToken = default)
        => SendPostAsync(
            BuildBindingEndpoint(virtualHostName, binding),
            new
            {
                routing_key = binding.RoutingKey,
                arguments = binding.Arguments,
            },
            cancellationToken);

    /// <inheritdoc />
    public async ValueTask DeleteBindingAsync(string virtualHostName, BindingDefinition binding, CancellationToken cancellationToken = default)
    {
        var bindings = await GetBindingsAsync(virtualHostName, cancellationToken);
        var candidates = bindings.Where(candidate =>
            string.Equals(candidate.Source, binding.SourceExchange, StringComparison.Ordinal) &&
            string.Equals(candidate.Destination, binding.Destination, StringComparison.Ordinal) &&
            string.Equals(candidate.DestinationType, ToBindingDestination(binding.DestinationType), StringComparison.Ordinal) &&
            string.Equals(candidate.RoutingKey, binding.RoutingKey, StringComparison.Ordinal) &&
            SerializeArguments(candidate.Arguments) == SerializeArguments(binding.Arguments));

        foreach (var candidate in candidates)
        {
            await SendDeleteAsync(
                $"{BuildBindingEndpoint(virtualHostName, binding)}/{Encode(candidate.PropertiesKey)}",
                cancellationToken,
                allowNotFound: true);
        }
    }

    /// <inheritdoc />
    public async ValueTask RebindAsync(string virtualHostName, BindingDefinition binding, CancellationToken cancellationToken = default)
    {
        var bindings = await GetBindingsAsync(virtualHostName, cancellationToken);
        var candidates = bindings.Where(candidate =>
            string.Equals(candidate.Source, binding.SourceExchange, StringComparison.Ordinal) &&
            string.Equals(candidate.Destination, binding.Destination, StringComparison.Ordinal) &&
            string.Equals(candidate.DestinationType, ToBindingDestination(binding.DestinationType), StringComparison.Ordinal) &&
            string.Equals(candidate.RoutingKey, binding.RoutingKey, StringComparison.Ordinal));

        foreach (var candidate in candidates)
        {
            await SendDeleteAsync(
                $"{BuildBindingEndpoint(virtualHostName, binding)}/{Encode(candidate.PropertiesKey)}",
                cancellationToken);
        }

        await CreateBindingAsync(virtualHostName, binding, cancellationToken);
    }

    private static string BuildBindingEndpoint(string virtualHostName, BindingDefinition binding)
        => $"bindings/{Encode(virtualHostName)}/e/{Encode(binding.SourceExchange)}/{ToBindingDestination(binding.DestinationType)}/{Encode(binding.Destination)}";

    private static string ToBindingDestination(BindingDestinationType destinationType)
        => destinationType == BindingDestinationType.Queue ? "q" : "e";

    private static string Encode(string value) => Uri.EscapeDataString(value);

    private static string SerializeArguments(IReadOnlyDictionary<string, object?> arguments)
        => JsonSerializer.Serialize(arguments);

    private async ValueTask<IReadOnlyList<T>> SendGetAsync<T>(string relativePath, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(relativePath, cancellationToken);
        await EnsureSuccessStatusCodeAsync(response, HttpMethod.Get, relativePath, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<List<T>>(_serializerOptions, cancellationToken);
        return payload is null ? Array.Empty<T>() : payload;
    }

    private async ValueTask SendPutAsync(string relativePath, object payload, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PutAsJsonAsync(relativePath, payload, _serializerOptions, cancellationToken);
        await EnsureSuccessStatusCodeAsync(response, HttpMethod.Put, relativePath, cancellationToken);
    }

    private async ValueTask SendPostAsync(string relativePath, object payload, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(relativePath, payload, _serializerOptions, cancellationToken);
        await EnsureSuccessStatusCodeAsync(response, HttpMethod.Post, relativePath, cancellationToken);
    }

    private async ValueTask SendDeleteAsync(string relativePath, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        using var response = await _httpClient.DeleteAsync(relativePath, cancellationToken);
        if (allowNotFound && response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return;
        }

        await EnsureSuccessStatusCodeAsync(response, HttpMethod.Delete, relativePath, cancellationToken);
    }

    private async ValueTask EnsureSuccessStatusCodeAsync(
        HttpResponseMessage response,
        HttpMethod method,
        string relativePath,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var responseBody = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken);
        var requestUri = new Uri(_httpClient.BaseAddress!, relativePath);
        var bodySuffix = string.IsNullOrWhiteSpace(responseBody)
            ? string.Empty
            : $" Response body: {responseBody}";

        throw new HttpRequestException(
            $"RabbitMQ Management API request failed. Method: {method}. Uri: {requestUri}. Status: {(int)response.StatusCode} ({response.ReasonPhrase}).{bodySuffix}");
    }
}
