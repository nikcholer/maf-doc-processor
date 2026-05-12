using System.Diagnostics;
using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;

namespace MafDocumentProcessor.Services;

public sealed class OpenAICompatibleModelChatClient(
    ILogger<OpenAICompatibleModelChatClient>? logger = null) : IModelChatClient
{
    private readonly ILogger<OpenAICompatibleModelChatClient> _logger =
        logger ?? NullLogger<OpenAICompatibleModelChatClient>.Instance;
    private static readonly JsonSerializerOptions ProtocolJsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> SupportedProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        AiModelSettingsDefaults.TogetherAiProvider,
        "OpenAICompatible",
        "OpenAI"
    };

    public async ValueTask<ModelChatResponse> CompleteAsync(
        ModelChatRequest request,
        CancellationToken cancellationToken)
    {
        var settings = request.Settings;
        Validate(settings);

        var client = CreateClient(settings);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));

        _logger.LogInformation(
            "Starting model operation {Operation} with {Provider}/{ModelId}. TimeoutSeconds={TimeoutSeconds}.",
            request.Operation,
            settings.Provider,
            settings.ModelId,
            settings.RequestTimeoutSeconds);

        try
        {
            var elapsed = Stopwatch.StartNew();
            var response = await CompleteWithRetriesAsync(client, request, timeout.Token);
            elapsed.Stop();
            var content = response.Content ?? string.Empty;
            var usage = response.Usage with
            {
                DurationMilliseconds = elapsed.ElapsedMilliseconds
            };

            _logger.LogInformation(
                "Completed model operation {Operation} with {Provider}/{ModelId}. ResponseChars={ResponseChars}, InputTokens={InputTokens}, OutputTokens={OutputTokens}, DurationMilliseconds={DurationMilliseconds}, EstimatedCostUsd={EstimatedCostUsd}.",
                request.Operation,
                settings.Provider,
                settings.ModelId,
                content.Length,
                usage.InputTokens,
                usage.OutputTokens,
                usage.DurationMilliseconds,
                usage.EstimatedTotalCostUsd);

            return new ModelChatResponse(
                content,
                usage);
        }
        catch (AggregateException ex) when (IsTimeoutException(ex) && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "Timed out model operation {Operation} with {Provider}/{ModelId} after {TimeoutSeconds} seconds.",
                request.Operation,
                settings.Provider,
                settings.ModelId,
                settings.RequestTimeoutSeconds);

            throw new TimeoutException(
                $"Model operation '{request.Operation}' exceeded {settings.RequestTimeoutSeconds} seconds.",
                ex);
        }
        catch (OperationCanceledException ex) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "Timed out model operation {Operation} with {Provider}/{ModelId} after {TimeoutSeconds} seconds.",
                request.Operation,
                settings.Provider,
                settings.ModelId,
                settings.RequestTimeoutSeconds);

            throw new TimeoutException(
                $"Model operation '{request.Operation}' exceeded {settings.RequestTimeoutSeconds} seconds.",
                ex);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "Model operation {Operation} failed with {Provider}/{ModelId}.",
                request.Operation,
                settings.Provider,
                settings.ModelId);

            throw new ModelProviderException(
                $"Model operation '{request.Operation}' failed while calling {settings.Provider}.",
                ex);
        }
    }

    private static async Task<ModelChatResponse> CompleteWithTypedClientAsync(
        ChatClient client,
        ModelChatRequest request,
        CancellationToken cancellationToken)
    {
        ChatCompletion completion = await client.CompleteChatAsync(
            request.Messages.Select(ConvertMessage),
            new ChatCompletionOptions
            {
                MaxOutputTokenCount = request.MaxOutputTokens
            },
            cancellationToken);

        return new ModelChatResponse(
            string.Concat(completion.Content.Select(part => part.Text)),
            MapUsage(request.Operation, request.Settings, completion.Usage));
    }

    private async Task<ModelChatResponse> CompleteWithProtocolAsync(
        ChatClient client,
        ModelChatRequest request,
        CancellationToken cancellationToken)
    {
        var protocolRequest = CreateProtocolRequest(request);
        using var content = BinaryContent.Create(BinaryData.FromObjectAsJson(
            protocolRequest,
            ProtocolJsonOptions));
        var options = new RequestOptions
        {
            CancellationToken = cancellationToken
        };

        ClientResult result = await client.CompleteChatAsync(content, options);
        var rawResponse = result.GetRawResponse().Content.ToString();
        using var document = JsonDocument.Parse(rawResponse);
        var responseContent = ExtractProtocolContent(document.RootElement);

        if (string.IsNullOrWhiteSpace(responseContent))
        {
            _logger.LogWarning(
                "Model operation {Operation} with {Provider}/{ModelId} returned empty content. RawResponsePreview={RawResponsePreview}",
                request.Operation,
                request.Settings.Provider,
                request.Settings.ModelId,
                CreatePreview(rawResponse));
        }

        return new ModelChatResponse(
            responseContent,
            MapUsage(request.Operation, request.Settings, document.RootElement));
    }

    private static Dictionary<string, object?> CreateProtocolRequest(ModelChatRequest request)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = request.Settings.ModelId,
            ["messages"] = request.Messages.Select(message => ConvertMessageForProtocol(message, request.Operation)).ToArray(),
            ["max_tokens"] = request.MaxOutputTokens
        };

        if (ShouldDisableThinking(request.Settings))
        {
            body["temperature"] = 0;
            body["chat_template_kwargs"] = new Dictionary<string, object?>
            {
                ["enable_thinking"] = false
            };
        }

        return body;
    }

    private static Dictionary<string, object?> ConvertMessageForProtocol(
        ModelChatMessage message,
        string operation)
    {
        return new Dictionary<string, object?>
        {
            ["role"] = message.Role switch
            {
                ModelChatRole.System => "system",
                ModelChatRole.User => "user",
                _ => throw new ModelConfigurationException($"Unsupported chat role '{message.Role}'.")
            },
            ["content"] = ConvertMessageContentForProtocol(message.Content, operation)
        };
    }

    private static object ConvertMessageContentForProtocol(
        IReadOnlyList<ModelChatContent> content,
        string operation)
    {
        return content.All(part => part is ModelTextContent)
            ? string.Concat(content.OfType<ModelTextContent>().Select(part => part.Text))
            : content.Select(part => ConvertContentPartForProtocol(part, operation)).ToArray();
    }

    private static Dictionary<string, object?> ConvertContentPartForProtocol(
        ModelChatContent part,
        string operation)
    {
        return part switch
        {
            ModelTextContent text => new Dictionary<string, object?>
            {
                ["type"] = "text",
                ["text"] = text.Text
            },
            ModelImageContent image => new Dictionary<string, object?>
            {
                ["type"] = "image_url",
                ["image_url"] = new Dictionary<string, object?>
                {
                    ["url"] = $"data:{image.ContentType};base64,{Convert.ToBase64String(image.Content)}",
                    ["detail"] = string.Equals(operation, "classification", StringComparison.OrdinalIgnoreCase)
                        ? "low"
                        : "high"
                }
            },
            _ => throw new ModelConfigurationException(
                $"Unsupported chat content part '{part.GetType().Name}'.")
        };
    }

    private static ChatClient CreateClient(ModelRoleSettings settings)
    {
        var apiKey = ApiKeyEnvironment.GetApiKey(settings.ApiKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ModelConfigurationException(
                $"Environment variable '{settings.ApiKeyEnvironmentVariable}' is required for model role '{settings.ServiceId}'.");
        }

        return new ChatClient(
            settings.ModelId,
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(settings.Endpoint),
                NetworkTimeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds),
                RetryPolicy = new ClientRetryPolicy(maxRetries: 0)
            });
    }

    private static bool ShouldUseProtocolRequest(ModelRoleSettings settings)
    {
        return string.Equals(settings.Provider, AiModelSettingsDefaults.TogetherAiProvider, StringComparison.OrdinalIgnoreCase)
            && settings.ModelId.Contains("Qwen", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldDisableThinking(ModelRoleSettings settings)
    {
        return string.Equals(settings.Provider, AiModelSettingsDefaults.TogetherAiProvider, StringComparison.OrdinalIgnoreCase)
            && settings.ModelId.Contains("Qwen", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTimeoutException(Exception ex)
    {
        return ex switch
        {
            TimeoutException => true,
            TaskCanceledException => true,
            OperationCanceledException => true,
            AggregateException aggregate => aggregate.Flatten().InnerExceptions.Any(IsTimeoutException),
            _ when ex.InnerException is not null => IsTimeoutException(ex.InnerException),
            _ => false
        };
    }

    private static void Validate(ModelRoleSettings settings)
    {
        if (!SupportedProviders.Contains(settings.Provider))
        {
            throw new ModelConfigurationException(
                $"Provider '{settings.Provider}' is not supported by {nameof(OpenAICompatibleModelChatClient)}.");
        }

        if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out _))
        {
            throw new ModelConfigurationException(
                $"Endpoint '{settings.Endpoint}' is not a valid absolute URI for model role '{settings.ServiceId}'.");
        }

        if (string.IsNullOrWhiteSpace(settings.ModelId))
        {
            throw new ModelConfigurationException(
                $"ModelId is required for model role '{settings.ServiceId}'.");
        }

        if (settings.RequestTimeoutSeconds <= 0)
        {
            throw new ModelConfigurationException(
                $"RequestTimeoutSeconds must be greater than zero for model role '{settings.ServiceId}'.");
        }

        if (settings.MaxRetryAttempts < 0)
        {
            throw new ModelConfigurationException(
                $"MaxRetryAttempts must not be negative for model role '{settings.ServiceId}'.");
        }

        if (settings.RetryBaseDelayMilliseconds < 0)
        {
            throw new ModelConfigurationException(
                $"RetryBaseDelayMilliseconds must not be negative for model role '{settings.ServiceId}'.");
        }

        if (settings.InputTokenPricePerMillionUsd < 0)
        {
            throw new ModelConfigurationException(
                $"InputTokenPricePerMillionUsd must not be negative for model role '{settings.ServiceId}'.");
        }

        if (settings.OutputTokenPricePerMillionUsd < 0)
        {
            throw new ModelConfigurationException(
                $"OutputTokenPricePerMillionUsd must not be negative for model role '{settings.ServiceId}'.");
        }
    }

    private async Task<ModelChatResponse> CompleteWithRetriesAsync(
        ChatClient client,
        ModelChatRequest request,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return ShouldUseProtocolRequest(request.Settings)
                    ? await CompleteWithProtocolAsync(client, request, cancellationToken)
                    : await CompleteWithTypedClientAsync(client, request, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested
                && attempt <= request.Settings.MaxRetryAttempts
                && IsTransientModelFailure(ex))
            {
                var delay = GetRetryDelay(request.Settings, attempt);
                _logger.LogWarning(
                    ex,
                    "Transient model operation failure for {Operation} with {Provider}/{ModelId}. Attempt {Attempt} of {MaxAttempts}; retrying after {RetryDelayMilliseconds} ms.",
                    request.Operation,
                    request.Settings.Provider,
                    request.Settings.ModelId,
                    attempt,
                    request.Settings.MaxRetryAttempts + 1,
                    delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private static ModelTokenUsage MapUsage(
        string operation,
        ModelRoleSettings settings,
        ChatTokenUsage? usage)
    {
        var inputTokens = usage?.InputTokenCount;
        var outputTokens = usage?.OutputTokenCount;
        var estimatedInputCost = EstimateCost(inputTokens, settings.InputTokenPricePerMillionUsd);
        var estimatedOutputCost = EstimateCost(outputTokens, settings.OutputTokenPricePerMillionUsd);

        return new ModelTokenUsage(
            operation,
            settings.ModelId,
            inputTokens,
            outputTokens,
            usage?.TotalTokenCount,
            settings.InputTokenPricePerMillionUsd,
            settings.OutputTokenPricePerMillionUsd,
            estimatedInputCost,
            estimatedOutputCost,
            SumCosts(estimatedInputCost, estimatedOutputCost));
    }

    private static ModelTokenUsage MapUsage(
        string operation,
        ModelRoleSettings settings,
        JsonElement root)
    {
        var usage = root.TryGetProperty("usage", out var usageElement)
            ? usageElement
            : default;
        var inputTokens = GetOptionalInt32(usage, "prompt_tokens");
        var outputTokens = GetOptionalInt32(usage, "completion_tokens");
        var totalTokens = GetOptionalInt32(usage, "total_tokens");
        var estimatedInputCost = EstimateCost(inputTokens, settings.InputTokenPricePerMillionUsd);
        var estimatedOutputCost = EstimateCost(outputTokens, settings.OutputTokenPricePerMillionUsd);

        return new ModelTokenUsage(
            operation,
            settings.ModelId,
            inputTokens,
            outputTokens,
            totalTokens,
            settings.InputTokenPricePerMillionUsd,
            settings.OutputTokenPricePerMillionUsd,
            estimatedInputCost,
            estimatedOutputCost,
            SumCosts(estimatedInputCost, estimatedOutputCost));
    }

    private static int? GetOptionalInt32(JsonElement root, string propertyName)
    {
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static TimeSpan GetRetryDelay(ModelRoleSettings settings, int failedAttempt)
    {
        return TimeSpan.FromMilliseconds(settings.RetryBaseDelayMilliseconds * ((2 * failedAttempt) - 1));
    }

    private static bool IsTransientModelFailure(Exception ex)
    {
        return ex switch
        {
            ClientResultException clientResultException => IsTransientStatusCode(clientResultException.Status),
            HttpRequestException => true,
            IOException => true,
            AggregateException aggregate => aggregate.Flatten().InnerExceptions.Any(IsTransientModelFailure),
            _ when ex.InnerException is not null => IsTransientModelFailure(ex.InnerException),
            _ => false
        };
    }

    private static bool IsTransientStatusCode(int statusCode)
    {
        return statusCode is 0 or 408 or 429 or 500 or 502 or 503 or 504;
    }

    private static decimal? EstimateCost(int? tokens, decimal? pricePerMillionUsd)
    {
        if (!tokens.HasValue || !pricePerMillionUsd.HasValue)
        {
            return null;
        }

        return decimal.Round(tokens.Value * pricePerMillionUsd.Value / 1_000_000m, 8);
    }

    private static decimal? SumCosts(params decimal?[] costs)
    {
        var knownCosts = costs.Where(cost => cost.HasValue).Select(cost => cost!.Value).ToArray();
        return knownCosts.Length == 0 ? null : knownCosts.Sum();
    }

    private static ChatMessage ConvertMessage(ModelChatMessage message)
    {
        return message.Role switch
        {
            ModelChatRole.System => new SystemChatMessage(ToContentParts(message)),
            ModelChatRole.User => new UserChatMessage(ToContentParts(message)),
            _ => throw new ModelConfigurationException($"Unsupported chat role '{message.Role}'.")
        };
    }

    private static IEnumerable<ChatMessageContentPart> ToContentParts(ModelChatMessage message)
    {
        return message.Content.Select(part => part switch
        {
            ModelTextContent text => ChatMessageContentPart.CreateTextPart(text.Text),
            ModelImageContent image => ChatMessageContentPart.CreateImagePart(
                BinaryData.FromBytes(image.Content),
                image.ContentType,
                ChatImageDetailLevel.High),
            _ => throw new ModelConfigurationException(
                $"Unsupported chat content part '{part.GetType().Name}'.")
        });
    }

    private static string ExtractProtocolContent(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            return string.Empty;
        }

        var firstChoice = choices[0];
        if (!firstChoice.TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var content))
        {
            return string.Empty;
        }

        return content.ValueKind switch
        {
            JsonValueKind.String => content.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Concat(content.EnumerateArray().Select(ExtractProtocolContentPart)),
            _ => string.Empty
        };
    }

    private static string ExtractProtocolContentPart(JsonElement part)
    {
        if (part.ValueKind == JsonValueKind.String)
        {
            return part.GetString() ?? string.Empty;
        }

        return part.ValueKind == JsonValueKind.Object
            && part.TryGetProperty("text", out var text)
            && text.ValueKind == JsonValueKind.String
            ? text.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string CreatePreview(string value)
    {
        var preview = value.ReplaceLineEndings(" ").Trim();
        if (preview.Length == 0)
        {
            return "(empty response)";
        }

        return preview.Length > 500 ? $"{preview[..500]}..." : preview;
    }
}
