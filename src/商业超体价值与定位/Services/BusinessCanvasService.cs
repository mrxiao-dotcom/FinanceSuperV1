using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Serilog;
using 商业超体价值与定位.Models;
using System.Text.RegularExpressions;
using MessageRole = 商业超体价值与定位.Models.MessageRole;

namespace 商业超体价值与定位.Services;

public interface IBusinessCanvasService
{
    Task UpdateCanvasFromSessionAsync(DiagnosticSession session);
    Task<CompetitiveAnalysis> GenerateCompetitiveAnalysisAsync(DiagnosticSession session);
    Task<BlueprintCard> ExtractBlueprintFromConversationAsync(DiagnosticSession session);
}

public class BusinessCanvasService : IBusinessCanvasService
{
    private readonly ILlmService _llmService;
    private readonly JsonSerializerSettings _jsonSettings;

    public BusinessCanvasService(ILlmService llmService)
    {
        _llmService = llmService;
        _jsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new SnakeCaseNamingStrategy()
            }
        };
    }

    public Task UpdateCanvasFromSessionAsync(DiagnosticSession session)
    {
        var canvas = session.Canvas;

        var completedCards = new[] {
            canvas.MoatCard.IsActivated,
            canvas.PainPointCard.IsActivated,
            canvas.EmotionalPremiumCard.IsActivated,
            canvas.BlueprintCard.IsActivated || canvas.BlueprintCard.DeliveryMode != null
        }.Count(b => b);

        canvas.CompletionPercentage = completedCards / 4.0;

        Log.Information("商业画布已更新，完成度: {Percentage:P0}, 各卡片状态: 护城河={Moat}, 痛点={Pain}, 情感={Emotional}, 蓝图={Blueprint}",
            canvas.CompletionPercentage,
            canvas.MoatCard.IsActivated,
            canvas.PainPointCard.IsActivated,
            canvas.EmotionalPremiumCard.IsActivated,
            canvas.BlueprintCard.IsActivated || canvas.BlueprintCard.DeliveryMode != null);

        return Task.CompletedTask;
    }

    public async Task<CompetitiveAnalysis> GenerateCompetitiveAnalysisAsync(DiagnosticSession session)
    {
        Log.Information("开始生成竞品分析");

        var prompt = @"基于以下商业诊断信息，生成竞品分析：

1. 独占资源：{{exclusive_resources}}
2. 客户痛点：{{pain_points}}
3. 情感驱动：{{emotional_drivers}}

请生成JSON格式的竞品分析，返回结构如下：
{
    ""competitor_blind_spots"": [""盲区1"", ""盲区2""],
    ""our_exclusive_advantages"": [""优势1"", ""优势2""]
}

请以JSON格式返回，仅包含JSON，不要其他文字。";

        var exclusiveResources = string.Join("、", session.Canvas.MoatCard.ExclusiveResources);
        var painPoints = string.Join("、", session.Canvas.PainPointCard.HiddenCosts);
        var emotionalDrivers = string.Join("、", session.Canvas.EmotionalPremiumCard.EmotionalDrivers);

        var context = prompt
            .Replace("{{exclusive_resources}}", exclusiveResources)
            .Replace("{{pain_points}}", painPoints)
            .Replace("{{emotional_drivers}}", emotionalDrivers);

        try
        {
            var response = await _llmService.ChatAsync(
                "你是一个专业的商业竞争分析师。",
                new List<LlmMessage> { new() { Role = "user", Content = context } });

            Log.Information("[竞品分析] AI返回: {Response}", response);

            var analysis = TryParseCompetitiveAnalysis(response);
            return analysis ?? GenerateDefaultAnalysis();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "竞品分析生成失败，使用默认分析");
            return GenerateDefaultAnalysis();
        }
    }

    private CompetitiveAnalysis? TryParseCompetitiveAnalysis(string response)
    {
        var json = ExtractJson(response);
        if (string.IsNullOrEmpty(json)) return null;

        try
        {
            var result = JsonConvert.DeserializeObject<CompetitiveAnalysis>(json, _jsonSettings);
            return result;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "JSON解析失败，尝试部分解析");
            return TryPartialParse(json);
        }
    }

    private string? ExtractJson(string text)
    {
        var match = Regex.Match(text, @"\{[\s\S]*\}", RegexOptions.Singleline);
        return match.Success ? match.Value : null;
    }

    private CompetitiveAnalysis? TryPartialParse(string json)
    {
        try
        {
            var result = new CompetitiveAnalysis();

            var blindSpotsMatch = Regex.Match(json, @"""competitor_blind_spots""\s*:\s*\[([^\]]+)\]");
            if (blindSpotsMatch.Success)
            {
                var items = Regex.Matches(blindSpotsMatch.Groups[1].Value, @"""([^""]+)""");
                result.CompetitorBlindSpots = items.Select(m => m.Groups[1].Value).ToList();
            }

            var advantagesMatch = Regex.Match(json, @"""our_exclusive_advantages""\s*:\s*\[([^\]]+)\]");
            if (advantagesMatch.Success)
            {
                var items = Regex.Matches(advantagesMatch.Groups[1].Value, @"""([^""]+)""");
                result.OurExclusiveAdvantages = items.Select(m => m.Groups[1].Value).ToList();
            }

            return result.CompetitorBlindSpots.Count > 0 || result.OurExclusiveAdvantages.Count > 0
                ? result : null;
        }
        catch
        {
            return null;
        }
    }

    private static CompetitiveAnalysis GenerateDefaultAnalysis()
    {
        return new CompetitiveAnalysis
        {
            CompetitorBlindSpots = new List<string>
            {
                "竞争对手缺乏系统化的价值显影方法论",
                "竞争对手未能建立数据护城河"
            },
            OurExclusiveAdvantages = new List<string>
            {
                "五层逼问模板形成的独特咨询流程",
                "基于真实问答数据积累的持续优化能力"
            }
        };
    }

    public async Task<BlueprintCard> ExtractBlueprintFromConversationAsync(DiagnosticSession session)
    {
        Log.Information("从对话历史中提取商业蓝图");

        var messages = session.Messages;
        if (messages.Count == 0)
        {
            Log.Warning("对话历史为空，无法提取商业蓝图");
            return GenerateDefaultBlueprint(session.Canvas);
        }

        var assistantMessages = messages
            .Where(m => m.Role == MessageRole.Assistant)
            .ToList();

        if (assistantMessages.Count == 0)
        {
            Log.Warning("对话历史中没有AI回复，无法提取商业蓝图");
            return GenerateDefaultBlueprint(session.Canvas);
        }

        var lastAssistantMessage = assistantMessages.Last();
        var content = lastAssistantMessage.Content;

        Log.Information("[蓝图提取] AI回复长度: {Length}", content.Length);

        var blueprint = new BlueprintCard
        {
            FullContent = content,
            DeliveryMode = session.Canvas.BlueprintCard.DeliveryMode,
            IsActivated = true
        };

        ParseBlueprintMarkdown(content, blueprint);

        // 兜底：如果最终蓝图未提取到信任构建 SOP，从历史 AI 回复中软提取
        // （应对 AI 在最终蓝图中省略"信任构建SOP"章节的情况）
        if (!HasTrustSopContent(blueprint))
        {
            Log.Warning("最终蓝图未包含信任构建 SOP，尝试从历史 AI 回复中提取");
            var fallbackContent = BuildFallbackContent(assistantMessages);
            if (!string.IsNullOrEmpty(fallbackContent))
            {
                SoftExtractTrustSop(fallbackContent, blueprint);
                Log.Information("软提取完成: 案例={Cases}, 资质={Quals}, 证明={Socials}",
                    blueprint.TrustBuildingSop?.CaseStudies.Count ?? 0,
                    blueprint.TrustBuildingSop?.Qualifications.Count ?? 0,
                    blueprint.TrustBuildingSop?.SocialProofs.Count ?? 0);
            }
        }

        return blueprint;
    }

    /// <summary>
    /// 判断 trust SOP 是否已有内容。
    /// </summary>
    private static bool HasTrustSopContent(BlueprintCard blueprint)
    {
        if (blueprint.TrustBuildingSop == null) return false;
        return blueprint.TrustBuildingSop.CaseStudies.Count > 0
            || blueprint.TrustBuildingSop.Qualifications.Count > 0
            || blueprint.TrustBuildingSop.SocialProofs.Count > 0;
    }

    /// <summary>
    /// 拼接除最后一条 AI 回复外的所有 AI 回复内容（倒序：最近的优先）。
    /// </summary>
    private static string BuildFallbackContent(List<Message> assistantMessages)
    {
        // 排除最后一条（已是蓝图本体），但保留更早的高价值回复（特别是 70%/85% 的总结段）
        if (assistantMessages.Count <= 1) return string.Empty;

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < assistantMessages.Count - 1; i++)
        {
            sb.AppendLine($"## AI 回复 #{i + 1}");
            sb.AppendLine(assistantMessages[i].Content);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>
    /// 从文本中软提取信任 SOP 相关条目。
    /// 通过模式匹配寻找：
    /// - 案例 / 成果 / 数据 相关的列表项 → 成功案例
    /// - 资质 / 证书 / 认证 / 经历 相关的列表项 → 资质背书
    /// - 见证 / 评价 / 口碑 / 反馈 相关的列表项 → 社交证明
    /// - 引用块（"> "开头）→ 成功案例
    /// - 加粗段落（"**xxx**"）→ 分类后入对应子章节
    /// - 含多关键词的句子 → 默认入成功案例
    /// </summary>
    private static void SoftExtractTrustSop(string content, BlueprintCard blueprint)
    {
        if (string.IsNullOrEmpty(content)) return;

        blueprint.TrustBuildingSop ??= new TrustBuildingSop();
        var sop = blueprint.TrustBuildingSop;

        // 关键词分类（中英兼顾）
        var caseKeywords = new[] { "案例", "成果", "战果", "战绩", "数据", "学员", "客户", "训练", "交付", "落地", "成果库", "效果", "业绩", "复购", "转化" };
        var qualKeywords = new[] { "资质", "证书", "认证", "背书", "经历", "资历", "奖项", "荣誉", "权威", "专业", "资格" };
        var socialKeywords = new[] { "见证", "评价", "口碑", "反馈", "好评", "推荐", "感谢", "社交", "媒体", "报道", "发布", "曝光", "社群", "客户证言" };

        var lines = content.Split('\n');
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line)) continue;
            if (line.Length < 4 || line.Length > 300) continue;  // 跳过过短或过长的行

            string? item = null;

            // 1) 列表项
            if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                item = line.Substring(2).Trim();
            }
            // 2) 引用块
            else if (line.StartsWith(">"))
            {
                item = line.TrimStart('>').Trim();
            }
            // 3) 编号列表
            else if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^\d+[\.\)、]\s*\S"))
            {
                item = System.Text.RegularExpressions.Regex.Replace(line, @"^\d+[\.\)、]\s*", "");
            }
            // 4) 加粗段落（"**第一层：xxx**" 这种层标题）
            else if (line.StartsWith("**") && line.EndsWith("**") && line.Length > 4 && line.Length < 80)
            {
                item = line.Substring(2, line.Length - 4).Trim();
            }
            // 5) 含高密度关键词的长段落（≥2 个信任相关关键词）
            else if (line.Length >= 20)
            {
                int totalHits = caseKeywords.Count(k => line.Contains(k))
                              + qualKeywords.Count(k => line.Contains(k))
                              + socialKeywords.Count(k => line.Contains(k));
                if (totalHits >= 2)
                {
                    // 截取第一个句号/分号之前的内容，避免过长
                    var cut = line;
                    foreach (var sep in new[] { "。", "；", "!", "!", "?", "?", "\n" })
                    {
                        var idx = cut.IndexOf(sep);
                        if (idx > 10 && idx < cut.Length) { cut = cut.Substring(0, idx); break; }
                    }
                    item = cut.Trim();
                }
            }

            if (string.IsNullOrEmpty(item) || item.Length < 4) continue;

            // 去掉强调符号
            item = item.Trim('*', ' ', '\t', '✅', '⏳', '❌');

            // 分类
            var matchedSection = ClassifyTrustItem(item, caseKeywords, qualKeywords, socialKeywords);
            if (matchedSection == null) continue;

            AddTrustItemSafe(sop, matchedSection, item);
        }
    }

    private static void AddTrustItemSafe(TrustBuildingSop sop, string section, string item)
    {
        if (string.IsNullOrWhiteSpace(item) || item.Length < 2) return;
        switch (section)
        {
            case "case":
                if (sop.CaseStudies.Count < 8 && !ContainsSimilar(sop.CaseStudies, item))
                    sop.CaseStudies.Add(item);
                break;
            case "qualification":
                if (sop.Qualifications.Count < 8 && !ContainsSimilar(sop.Qualifications, item))
                    sop.Qualifications.Add(item);
                break;
            case "social":
                if (sop.SocialProofs.Count < 8 && !ContainsSimilar(sop.SocialProofs, item))
                    sop.SocialProofs.Add(item);
                break;
        }
    }

    private static string? ClassifyTrustItem(string item, string[] caseKw, string[] qualKw, string[] socialKw)
    {
        int caseScore = caseKw.Count(k => item.Contains(k));
        int qualScore = qualKw.Count(k => item.Contains(k));
        int socialScore = socialKw.Count(k => item.Contains(k));

        // 取最高分（要求至少 1 分）
        var max = Math.Max(caseScore, Math.Max(qualScore, socialScore));
        if (max == 0) return null;

        if (caseScore == max) return "case";
        if (qualScore == max) return "qualification";
        return "social";
    }

    private static bool ContainsSimilar(List<string> list, string item)
    {
        // 简单相似度：前 8 字相同即视为重复
        var key = item.Length > 8 ? item.Substring(0, 8) : item;
        return list.Any(x => x.StartsWith(key));
    }

    private void ParseBlueprintMarkdown(string markdown, BlueprintCard blueprint)
    {
        if (string.IsNullOrEmpty(markdown)) return;

        var lines = markdown.Split('\n');
        var currentSection = "";
        var trustSubSection = "";  // trust 下的子章节：case / qualification / social / ""

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("#"))
            {
                if (trimmed.Contains("超级签名") || trimmed.Contains("一句话签名"))
                {
                    currentSection = "signature";
                    trustSubSection = "";
                }
                else if (trimmed.Contains("信任") || trimmed.Contains("SOP") ||
                         trimmed.Contains("信任证据") || trimmed.Contains("案例库"))
                {
                    currentSection = "trust";
                    trustSubSection = "";  // 进入 trust 时重置子章节
                }
                else if (trimmed.Contains("交付") || trimmed.Contains("模式"))
                {
                    currentSection = "delivery";
                    trustSubSection = "";
                }
                else
                {
                    // 其它章节标题（如"战略总纲"、"月度运营节奏"等）：重置章节状态
                    currentSection = "";
                    trustSubSection = "";
                }
                continue;
            }

            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            switch (currentSection)
            {
                case "signature":
                    if (trimmed.StartsWith("**") && trimmed.EndsWith("**"))
                    {
                        var signatureText = trimmed.Trim('*');
                        ParseSuperSignature(signatureText, blueprint);
                    }
                    else if (blueprint.SuperSignature == null && !trimmed.StartsWith("-") && !trimmed.StartsWith(">"))
                    {
                        ParseSuperSignature(trimmed, blueprint);
                    }
                    break;

                case "trust":
                    // 识别子章节
                    if (trimmed.StartsWith("###") || trimmed.StartsWith("##") ||
                        trimmed.Contains("成功案例") || trimmed.Contains("客户案例") ||
                        trimmed.Contains("学员案例") || trimmed.Contains("战果") ||
                        trimmed.Contains("成果展示"))
                    {
                        trustSubSection = "case";
                    }
                    else if (trimmed.Contains("资质") || trimmed.Contains("背书") ||
                             trimmed.Contains("认证") || trimmed.Contains("证书") ||
                             trimmed.Contains("资历"))
                    {
                        trustSubSection = "qualification";
                    }
                    else if (trimmed.Contains("证明") || trimmed.Contains("社交") ||
                             trimmed.Contains("见证") || trimmed.Contains("评价") ||
                             trimmed.Contains("口碑") || trimmed.Contains("客户反馈"))
                    {
                        trustSubSection = "social";
                    }
                    else if (trimmed.StartsWith("-") || trimmed.StartsWith("*"))
                    {
                        var item = TrimBulletItem(trimmed);

                        if (blueprint.TrustBuildingSop == null)
                            blueprint.TrustBuildingSop = new TrustBuildingSop();

                        // 如果没有具体子章节，但有内容，仍然保留为「通用信任项」并放入案例
                        var targetSection = string.IsNullOrEmpty(trustSubSection) ? "case" : trustSubSection;
                        AddTrustItem(blueprint.TrustBuildingSop, targetSection, item);
                    }
                    else if (trimmed.StartsWith(">"))
                    {
                        // 引用块（如 "> 案例描述..."）也作为案例
                        var quote = trimmed.TrimStart('>').Trim();
                        if (!string.IsNullOrEmpty(quote))
                        {
                            if (blueprint.TrustBuildingSop == null)
                                blueprint.TrustBuildingSop = new TrustBuildingSop();
                            AddTrustItem(blueprint.TrustBuildingSop, "case", quote);
                        }
                    }
                    break;
            }
        }

        if (blueprint.SuperSignature == null)
        {
            ParseSuperSignatureFromFullText(markdown, blueprint);
        }
    }

    /// <summary>
    /// 把列表项的强调符号去掉，例如 "**案例标题**：内容" → "案例标题：内容"
    /// </summary>
    private static string TrimBulletItem(string raw)
    {
        var s = raw.TrimStart('-', '*', ' ');
        if (s.StartsWith("**") && s.EndsWith("**") && s.Length > 4)
        {
            s = s.Substring(2, s.Length - 4);
        }
        else if (s.StartsWith("**"))
        {
            s = s.Substring(2);
        }
        return s.Trim();
    }

    private static void AddTrustItem(TrustBuildingSop sop, string section, string item)
    {
        if (string.IsNullOrWhiteSpace(item) || item.Length < 2) return;
        switch (section)
        {
            case "case":
                if (!sop.CaseStudies.Contains(item)) sop.CaseStudies.Add(item);
                break;
            case "qualification":
                if (!sop.Qualifications.Contains(item)) sop.Qualifications.Add(item);
                break;
            case "social":
                if (!sop.SocialProofs.Contains(item)) sop.SocialProofs.Add(item);
                break;
        }
    }

    private void ParseSuperSignature(string text, BlueprintCard blueprint)
    {
        if (blueprint.SuperSignature != null) return;

        var signature = new SuperSignature();

        var woMatch = Regex.Match(text, @"我是([^，,]+)");
        if (woMatch.Success)
            signature.Identity = woMatch.Groups[1].Value.Trim();

        var yongMatch = Regex.Match(text, @"用([^，,，帮]+)");
        if (yongMatch.Success)
            signature.Method = yongMatch.Groups[1].Value.Trim();

        var bangMatch = Regex.Match(text, @"帮([^，,，解]+)");
        if (bangMatch.Success)
            signature.TargetAudience = bangMatch.Groups[1].Value.Trim();

        var jiejueMatch = Regex.Match(text, @"[解决决]?([^。.]+)");
        if (jiejueMatch.Success && string.IsNullOrEmpty(signature.TargetAudience))
        {
            signature.TargetAudience = jiejueMatch.Groups[1].Value.Trim();
        }

        if (!string.IsNullOrEmpty(signature.Identity) ||
            !string.IsNullOrEmpty(signature.Method))
        {
            blueprint.SuperSignature = signature;
        }
    }

    private void ParseSuperSignatureFromFullText(string markdown, BlueprintCard blueprint)
    {
        var woMatch = Regex.Match(markdown, @"我是([^，。,]+)");
        var bangMatch = Regex.Match(markdown, @"帮助([^，。,]+)");
        var jiejueMatch = Regex.Match(markdown, @"[解]?决([^，。,]+)");

        if (woMatch.Success || bangMatch.Success || jiejueMatch.Success)
        {
            blueprint.SuperSignature = new SuperSignature
            {
                Identity = woMatch.Success ? woMatch.Groups[1].Value : "",
                Method = "",
                TargetAudience = bangMatch.Success ? bangMatch.Groups[1].Value : "",
                Problem = jiejueMatch.Success ? jiejueMatch.Groups[1].Value : ""
            };
        }
    }

    private BlueprintCard? TryParseBlueprint(string response)
    {
        var json = ExtractJson(response);
        if (string.IsNullOrEmpty(json)) return null;

        try
        {
            dynamic? parsed = JsonConvert.DeserializeObject<BlueprintData>(json, _jsonSettings);
            if (parsed == null) return null;

            var blueprint = new BlueprintCard
            {
                SuperSignature = new SuperSignature
                {
                    Identity = parsed.super_signature?.identity ?? "",
                    Method = parsed.super_signature?.method ?? "",
                    TargetAudience = parsed.super_signature?.target_audience ?? "",
                    Problem = parsed.super_signature?.problem ?? ""
                },
                TrustBuildingSop = new TrustBuildingSop
                {
                    CaseStudies = ((List<object>?)parsed.trust_building_sop?.case_studies)?
                        .Select(o => o?.ToString() ?? "")
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToList() ?? new List<string>(),
                    Qualifications = ((List<object>?)parsed.trust_building_sop?.qualifications)?
                        .Select(o => o?.ToString() ?? "")
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToList() ?? new List<string>(),
                    SocialProofs = ((List<object>?)parsed.trust_building_sop?.social_proofs)?
                        .Select(o => o?.ToString() ?? "")
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToList() ?? new List<string>()
                }
            };

            return blueprint;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "蓝图JSON解析失败");
            return TryPartialParseBlueprint(json);
        }
    }

    private BlueprintCard? TryPartialParseBlueprint(string json)
    {
        var blueprint = new BlueprintCard();

        try
        {
            var superSigMatch = Regex.Match(json, @"""super_signature""\s*:\s*\{([^}]+)\}", RegexOptions.Singleline);
            if (superSigMatch.Success)
            {
                var sigContent = superSigMatch.Groups[1].Value;
                var identity = Regex.Match(sigContent, @"""identity""\s*:\s*""([^""]+)""").Groups[1].Value;
                var method = Regex.Match(sigContent, @"""method""\s*:\s*""([^""]+)""").Groups[1].Value;
                var audience = Regex.Match(sigContent, @"""target_audience""\s*:\s*""([^""]+)""").Groups[1].Value;
                var problem = Regex.Match(sigContent, @"""problem""\s*:\s*""([^""]+)""").Groups[1].Value;

                blueprint.SuperSignature = new SuperSignature
                {
                    Identity = identity,
                    Method = method,
                    TargetAudience = audience,
                    Problem = problem
                };
            }

            var casesMatch = Regex.Match(json, @"""case_studies""\s*:\s*\[([^\]]+)\]");
            if (casesMatch.Success)
            {
                var items = Regex.Matches(casesMatch.Groups[1].Value, @"""([^""]+)""");
                blueprint.TrustBuildingSop = new TrustBuildingSop
                {
                    CaseStudies = items.Select(m => m.Groups[1].Value).ToList()
                };
            }

            return blueprint.SuperSignature != null || blueprint.TrustBuildingSop != null
                ? blueprint : null;
        }
        catch
        {
            return null;
        }
    }

    private static BlueprintCard GenerateDefaultBlueprint(BusinessCanvas canvas)
    {
        return new BlueprintCard
        {
            IsActivated = true,
            SuperSignature = new SuperSignature
            {
                Identity = "商业顾问",
                Method = "价值显影引擎",
                TargetAudience = string.Join("、", canvas.MoatCard.ExclusiveResources.Take(2)),
                Problem = string.Join("、", canvas.PainPointCard.HiddenCosts.Take(2))
            },
            TrustBuildingSop = new TrustBuildingSop
            {
                CaseStudies = new List<string> { "暂无案例，请添加" },
                Qualifications = new List<string> { "暂无资质，请添加" },
                SocialProofs = new List<string> { "暂无社交证明，请添加" }
            },
            DeliveryMode = canvas.BlueprintCard.DeliveryMode
        };
    }
}

internal class BlueprintData
{
    public SuperSignatureData? super_signature { get; set; }
    public TrustBuildingSopData? trust_building_sop { get; set; }
}

internal class SuperSignatureData
{
    public string? identity { get; set; }
    public string? method { get; set; }
    public string? target_audience { get; set; }
    public string? problem { get; set; }
}

internal class TrustBuildingSopData
{
    public List<string>? case_studies { get; set; }
    public List<string>? qualifications { get; set; }
    public List<string>? social_proofs { get; set; }
}
