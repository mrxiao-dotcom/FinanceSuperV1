namespace 商业超体价值与定位.Models;

public class LlmConfig
{
    public string Provider { get; set; } = "DeepSeek";
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.deepseek.com/v1";
    public string Model { get; set; } = "deepseek-chat";
    public int MaxTokens { get; set; } = 2000;
    public double Temperature { get; set; } = 0.7;
}

public class LlmRequest
{
    [Newtonsoft.Json.JsonProperty("model")]
    public string Model { get; set; } = string.Empty;

    [Newtonsoft.Json.JsonProperty("messages")]
    public List<LlmMessage> Messages { get; set; } = new();

    [Newtonsoft.Json.JsonProperty("max_tokens")]
    public int MaxTokens { get; set; } = 2000;

    [Newtonsoft.Json.JsonProperty("temperature")]
    public double Temperature { get; set; } = 0.7;

    [Newtonsoft.Json.JsonProperty("stream")]
    public bool Stream { get; set; } = false;
}

public class LlmMessage
{
    [Newtonsoft.Json.JsonProperty("role")]
    public string Role { get; set; } = "user";

    [Newtonsoft.Json.JsonProperty("content")]
    public string Content { get; set; } = string.Empty;
}

public class LlmResponse
{
    [Newtonsoft.Json.JsonProperty("choices")]
    public List<LlmChoice> Choices { get; set; } = new();

    [Newtonsoft.Json.JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [Newtonsoft.Json.JsonProperty("created")]
    public long Created { get; set; }
}

public class LlmChoice
{
    [Newtonsoft.Json.JsonProperty("message")]
    public LlmMessage Message { get; set; } = new();

    [Newtonsoft.Json.JsonProperty("finish_reason")]
    public string FinishReason { get; set; } = string.Empty;
}
