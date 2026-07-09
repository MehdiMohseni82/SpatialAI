using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

namespace SpatialAI.Api.Blueprint;

/// <summary>
/// Vision calls via Claude (Anthropic): sends a system prompt + user text alongside one or more images
/// and returns the model's text reply. <see cref="BlueprintService"/> extracts the JSON object from it.
/// Uses the same Anthropic key as <see cref="ChatEngine"/>; the model is <c>LLM:VisionModel</c>
/// (falls back to <c>LLM:Model</c>). No separate/Azure vision resource needed.
/// </summary>
public sealed class VisionClient
{
    private readonly AnthropicClient? _client;
    private readonly string _model;

    public VisionClient(IConfiguration config)
    {
        var apiKey = config["LLM:ApiKey"] ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        _model = config["LLM:VisionModel"] ?? config["LLM:Model"] ?? "claude-haiku-4-5";
        if (!string.IsNullOrWhiteSpace(apiKey))
            _client = new AnthropicClient { ApiKey = apiKey };
    }

    public bool IsConfigured => _client is not null;

    public sealed record Image(byte[] Bytes, string Mime);

    /// <summary>
    /// Sends <paramref name="userText"/> plus <paramref name="images"/> to Claude and returns the reply
    /// text (the caller extracts the JSON object). Throws if the client is not configured.
    /// </summary>
    public async Task<string> CompleteJsonAsync(string systemPrompt, string userText,
        IReadOnlyList<Image> images, CancellationToken ct)
    {
        if (_client is null)
            throw new InvalidOperationException("Vision client is not configured (set LLM:ApiKey / ANTHROPIC_API_KEY).");

        var content = new List<ContentBlockParam> { new TextBlockParam { Text = userText } };
        foreach (var img in images)
            content.Add(ImageBlockParam.FromRawUnchecked(new Dictionary<string, JsonElement>
            {
                ["type"] = JsonSerializer.SerializeToElement("image"),
                ["source"] = JsonSerializer.SerializeToElement(new
                {
                    type = "base64",
                    media_type = img.Mime,
                    data = Convert.ToBase64String(img.Bytes),
                }),
            }));

        var resp = await _client.Messages.Create(new MessageCreateParams
        {
            Model = _model,
            MaxTokens = 8192,
            System = new List<TextBlockParam> { new() { Text = systemPrompt } },
            Messages = new List<MessageParam> { new() { Role = Role.User, Content = content } },
        }, cancellationToken: ct);

        var sb = new StringBuilder();
        foreach (var block in resp.Content)
            if (block.TryPickText(out TextBlock? t))
                sb.Append(t.Text);
        return sb.ToString();
    }
}
