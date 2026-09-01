using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace 商业超体价值与定位.Models;

/// <summary>
/// 周计划聚合根。
/// 一个会话对应一份 WeeklyPlan（包含 4 周大纲 + 每周的任务）。
/// 持久化为 JSON 与 Markdown 两种形态：
///   - JSON：随 session.json 一起加载/保存，用于 UI 即时回显
///   - Markdown：可读镜像，方便人工查阅与复制到下游应用
/// </summary>
public class WeeklyPlan
{
    /// <summary>会话 ID（与 DiagnosticSession.Id 对齐，用于校验）。</summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>生成时间。</summary>
    public DateTime GeneratedAt { get; set; } = DateTime.Now;

    /// <summary>最后一次重新生成某周任务的时间。</summary>
    public DateTime LastRegeneratedAt { get; set; } = DateTime.Now;

    /// <summary>输入蓝图摘要（仅前 600 字 + 后续省略号），用于追溯与可读性。</summary>
    public string BlueprintExcerpt { get; set; } = string.Empty;

    /// <summary>周计划总数（默认 4，可由 prompt 配置覆盖）。</summary>
    public int TotalWeeks { get; set; } = 4;

    /// <summary>四周大纲 + 任务详情。</summary>
    public List<WeekPlan> Weeks { get; set; } = new();
}

/// <summary>
/// 单周计划。
/// Outline 是 4 周总规划时生成的该周"主题/重点/产出方向"；
/// DailyTasks 在用户选定本周后由 WeeklyPlanService 二次生成。
/// </summary>
public class WeekPlan
{
    /// <summary>第几周，从 1 开始。</summary>
    public int WeekNumber { get; set; }

    /// <summary>周主题（一句话目标）。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>本周重点（Markdown 列表形式的原文，保留 LLM 输出原貌）。</summary>
    public string Outline { get; set; } = string.Empty;

    /// <summary>本周日常任务列表。</summary>
    public List<DailyTask> DailyTasks { get; set; } = new();

    /// <summary>任务是否已生成。</summary>
    [JsonIgnore]
    public bool HasTasks => DailyTasks != null && DailyTasks.Count > 0;

    /// <summary>任务生成时间。</summary>
    public DateTime? TasksGeneratedAt { get; set; }
}

/// <summary>
/// 日常工作项。
/// 对应下游应用的"输入物料"：每个任务产出的文案/策略/钩子可直接喂给
/// 文章生成器、短视频脚本生成器、图片生成器等下游工具。
/// </summary>
public class DailyTask
{
    /// <summary>任务编号（从 1 开始）。</summary>
    public int Index { get; set; }

    /// <summary>所属周次。</summary>
    public int WeekNumber { get; set; }

    /// <summary>任务标题，如「撰写公众号开篇文章」。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>任务类型 / 内容形态。</summary>
    public ContentFormat Format { get; set; } = ContentFormat.Article;

    /// <summary>策略/目的，例如「教育型软文 / 痛点钩子 / 引流私域」。</summary>
    public string Strategy { get; set; } = string.Empty;

    /// <summary>目标受众切片，如「30-40 岁宝妈 / 一线城市职场中层」。</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>分发渠道，例如「公众号 / 视频号 / 小红书 / 朋友圈」。</summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>核心钩子/卖点（短句，用于下游应用的"开场钩子"）。</summary>
    public string Hook { get; set; } = string.Empty;

    /// <summary>文案正文（Markdown，可直接复制到下游应用）。</summary>
    public string Copywriting { get; set; } = string.Empty;

    /// <summary>附加说明（可选，备注/注意事项）。</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>生成时间。</summary>
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// 内容形态枚举。决定下游应用如何消费这条任务的产出。
/// </summary>
public enum ContentFormat
{
    /// <summary>公众号/知乎等长图文。</summary>
    Article,

    /// <summary>短视频脚本（口播稿 + 分镜）。</summary>
    ShortVideoScript,

    /// <summary>图文素材（朋友圈/小红书图配文）。</summary>
    ImagePost,

    /// <summary>海报文案 / 单图长文案。</summary>
    PosterCopy,

    /// <summary>私聊/群发话术。</summary>
    PrivateMessage,

    /// <summary>通用素材，由用户在备注里说明具体用途。</summary>
    Generic
}
