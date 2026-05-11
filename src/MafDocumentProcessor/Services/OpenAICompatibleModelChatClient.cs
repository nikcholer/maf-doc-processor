using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace MafDocumentProcessor.Services;

public sealed class OpenAICompatibleModelChatClient : IModelChatClient
{
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

        ChatCompletion completion = await client.CompleteChatAsync(
            request.Messages.Select(ConvertMessage),
            new ChatCompletionOptions
            {
                MaxOutputTokenCount = request.MaxOutputTokens
            },
            timeout.Token);

        var content = string.Concat(completion.Content.Select(part => part.Text));
        return new ModelChatResponse(
            content,
            new ModelTokenUsage(
                request.Operation,
                settings.ModelId,
                InputTokens: null,
                OutputTokens: null,
                TotalTokens: null));
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
                Endpoint = new Uri(settings.Endpoint)
            });
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
