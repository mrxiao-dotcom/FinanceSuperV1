using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using 商业超体价值与定位.Models;
using Serilog;

namespace 商业超体价值与定位.Services;

public interface IConversationService
{
    Task<ConversationResponse> GetResponseAsync(
        IReadOnlyList<Message> messages,
        DiagnosticStage currentStage);
}

public class ConversationResponse
{
    public string Content { get; set; } = string.Empty;
    public DiagnosticStage NewStage { get; set; }
}

public class ConversationService : IConversationService
{
    private readonly ILlmService _llmService;
    private readonly IDiagnosticEngine _diagnosticEngine;
    private readonly Dictionary<DiagnosticStage, string> _systemPrompts;

    public ConversationService(ILlmService llmService, IDiagnosticEngine diagnosticEngine)
    {
        _llmService = llmService;
        _diagnosticEngine = diagnosticEngine;
        _systemPrompts = LoadPrompts();
    }

    private Dictionary<DiagnosticStage, string> LoadPrompts()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "商业超体价值与定位.Resources.prompts.json";
            
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                var prompts = JsonConvert.DeserializeObject<PromptConfig>(json);
                return new Dictionary<DiagnosticStage, string>
                {
                    { DiagnosticStage.ExploringExclusiveResources, prompts?.Stage1Prompt ?? GetDefaultPrompt(1) },
                    { DiagnosticStage.ProbingHiddenPainPoints, prompts?.Stage2Prompt ?? GetDefaultPrompt(2) },
                    { DiagnosticStage.ConfirmingDeliveryBoundaries, prompts?.Stage3Prompt ?? GetDefaultPrompt(3) },
                    { DiagnosticStage.BuildingMoat, prompts?.Stage4Prompt ?? GetDefaultPrompt(4) },
                    { DiagnosticStage.GeneratingBlueprint, prompts?.Stage5Prompt ?? GetDefaultPrompt(5) }
                };
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load prompts from embedded resource, using defaults");
        }

        return GetDefaultPrompts();
    }

    private static Dictionary<DiagnosticStage, string> GetDefaultPrompts()
    {
        return new Dictionary<DiagnosticStage, string>
        {
            { DiagnosticStage.ExploringExclusiveResources, GetDefaultPrompt(1) },
            { DiagnosticStage.ProbingHiddenPainPoints, GetDefaultPrompt(2) },
            { DiagnosticStage.ConfirmingDeliveryBoundaries, GetDefaultPrompt(3) },
            { DiagnosticStage.BuildingMoat, GetDefaultPrompt(4) },
            { DiagnosticStage.GeneratingBlueprint, GetDefaultPrompt(5) }
        };
    }

    private static string GetDefaultPrompt(int stage)
    {
        return stage switch
        {
            1 => "You are a business strategy advisor. Help users discover their exclusive resources through probing questions. Focus on one question per response, keep it under 200 words.",
            2 => "You are a business strategy advisor. Help users discover hidden costs and risks their customers face. Create urgency, keep responses under 200 words.",
            3 => "You are a business strategy advisor. Confirm delivery boundaries including time, energy, and profit expectations. Be practical and direct.",
            4 => "You are a business strategy advisor. Generate a competitive moat strategy based on collected information. Be confident and strategic.",
            5 => "You are a business strategy advisor. Generate the ultimate business blueprint with one-sentence signature, trust-building SOP, and delivery mode suggestions. Be authoritative.",
            _ => "You are a helpful assistant."
        };
    }

    public async Task<ConversationResponse> GetResponseAsync(
        IReadOnlyList<Message> messages,
        DiagnosticStage currentStage)
    {
        var systemPrompt = _systemPrompts.GetValueOrDefault(currentStage, GetDefaultPrompt(1));

        var conversationHistory = messages
            .Select(m => new LlmMessage
            {
                Role = m.Role == MessageRole.User ? "user" : "assistant",
                Content = m.Content
            })
            .ToList();

        bool useLongContext = messages.Count > 10;
        var response = await _llmService.ChatAsync(systemPrompt, conversationHistory, useLongContext);
        var newStage = _diagnosticEngine.DetermineNextStage(messages, currentStage);

        return new ConversationResponse
        {
            Content = response,
            NewStage = newStage
        };
    }

    private class PromptConfig
    {
        public string? Stage1Prompt { get; set; }
        public string? Stage2Prompt { get; set; }
        public string? Stage3Prompt { get; set; }
        public string? Stage4Prompt { get; set; }
        public string? Stage5Prompt { get; set; }
    }
}
