using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using CareHR.RfidGateway.Configuration;
using CareHR.RfidGateway.Models;
using CareHR.RfidGateway.Services;
using Microsoft.Extensions.Logging;

namespace CareHR.RfidGateway.Api;

public sealed class CareHrApiClient(
    HttpClient httpClient,
    GatewayOptionsAccessor options,
    GatewayState state,
    ILogger<CareHrApiClient> logger)
{
    private const string SystemHealthPath = "/api/system/health";

    public async Task<SystemHealthResult> TestApiAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Test API started");

        var cfg = options.Current;
        if (!Uri.TryCreate(cfg.ApiBaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri))
        {
            return new SystemHealthResult
            {
                Authentication = "Connection Failed",
                HttpStatusText = "Invalid ApiBaseUrl",
                ErrorDetail = $"Invalid ApiBaseUrl: {cfg.ApiBaseUrl}"
            };
        }

        var endpoint = new Uri(baseUri, SystemHealthPath.TrimStart('/'));
        logger.LogInformation("Test API Request URL={RequestUrl}", endpoint.AbsoluteUri);

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        if (!string.IsNullOrWhiteSpace(cfg.ApiToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ApiToken);
        }

        // Latency covers the full request: send + receive + body deserialize.
        var sw = Stopwatch.StartNew();
        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var statusCode = (int)response.StatusCode;

            SystemHealthPayload? payload = null;
            if (response.Content is not null && statusCode is 200 or 503)
            {
                try
                {
                    payload = await response.Content
                        .ReadFromJsonAsync<SystemHealthPayload>(cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Test API response body parse failed");
                }
            }

            sw.Stop();
            logger.LogInformation("Test API HTTP Status={StatusCode}", statusCode);
            logger.LogInformation("Test API Latency={LatencyMs}ms", sw.ElapsedMilliseconds);

            var result = new SystemHealthResult
            {
                Api = payload?.Api ?? "-",
                Database = payload?.Database ?? "-",
                Authentication = MapAuthentication(statusCode),
                HttpStatusCode = statusCode,
                HttpStatusText = $"{statusCode} {response.ReasonPhrase}".Trim(),
                LatencyMs = sw.ElapsedMilliseconds,
                ServerTime = payload?.ServerTime?.ToString("O") ?? "-",
                Version = string.IsNullOrWhiteSpace(payload?.Version) ? "-" : payload.Version,
                Environment = string.IsNullOrWhiteSpace(payload?.Environment) ? "-" : payload.Environment,
                Uptime = string.IsNullOrWhiteSpace(payload?.Uptime) ? "-" : payload.Uptime
            };

            logger.LogInformation("Test API Completed");
            return result;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            logger.LogWarning(ex, "Test API Connection Timeout Latency={LatencyMs}ms", sw.ElapsedMilliseconds);
            logger.LogInformation("Test API Completed");
            return new SystemHealthResult
            {
                Authentication = "Connection Timeout",
                HttpStatusText = "Timeout",
                LatencyMs = sw.ElapsedMilliseconds,
                ErrorDetail = ex.Message
            };
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            logger.LogWarning(ex, "Test API Connection Failed Latency={LatencyMs}ms", sw.ElapsedMilliseconds);
            logger.LogInformation("Test API Completed");
            return new SystemHealthResult
            {
                Authentication = "Connection Failed",
                HttpStatusText = "Connection Failed",
                LatencyMs = sw.ElapsedMilliseconds,
                ErrorDetail = ex.Message
            };
        }
    }

    private static string MapAuthentication(int statusCode) => statusCode switch
    {
        200 or 503 => "Passed",
        401 => "Failed",
        403 => "Forbidden",
        _ => $"HTTP {statusCode}"
    };

    /// <summary>Typed JSON body for GET /api/system/health (controller-local DTO).</summary>
    private sealed class SystemHealthPayload
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("database")]
        public string? Database { get; set; }

        [JsonPropertyName("api")]
        public string? Api { get; set; }

        [JsonPropertyName("serverTime")]
        public DateTimeOffset? ServerTime { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("environment")]
        public string? Environment { get; set; }

        [JsonPropertyName("uptime")]
        public string? Uptime { get; set; }
    }

    public async Task<bool> SendTagAsync(TagRead tag, CancellationToken cancellationToken)
    {
        var cfg = options.Current;
        var retryCount = Math.Max(0, cfg.ApiRetryCount);
        var backoff = Math.Max(100, cfg.ApiRetryBackoffMs);

        for (var attempt = 0; attempt <= retryCount; attempt++)
        {
            try
            {
                var ok = await SendOnceAsync(tag, cfg, cancellationToken).ConfigureAwait(false);
                if (ok)
                {
                    state.SetApi(ApiHealth.Online, DateTimeOffset.Now);
                    logger.LogInformation("API Success EPC={Epc}", tag.Epc);
                    return true;
                }
            }
            catch (Exception ex) when (attempt < retryCount)
            {
                logger.LogWarning(ex, "API Failed (attempt {Attempt}/{Total}) EPC={Epc}", attempt + 1, retryCount + 1, tag.Epc);
                await Task.Delay(backoff * (attempt + 1), cancellationToken).ConfigureAwait(false);
                continue;
            }
            catch (Exception ex)
            {
                state.SetApi(ApiHealth.Offline);
                state.SetLastError(ex.Message);
                logger.LogError(ex, "API Failed EPC={Epc}", tag.Epc);
                return false;
            }

            if (attempt < retryCount)
            {
                await Task.Delay(backoff * (attempt + 1), cancellationToken).ConfigureAwait(false);
            }
        }

        state.SetApi(ApiHealth.Offline);
        logger.LogWarning("API Failed EPC={Epc}", tag.Epc);
        return false;
    }

    private async Task<bool> SendOnceAsync(TagRead tag, GatewayOptions cfg, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(cfg.ApiBaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException($"Invalid ApiBaseUrl: {cfg.ApiBaseUrl}");
        }

        var path = string.IsNullOrWhiteSpace(cfg.ApiEventsPath) ? "/rfid/events" : cfg.ApiEventsPath;
        var endpoint = new Uri(baseUri, path.TrimStart('/'));

        _ = Guid.TryParse(cfg.DeviceId, out var deviceId);

        var payload = new
        {
            eventId = Guid.NewGuid().ToString("N"),
            epc = tag.Epc,
            readerCode = cfg.ReaderCode,
            deviceId,
            direction = cfg.Direction,
            rssi = tag.Rssi,
            observedAt = tag.Timestamp.ToUniversalTime()
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payload)
        };

        if (!string.IsNullOrWhiteSpace(cfg.ApiToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ApiToken);
        }

        if (!string.IsNullOrWhiteSpace(cfg.HospitalCode))
        {
            request.Headers.TryAddWithoutValidation("X-Hospital-Code", cfg.HospitalCode);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            logger.LogWarning(
                "API rejected tag. Status={StatusCode}, Body={Body}",
                (int)response.StatusCode,
                body);
            return false;
        }

        return true;
    }
}

/// <summary>
/// Result of GET /api/system/health — kept next to CareHrApiClient (single consumer: StatusForm).
/// </summary>
public sealed class SystemHealthResult
{
    public string Api { get; init; } = "-";
    public string Database { get; init; } = "-";
    public string Authentication { get; init; } = "-";
    public int? HttpStatusCode { get; init; }
    public string HttpStatusText { get; init; } = "-";
    public long LatencyMs { get; init; }
    public string ServerTime { get; init; } = "-";
    public string Version { get; init; } = "-";
    public string Environment { get; init; } = "-";
    public string Uptime { get; init; } = "-";
    public string? ErrorDetail { get; init; }
}
