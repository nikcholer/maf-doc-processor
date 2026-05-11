using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.ClientModel.Primitives;

namespace MafDocumentProcessor.Services;

public sealed class OpenAICompatibleModelChatClient(
    ILogger<OpenAICompatibleModelChatClient>? logger = null) : IModelChatClient
{
    private readonly ILogger<OpenAICompatibleModelChatClient> _logger =
        logger ?? NullLogger<OpenAICompatibleModelChatClient>.Instance;

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
            ChatCompletion completion = await client.CompleteChatAsync(
                request.Messages.Select(ConvertMessage),
                new ChatCompletionOptions
                {
                    MaxOutputTokenCount = request.MaxOutputTokens
                },
                timeout.Token);

            var content = string.Concat(completion.Content.Select(part => part.Text));
            var usage = MapUsage(request.Operation, settings, completion.Usage);
            _logger.LogInformation(
                "Completed model operation {Operation} with {Provider}/{ModelId}. ResponseChars={ResponseChars}, InputTokens={InputTokens}, OutputTokens={OutputTokens}, EstimatedCostUsd={EstimatedCostUsd}.",
                request.Operation,
                settings.Provider,
                settings.ModelId,
                content.Length,
                usage.InputTokens,
                usage.OutputTokens,
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
}
