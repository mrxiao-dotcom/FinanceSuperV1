using System.IO;
using Newtonsoft.Json;
using Serilog;

namespace 商业超体价值与定位.Services;

public interface IPromptsService
{
    /// <summary>获取所有 prompt（key → value 字典）。</summary>
    IReadOnlyDictionary<string, string> GetAll();

    /// <summary>按 key 获取单个 prompt。</summary>
    string? Get(string key);

    /// <summary>批量保存。会自动写回磁盘并通知订阅者。</summary>
    void Save(IDictionary<string, string> prompts);

    /// <summary>恢复某个 prompt 到出厂默认值（基于 Resources/prompts.json 的首次拷贝）。</summary>
    void RestoreDefault(string key);

    /// <summary>恢复所有 prompt 到出厂默认值。</summary>
    void RestoreAllDefaults();

    /// <summary>所有 prompt 的元数据（key, 显示名, 描述, 占位符）。UI 用它来渲染列表。</summary>
    IReadOnlyList<PromptMeta> ListMetadata();

    /// <summary>订阅 prompt 变更（保存后触发）。</summary>
    event EventHandler? PromptsChanged;
}

public class PromptMeta
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Placeholders { get; set; } = new();
}

public class PromptsService : IPromptsService
{
    private readonly Dictionary<string, string> _prompts = new();
    private readonly string _promptsPath;
    private readonly string _backupPath;
    private readonly object _lock = new();

    public event EventHandler? PromptsChanged;

    public PromptsService()
    {
        // 优先使用输出目录下的 prompts.json（开发/打包均可访问）
        var baseDir = AppContext.BaseDirectory;
        var resourcePath = Path.Combine(baseDir, "Resources", "prompts.json");

        if (!File.Exists(resourcePath))
        {
            // 回退到 LocalApplicationData（兼容性兜底）
            var fallbackDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "商业超体");
            Directory.CreateDirectory(fallbackDir);
            resourcePath = Path.Combine(fallbackDir, "prompts.json");
        }

        _promptsPath = resourcePath;
        _backupPath = _promptsPath + ".bak";

