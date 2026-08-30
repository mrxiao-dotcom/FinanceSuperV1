using CommunityToolkit.Mvvm.ComponentModel;

namespace 商业超体价值与定位.Models;

public partial class BusinessCanvas : ObservableObject
{
    [ObservableProperty]
    private MoatCard _moatCard = new();

    [ObservableProperty]
    private PainPointCard _painPointCard = new();

    [ObservableProperty]
    private EmotionalPremiumCard _emotionalPremiumCard = new();

    [ObservableProperty]
    private BlueprintCard _blueprintCard = new();

    [ObservableProperty]
    private double _completionPercentage;

    public bool IsComplete => CompletionPercentage >= 0.8;
}

public partial class MoatCard : ObservableObject
{
    [ObservableProperty]
    private string _title = "护城河";

    [ObservableProperty]
    private string _description = "独占资源与认知资产";

    [ObservableProperty]
    private string _content = "请开始与AI顾问对话，\n系统将自动提取您的商业护城河...";

    [ObservableProperty]
    private bool _isActivated;

    [ObservableProperty]
    private string _color = "#E94560";

    [ObservableProperty]
    private List<string> _exclusiveResources = new();

    [ObservableProperty]
    private List<string> _cognitiveAssets = new();
}

public partial class PainPointCard : ObservableObject
{
    [ObservableProperty]
    private string _title = "客户痛点";

    [ObservableProperty]
    private string _description = "隐性成本与致命风险";

    [ObservableProperty]
    private string _content = "请开始与AI顾问对话，\n系统将自动识别客户痛点...";

    [ObservableProperty]
    private bool _isActivated;

    [ObservableProperty]
    private string _color = "#FF9800";

    [ObservableProperty]
    private List<string> _hiddenCosts = new();

    [ObservableProperty]
    private List<string> _criticalRisks = new();
}

public partial class EmotionalPremiumCard : ObservableObject
{
    [ObservableProperty]
    private string _title = "情感溢价";

    [ObservableProperty]
    private string _description = "驱动定价的情感因素";

    [ObservableProperty]
    private string _content = "请开始与AI顾问对话，\n系统将自动发现情感驱动力...";

    [ObservableProperty]
    private bool _isActivated;

    [ObservableProperty]
    private string _color = "#9C27B0";

    [ObservableProperty]
    private List<string> _emotionalDrivers = new();

    [ObservableProperty]
    private string _premiumStrategy = string.Empty;
}

public partial class BlueprintCard : ObservableObject
{
    [ObservableProperty]
    private string _title = "商业蓝图";

    [ObservableProperty]
    private string _description = "终极定位与交付模式";

    [ObservableProperty]
    private string _content = "完成对话后，\n系统将生成终极商业蓝图...";

    [ObservableProperty]
    private bool _isActivated;

    [ObservableProperty]
    private string _color = "#2196F3";

    [ObservableProperty]
    private string _fullContent = "";

    [ObservableProperty]
    private SuperSignature? _superSignature;

    [ObservableProperty]
    private TrustBuildingSop? _trustBuildingSop;

    [ObservableProperty]
    private DeliveryMode? _deliveryMode;
}

public class SuperSignature
{
    public string Identity { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string TargetAudience { get; set; } = string.Empty;
    public string Problem { get; set; } = string.Empty;

    public override string ToString() => $"我是{Identity}，我使用{Method}，帮助{TargetAudience}解决{Problem}。";
}

public class TrustBuildingSop
{
    public List<string> CaseStudies { get; set; } = new();
    public List<string> Qualifications { get; set; } = new();
    public List<string> SocialProofs { get; set; } = new();
}

public class DeliveryMode
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsHighTouch { get; set; }
}
