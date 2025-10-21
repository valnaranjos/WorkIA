using KommoAIAgent.Application.Connectors;
using System.Net.Http.Headers;
using System.Text.Json;

namespace KommoAIAgent.Infrastructure.Connectors;

/// <summary>
/// Conector genérico que invoca microservicios vía HTTP/REST
/// </summary>
public sealed class HttpWebhookConnector : IExternalConnector
{
    private readonly HttpClient _httpClient;
    private readonly string _endpointUrl;
    private readonly string _authType;
    private readonly JsonDocument _authConfig;
    private readonly int _timeoutMs;
    private readonly int _retryCount;
    private readonly ILogger<HttpWebhookConnector> _logger;
    private readonly string _tenantSlug;

    public string ConnectorType { get; }
    public string DisplayName { get; }
    public IReadOnlyList<string> Capabilities { get; }

    public HttpWebhookConnector(
        HttpClient httpClient,
        string connectorType,
        string displayName,
        string endpointUrl,
        string authType,
        JsonDocument authConfig,
        IReadOnlyList<string> capabilities,
        int timeoutMs,
        int retryCount,
        ILogger<HttpWebhookConnector> logger,
        string tenantSlug)
    {
        _httpClient = httpClient;
        ConnectorType = connectorType;
        DisplayName = displayName;
        _endpointUrl = endpointUrl;
        _authType = authType;
        _authConfig = authConfig;
        Capabilities = capabilities;
        _timeoutMs = timeoutMs;
        _retryCount = retryCount;
        _logger = logger;
        _tenantSlug = tenantSlug;
    }

    public async Task<ConnectorResponse> InvokeAsync(
     string capability,
     Dictionary<string, object> parameters,
     CancellationToken ct = default)
    {
        if (!Capabilities.Contains(capability))
        {
            return new ConnectorResponse
            {
                Success = false,
                Message = $"Capability '{capability}' no soportada por {DisplayName}",
                ErrorCode = "UNSUPPORTED_CAPABILITY"
            };
        }

        var payload = new ConnectorRequest
        {
            Capability = capability,
            Parameters = parameters
        };

        var attempt = 0;
        Exception? lastException = null;

        while (attempt < _retryCount)
        {
            attempt++;
            var startTime = DateTime.UtcNow;

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromMilliseconds(_timeoutMs));

                var request = new HttpRequestMessage(HttpMethod.Post, _endpointUrl)
                {
                    Content = JsonContent.Create(payload),
                    Version = System.Net.HttpVersion.Version11
                };

                // Aplicar autenticación
                ApplyAuth(request);

                // Agregar header de tenant
                request.Headers.Add("X-Tenant-Slug", _tenantSlug);

                _logger.LogInformation(
                    "Invoking connector {Type} ({Name}) → {Capability} (attempt {Attempt}/{Max})",
                    ConnectorType, DisplayName, capability, attempt, _retryCount
                );

                var response = await _httpClient.SendAsync(request, cts.Token);
                var duration = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;
                var responseBody = await response.Content.ReadAsStringAsync(cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Connector {Type} failed: {Status} {Body} (duration: {Duration}ms)",
                        ConnectorType, response.StatusCode, responseBody, duration
                    );

                    if (attempt < _retryCount && ShouldRetry(response.StatusCode))
                    {
                        await Task.Delay(1000 * attempt, ct);
                        continue;
                    }

                    return new ConnectorResponse
                    {
                        Success = false,
                        Message = $"Error al comunicarse con {DisplayName}",
                        ErrorDetails = $"HTTP {(int)response.StatusCode}: {responseBody}",
                        ErrorCode = response.StatusCode.ToString(),
                        Metadata = new()
                        {
                            ["statusCode"] = (int)response.StatusCode,
                            ["durationMs"] = duration,
                            ["attempt"] = attempt
                        }
                    };
                }