        EnsureBackup();
        Load();
    }

    /// <summary>
    /// 首次启动时把应用自带的 prompts.json 复制为 .bak。
    /// 用户每次保存会覆盖 prompts.json，但 .bak 保持出厂版本不变。
    /// </summary>
    private void EnsureBackup()
    {
        try
        {
            if (!File.Exists(_backupPath) && File.Exists(_promptsPath))
            {
                File.Copy(_promptsPath, _backupPath);
                Log.Information("[PromptsService] 已创建出厂备份: {Path}", _backupPath);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[PromptsService] 创建备份失败");
        }
    }

    private void Load()
    {
        lock (_lock)
        {
            _prompts.Clear();
            try
            {
                if (File.Exists(_promptsPath))
                {
                    var json = File.ReadAllText(_promptsPath);
                    var obj = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                    if (obj != null)
                    {
                        foreach (var kvp in obj)
                        {
                            _prompts[kvp.Key] = kvp.Value;
                        }
                    }
                    Log.Information("[PromptsService] 已加载 {Count} 个 prompt，路径: {Path}",
                        _prompts.Count, _promptsPath);
                }
                else
                {
                    Log.Warning("[PromptsService] prompts.json 未找到: {Path}", _promptsPath);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[PromptsService] 加载 prompts.json 失败");
            }
        }
    }

    public IReadOnlyDictionary<string, string> GetAll()
    {
        lock (_lock)
        {
            return new Dictionary<string, string>(_prompts);
        }
    }

    public string? Get(string key)
    {
        lock (_lock)
        {
            if (_prompts.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v))
                return v;
        }
        // 兜底：磁盘字典里没找到（或为空）时，从嵌入资源中取出厂默认值
        return LoadDefaultFromEmbeddedResource(key);
    }

    /// <summary>
    /// 从程序集嵌入资源 prompts.json 读取指定 key 的出厂默认值。
    /// 用于兜底：磁盘上 prompts.json 是旧版本、缺新加的 key 时，仍能拿到正确的出厂 prompt。
    /// </summary>
    private static string? LoadDefaultFromEmbeddedResource(string key)
    {
        try
        {
            var assembly = typeof(PromptsService).Assembly;
            var resourceName = "商业超体价值与定位.Resources.prompts.json";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return null;
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            var defaults = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
            if (defaults != null && defaults.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v))
            {
                Log.Information("[PromptsService] 磁盘无值，从嵌入资源回退读取: {Key}", key);
                return v;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[PromptsService] 嵌入资源回退读取失败: {Key}", key);
        }
        return null;
    }

    public void Save(IDictionary<string, string> prompts)
    {
        lock (_lock)
        {
            _prompts.Clear();
            foreach (var kvp in prompts)
            {
                _prompts[kvp.Key] = kvp.Value;
            }
            PersistToDisk();
        }

        PromptsChanged?.Invoke(this, EventArgs.Empty);
        Log.Information("[PromptsService] prompts 已保存，共 {Count} 项", _prompts.Count);
    }

    public void RestoreDefault(string key)
    {
        var backup = LoadBackup();
        if (backup == null || !backup.TryGetValue(key, out var defaultValue))
        {
            Log.Warning("[PromptsService] 未找到 key={Key} 的出厂默认值", key);
            return;
        }

        lock (_lock)
        {
            _prompts[key] = defaultValue;
            PersistToDisk();
        }

        PromptsChanged?.Invoke(this, EventArgs.Empty);
        Log.Information("[PromptsService] 已恢复默认: {Key}", key);
    }

    public void RestoreAllDefaults()
    {
        var backup = LoadBackup();
        if (backup == null)
        {
            Log.Warning("[PromptsService] 备份文件不可用，无法恢复所有默认");
            return;
        }

        lock (_lock)
        {
            _prompts.Clear();
            foreach (var kvp in backup)
            {
                _prompts[kvp.Key] = kvp.Value;
            }
            PersistToDisk();
        }

        PromptsChanged?.Invoke(this, EventArgs.Empty);
        Log.Information("[PromptsService] 已恢复所有 prompt 到出厂默认值，共 {Count} 项", _prompts.Count);
    }

    public IReadOnlyList<PromptMeta> ListMetadata()
    {
        var list = new List<PromptMeta>
        {
            new()
            {
                Key = "Stage1Prompt",
                DisplayName = "阶段 1：探索独占资源",
                Description = "诊断对话的第一步。AI 通过追问帮助用户挖掘商业护城河。语气专业温暖，每次回复 ≤250 字。",
                Placeholders = new() { "（无占位符）" }
            },
            new()
            {
                Key = "Stage2Prompt",
                DisplayName = "阶段 2：逼问隐性痛点",
                Description = "挖掘客户不购买的代价。语气略带紧迫感，强调定价权来源。",
                Placeholders = new() { "（无占位符）" }
            },
            new()
            {
                Key = "Stage3Prompt",
                DisplayName = "阶段 3：确认履约边界",
                Description = "确认时间/精力/能力/利润边界。语气务实直接。",
                Placeholders = new() { "（无占位符）" }
            },
            new()
            {
                Key = "Stage4Prompt",
                DisplayName = "阶段 4：构建护城河",
                Description = "基于已有信息生成降维打击策略。语气自信有战略高度。",
                Placeholders = new() { "（无占位符）" }
            },
            new()
            {
                Key = "Stage5Prompt",
                DisplayName = "阶段 5：生成终极蓝图",
                Description = "输出五大章节商业蓝图（核心定位 / 价值锚定 / 信任SOP / 成交路径 / 交付模式）。",
                Placeholders = new() { "（无占位符）" }
            },
            new()
            {
                Key = "WeeklyPlanPrompt",
                DisplayName = "周计划：4 周执行大纲",
                Description = "基于商业蓝图生成未来 N 周的执行大纲（默认 4 周）。",
                Placeholders = new() { "{{blueprint}}", "{{total_weeks}}" }
            },
            new()
            {
                Key = "DailyTasksPrompt",
                DisplayName = "周计划：日常任务清单",
                Description = "基于周大纲生成本周可执行的日常内容任务。每条任务的 copywriting 可作为下游应用输入。",
                Placeholders = new() { "{{blueprint}}", "{{week_outline}}", "{{week_number}}", "{{total_weeks}}" }
            },
        };

        return list;
    }

    /// <summary>读取出厂备份文件，如果不存在则尝试从主文件读取。</summary>
    private Dictionary<string, string>? LoadBackup()
    {
        try
        {
            var sourcePath = File.Exists(_backupPath) ? _backupPath : _promptsPath;
            if (!File.Exists(sourcePath)) return null;

            var json = File.ReadAllText(sourcePath);
            return JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PromptsService] 读取备份失败");
            return null;
        }
    }

    private void PersistToDisk()
    {
        try
        {
            var dir = Path.GetDirectoryName(_promptsPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // 用 Newtonsoft.Json 的格式化输出（保持与现有 prompts.json 一致的风格）
            var json = JsonConvert.SerializeObject(_prompts, Formatting.Indented);
            File.WriteAllText(_promptsPath, json);
            Log.Information("[PromptsService] 已写回磁盘: {Path}", _promptsPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PromptsService] 写回磁盘失败");
        }
    }
}