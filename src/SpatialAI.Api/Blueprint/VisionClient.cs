using Azure;
using Azure.AI.OpenAI;
using OpenAI.Chat;

namespace SpatialAI.Api.Blueprint;

/// <summary>
/// Thin wrapper over the Azure OpenAI chat client for <b>vision</b> calls: sends a system prompt + user text
/// alongside one or more images and returns the model's JSON text. Uses the same Azure resource as
/// <see cref="ChatEngine"/>; the deployment is <c>OpenAI:VisionDeployment</c> (falls back to
/// <c>OpenAI:ChatDeployment</c>, which for this project is the vision-capable gpt-4.1).
/// </summary>
public sealed class VisionClient
{
    private readonly ChatClient? _chat;

    public VisionClient(IConfiguration config)
    {
        var endpoint = config["OpenAI:AzureEndpoint"];
        var apiKey = config["OpenAI:ApiKey"];
        var deployment = config["OpenAI:VisionDeployment"] ?? config["OpenAI:ChatDeployment"] ?? "gpt-4o";
        if (!string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(apiKey))
        {
            var client = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
            _chat = client.GetChatClient(deployment);
        }
    }

    public bool IsConfigured => _chat is not null;

    public sealed record Image(byte[] Bytes, string Mime);

    /// <summary>
    /// Sends <paramref name="userText"/> plus <paramref name="images"/> and returns the model's reply text,
    /// requested as a single JSON object. Throws if the client is not configured.
    /// </summary>
    public async Task<string> CompleteJsonAsync(string systemPrompt, string userText,
        IReadOnlyList<Image> images, CancellationToken ct)
    {
        if (_chat is null) throw new InvalidOperationException("Vision client is not configured (set OpenAI:AzureEndpoint / OpenAI:ApiKey).");

        var parts = new List<ChatMessageContentPart> { ChatMessageContentPart.CreateTextPart(userText) };
        foreach (var img in images)
            parts.Add(ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(img.Bytes), img.Mime, ChatImageDetailLevel.High));

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(parts)
        };
        var options = new ChatCompletionOptions { ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat() };

        var completion = (await _chat.CompleteChatAsync(messages, options, ct)).Value;
        return string.Join("\n", completion.Content.Select(p => p.Text));
    }
}