                // 🔧 Parsear respuesta con opciones case-insensitive (soporta camelCase y PascalCase)
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                var result = JsonSerializer.Deserialize<ConnectorResponse>(responseBody, options);
                if (result is null)
                {
                    return new ConnectorResponse
                    {
                        Success = true,
                        Message = "OK",
                        Data = responseBody,
                        Metadata = new()
                        {
                            ["statusCode"] = (int)response.StatusCode,
                            ["durationMs"] = duration
                        }
                    };
                }

                // Agregar metadata de la invocación
                result = result with
                {
                    Metadata = new Dictionary<string, object>(result.Metadata ?? new())
                    {
                        ["statusCode"] = (int)response.StatusCode,
                        ["durationMs"] = duration,
                        ["attempt"] = attempt
                    }
                };

                _logger.LogInformation(
                    "Connector {Type} success (duration: {Duration}ms)",
                    ConnectorType, duration
                );

                return result;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogWarning("Connector {Type} cancelled by request", ConnectorType);
                throw;
            }
            catch (OperationCanceledException)
            {
                lastException = new TimeoutException($"Timeout after {_timeoutMs}ms");
                _logger.LogWarning(
                    "Connector {Type} timeout (attempt {Attempt}/{Max})",
                    ConnectorType, attempt, _retryCount
                );

                if (attempt < _retryCount)
                {
                    await Task.Delay(1000 * attempt, ct);
                    continue;
                }
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                _logger.LogWarning(
                    ex,
                    "Connector {Type} HTTP error (attempt {Attempt}/{Max})",
                    ConnectorType, attempt, _retryCount
                );

                if (attempt < _retryCount)
                {
                    await Task.Delay(1000 * attempt, ct);
                    continue;
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogError(
                    ex,
                    "Connector {Type} unexpected error",
                    ConnectorType
                );
                break;
            }
        }

        // Todos los reintentos fallaron
        return new ConnectorResponse
        {
            Success = false,
            Message = $"No se pudo comunicar con {DisplayName} tras {attempt} intentos",
            ErrorDetails = lastException?.Message,
            ErrorCode = "MAX_RETRIES_EXCEEDED",
            Metadata = new()
            {
                ["attempts"] = attempt,
                ["lastError"] = lastException?.GetType().Name ?? "Unknown"
            }
        };
    }

    public async Task<bool> HealthCheckAsync(CancellationToken ct = default)
    {
        try
        {
            var healthUrl = $"{_endpointUrl.TrimEnd('/')}/health";
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);

            var request = new HttpRequestMessage(HttpMethod.Get, healthUrl);
            ApplyAuth(request);

            var response = await _httpClient.SendAsync(request, linkedCts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private void ApplyAuth(HttpRequestMessage request)
    {
        switch (_authType.ToLowerInvariant())
        {
            case "bearer":
                if (_authConfig.RootElement.TryGetProperty("token", out var token))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue(
                        "Bearer",
                        token.GetString()
                    );
                }
                break;

            case "apikey":
                if (_authConfig.RootElement.TryGetProperty("header_name", out var headerName) &&
                    _authConfig.RootElement.TryGetProperty("api_key", out var apiKey))
                {
                    request.Headers.Add(
                        headerName.GetString()!,
                        apiKey.GetString()!
                    );
                }
                break;

            case "basic":
                if (_authConfig.RootElement.TryGetProperty("username", out var user) &&
                    _authConfig.RootElement.TryGetProperty("password", out var pass))
                {
                    var credentials = Convert.ToBase64String(
                        System.Text.Encoding.UTF8.GetBytes($"{user.GetString()}:{pass.GetString()}")
                    );
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                }
                break;

            case "none":
            default:
                // Sin autenticación
                break;
        }
    }

    private static bool ShouldRetry(System.Net.HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            System.Net.HttpStatusCode.RequestTimeout => true,
            System.Net.HttpStatusCode.TooManyRequests => true,
            System.Net.HttpStatusCode.InternalServerError => true,
            System.Net.HttpStatusCode.BadGateway => true,
            System.Net.HttpStatusCode.ServiceUnavailable => true,
            System.Net.HttpStatusCode.GatewayTimeout => true,
            _ => false
        };
    }
}