namespace WindowsDock;

internal static class ChatMediaRequest
{
    internal static bool Supports(AssistantProvider provider) =>
        provider is AssistantProvider.ChatGptPlan or AssistantProvider.ClaudeApi or AssistantProvider.Gemini;

    internal static object Content(AssistantProvider provider, string text, ChatMedia[]? files)
    {
        if (files is not { Length: > 0 })
            return provider == AssistantProvider.Gemini ? new[] { new { text } } : text;
        if (!Supports(provider))
            throw new ArgumentException("Choose ChatGPT plan, Claude API, or Gemini for images and PDFs.");

        var parts = new List<object>(files.Length + 1);
        parts.Add(provider switch
        {
            AssistantProvider.Gemini => new { text },
            AssistantProvider.ChatGptPlan => new { type = "input_text", text },
            _ => new { type = "text", text }
        });
        foreach (var file in files)
        {
            var pdf = file.MediaType == "application/pdf";
            parts.Add(provider switch
            {
                AssistantProvider.Gemini => new
                {
                    inlineData = new { mimeType = file.MediaType, data = Convert.ToBase64String(file.Bytes) }
                },
                AssistantProvider.ChatGptPlan => pdf
                    ? (object)new { type = "input_file", filename = file.Name, file_data = file.DataUrl }
                    : new { type = "input_image", image_url = file.DataUrl, detail = "auto" },
                _ => new
                {
                    type = pdf ? "document" : "image",
                    source = new { type = "base64", media_type = file.MediaType, data = Convert.ToBase64String(file.Bytes) }
                }
            });
        }
        return parts;
    }
}
