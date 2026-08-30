namespace 商业超体价值与定位.Models;

public class DiagnosticSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastModifiedAt { get; set; } = DateTime.Now;
    public string Name { get; set; } = $"商业诊断_{DateTime.Now:yyyyMMdd_HHmmss}";
    public List<Message> Messages { get; set; } = new();
    public BusinessCanvas Canvas { get; set; } = new();
    public DiagnosticStage CurrentStage { get; set; } = DiagnosticStage.NotStarted;
    public List<ExtractedTag> ExtractedTags { get; set; } = new();
    public CompetitiveAnalysis? CompetitiveAnalysis { get; set; }
}

public enum DiagnosticStage
{
    NotStarted,
    ExploringExclusiveResources,
    ProbingHiddenPainPoints,
    ConfirmingDeliveryBoundaries,
    BuildingMoat,
    GeneratingBlueprint,
    Complete
}

public class ExtractedTag
{
    public string Category { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public DateTime ExtractedAt { get; set; } = DateTime.Now;
}

public class CompetitiveAnalysis
{
    public List<string> CompetitorBlindSpots { get; set; } = new();
    public List<string> OurExclusiveAdvantages { get; set; } = new();
    public List<CompetitorComparison> Comparisons { get; set; } = new();
}

public class CompetitorComparison
{
    public string Dimension { get; set; } = string.Empty;
    public string CompetitorPosition { get; set; } = string.Empty;
    public string OurPosition { get; set; } = string.Empty;
}
