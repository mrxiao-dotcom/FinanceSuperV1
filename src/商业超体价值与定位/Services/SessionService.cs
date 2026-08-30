using System.IO;
using System.Text;
using Newtonsoft.Json;
using Serilog;
using 商业超体价值与定位.Models;

namespace 商业超体价值与定位.Services;

public interface ISessionService
{
    DiagnosticSession CurrentSession { get; }
    IReadOnlyList<SessionInfo> SessionHistory { get; }
    string? CurrentSessionId { get; set; }
    bool HasSavedSession();
    void RestoreSession();
    void RestoreLastSession();
    void UpdateSession(Action<DiagnosticSession> updateAction);
    void AutoSave();
    void SaveSession();
    void NewSession();
    void SwitchSession(string sessionId);
    void LoadSessionHistory();
    void DeleteSession(string sessionId);
}

public class SessionInfo
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime LastModifiedAt { get; set; }
    public int MessageCount { get; set; }
    public DiagnosticStage LastStage { get; set; }
    public string FolderPath { get; set; } = "";
    public bool IsCurrentSession { get; set; }
}

public class SessionService : ISessionService
{
    private readonly string _sessionsFolder;
    private DiagnosticSession _currentSession;
    private readonly List<SessionInfo> _sessionHistory = new();
    private readonly object _historyLock = new();

    public DiagnosticSession CurrentSession => _currentSession;
    public IReadOnlyList<SessionInfo> SessionHistory
    {
        get
        {
            lock (_historyLock)
            {
                return _sessionHistory.AsReadOnly();
            }
        }
    }
    public string? CurrentSessionId { get; set; }

    public SessionService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "商业超体");
        Directory.CreateDirectory(appDataPath);
        _sessionsFolder = Path.Combine(appDataPath, "sessions");
        Directory.CreateDirectory(_sessionsFolder);

        // 迁移旧格式
        MigrateOldFormat();

        _currentSession = new DiagnosticSession();
        LoadSessionHistory();
    }

    private string GetSessionFolderPath(string sessionId)
    {
        return Path.Combine(_sessionsFolder, sessionId);
    }

    private string GetSessionFilePath(string sessionId)
    {
        return Path.Combine(GetSessionFolderPath(sessionId), "session.json");
    }

    private void EnsureSessionFolder(string sessionId)
    {
        var folderPath = GetSessionFolderPath(sessionId);
        Directory.CreateDirectory(folderPath);
    }

    private void MigrateOldFormat()
    {
        try
        {
            var oldFiles = Directory.GetFiles(_sessionsFolder, "*.json");
            foreach (var oldFile in oldFiles)
            {
                var fileName = Path.GetFileName(oldFile);
                if (fileName == "settings.json") continue;

                var sessionId = Path.GetFileNameWithoutExtension(oldFile);
                var newFolder = GetSessionFolderPath(sessionId);
                var newFile = Path.Combine(newFolder, "session.json");

                if (!Directory.Exists(newFolder))
                {
                    Directory.CreateDirectory(newFolder);
                    File.Move(oldFile, newFile);
                    Log.Information("已迁移旧格式会话: {SessionId}", sessionId);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "迁移旧格式会话时发生错误");
        }
    }

    /// <summary>
    /// 从磁盘加载所有会话历史
    /// 仅在启动时调用一次
    /// </summary>
    public void LoadSessionHistory()
    {
        lock (_historyLock)
        {
            _sessionHistory.Clear();
            try
            {
                var sessionFolders = Directory.GetDirectories(_sessionsFolder);
                foreach (var folder in sessionFolders)
                {
                    var sessionId = Path.GetFileName(folder);
                    var sessionFile = Path.Combine(folder, "session.json");

                    if (!File.Exists(sessionFile))
                        continue;

                    try
                    {
                        var json = File.ReadAllText(sessionFile);
                        var session = JsonConvert.DeserializeObject<DiagnosticSession>(json);
                        if (session != null)
                        {
                            _sessionHistory.Add(new SessionInfo
                            {
                                Id = session.Id,
                                Title = GenerateSessionTitle(session),
                                CreatedAt = session.CreatedAt,
                                LastModifiedAt = session.LastModifiedAt,
                                MessageCount = session.Messages.Count,
                                LastStage = session.CurrentStage,
                                FolderPath = folder
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "加载会话文件失败: {Folder}", folder);
                    }
                }
                _sessionHistory.Sort((a, b) => b.LastModifiedAt.CompareTo(a.LastModifiedAt));
                Log.Information("已加载 {Count} 个历史会话", _sessionHistory.Count);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "加载会话历史失败");
            }
        }
    }

    private string GenerateSessionTitle(DiagnosticSession session)
    {
        // 尝试从对话中提取主题
        var firstUserMessage = session.Messages
            .FirstOrDefault(m => m.Role == MessageRole.User)?.Content;

        if (!string.IsNullOrEmpty(firstUserMessage))
        {
            var shortTitle = firstUserMessage.Length > 20
                ? firstUserMessage.Substring(0, 20) + "..."
                : firstUserMessage;
            var dateStr = session.CreatedAt.ToString("MM-dd");
            return $"{dateStr} {shortTitle}";
        }

        var dateStr2 = session.CreatedAt.ToString("MM-dd HH:mm");
        return $"{dateStr2} 商业对话";
    }

    public bool HasSavedSession()
    {
        lock (_historyLock)
        {
            return _sessionHistory.Count > 0;
        }
    }

    public void RestoreSession()
    {
        RestoreLastSession();
    }

    public void RestoreLastSession()
    {
        lock (_historyLock)
        {
            if (_sessionHistory.Count > 0)
            {
                var lastSession = _sessionHistory.First();
                RestoreSessionById(lastSession.Id);
            }
        }
    }

    private void RestoreSessionById(string sessionId)
    {
        try
        {
            var filePath = GetSessionFilePath(sessionId);
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                _currentSession = JsonConvert.DeserializeObject<DiagnosticSession>(json)
                    ?? new DiagnosticSession();
                CurrentSessionId = sessionId;
                Log.Information("会话已恢复: {SessionId}", sessionId);
            }
            else
            {
                _currentSession = new DiagnosticSession();
                CurrentSessionId = _currentSession.Id;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "恢复会话时发生错误");
            _currentSession = new DiagnosticSession();
            CurrentSessionId = _currentSession.Id;
        }
    }

    public void UpdateSession(Action<DiagnosticSession> updateAction)
    {
        updateAction(_currentSession);
        _currentSession.LastModifiedAt = DateTime.Now;
    }

    public void AutoSave()
    {
        try
        {
            // 没有当前会话时（例如刚被删除），不要自动保存
            if (string.IsNullOrEmpty(CurrentSessionId))
            {
                Log.Debug("AutoSave 跳过：没有当前会话");
                return;
            }

            EnsureSessionFolder(CurrentSessionId);

            var sessionFile = GetSessionFilePath(CurrentSessionId);
            var json = JsonConvert.SerializeObject(_currentSession, Formatting.Indented);
            File.WriteAllText(sessionFile, json);

            // 导出对话记录为 Markdown
            ExportMessagesToMarkdown(_currentSession, CurrentSessionId);

            // 导出商业画布为 Markdown
            ExportCanvasToMarkdown(_currentSession, CurrentSessionId);

            // 更新内存中的会话信息
            UpdateSessionHistoryInfo();

            Log.Debug("会话已自动保存: {SessionId}", CurrentSessionId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "自动保存会话时发生错误");
        }
    }

    private void ExportMessagesToMarkdown(DiagnosticSession session, string sessionId)
    {
        try
        {
            var folderPath = GetSessionFolderPath(sessionId);
            var mdFile = Path.Combine(folderPath, "对话记录.md");

            var sb = new StringBuilder();
            sb.AppendLine("# 商业诊断对话记录");
            sb.AppendLine();
            sb.AppendLine($"**会话ID**: {session.Id}");
            sb.AppendLine($"**创建时间**: {session.CreatedAt:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"**当前阶段**: {session.CurrentStage}");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();

            foreach (var msg in session.Messages)
            {
                var role = msg.Role == MessageRole.User ? "👤 用户" : "🤖 AI顾问";
                sb.AppendLine($"### {role}");
                sb.AppendLine();
                sb.AppendLine(msg.Content);
                sb.AppendLine();
                sb.AppendLine($"*{msg.Timestamp:HH:mm:ss}*");
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
            }

            File.WriteAllText(mdFile, sb.ToString(), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "导出对话记录失败");
        }
    }

    private void ExportCanvasToMarkdown(DiagnosticSession session, string sessionId)
    {
        try
        {
            var folderPath = GetSessionFolderPath(sessionId);
            var mdFile = Path.Combine(folderPath, "商业画布.md");

            var canvas = session.Canvas;
            var sb = new StringBuilder();
            sb.AppendLine("# 商业画布");
            sb.AppendLine();
            sb.AppendLine($"> 生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine("---\n");

            // 护城河
            sb.AppendLine("## 🏰 护城河 (Moat)");
            sb.AppendLine();
            if (canvas.MoatCard.IsActivated)
            {
                sb.AppendLine($"**状态**: ✅ 已激活\n");
                if (canvas.MoatCard.ExclusiveResources.Any())
                {
                    sb.AppendLine("**独占资源**:");
                    foreach (var r in canvas.MoatCard.ExclusiveResources)
                        sb.AppendLine($"- {r}");
                    sb.AppendLine();
                }
                if (canvas.MoatCard.CognitiveAssets.Any())
                {
                    sb.AppendLine("**认知资产**:");
                    foreach (var a in canvas.MoatCard.CognitiveAssets)
                        sb.AppendLine($"- {a}");
                    sb.AppendLine();
                }
                sb.AppendLine($"**内容摘要**: {canvas.MoatCard.Content}");
            }
            else
            {
                sb.AppendLine("**状态**: ⏳ 待挖掘\n");
                sb.AppendLine($"**提示**: {canvas.MoatCard.Content}");
            }
            sb.AppendLine("\n---\n");

            // 客户痛点
            sb.AppendLine("## 🎯 客户痛点 (Pain Points)");
            sb.AppendLine();
            if (canvas.PainPointCard.IsActivated)
            {
                sb.AppendLine($"**状态**: ✅ 已激活\n");
                if (canvas.PainPointCard.HiddenCosts.Any())
                {
                    sb.AppendLine("**隐性成本**:");
                    foreach (var c in canvas.PainPointCard.HiddenCosts)
                        sb.AppendLine($"- {c}");
                    sb.AppendLine();
                }
                if (canvas.PainPointCard.CriticalRisks.Any())
                {
                    sb.AppendLine("**致命风险**:");
                    foreach (var r in canvas.PainPointCard.CriticalRisks)
                        sb.AppendLine($"- {r}");
                    sb.AppendLine();
                }
                sb.AppendLine($"**内容摘要**: {canvas.PainPointCard.Content}");
            }
            else
            {
                sb.AppendLine("**状态**: ⏳ 待挖掘\n");
                sb.AppendLine($"**提示**: {canvas.PainPointCard.Content}");
            }
            sb.AppendLine("\n---\n");

            // 情感溢价
            sb.AppendLine("## 💜 情感溢价 (Emotional Premium)");
            sb.AppendLine();
            if (canvas.EmotionalPremiumCard.IsActivated)
            {
                sb.AppendLine($"**状态**: ✅ 已激活\n");
                if (canvas.EmotionalPremiumCard.EmotionalDrivers.Any())
                {
                    sb.AppendLine("**情感驱动因素**:");
                    foreach (var d in canvas.EmotionalPremiumCard.EmotionalDrivers)
                        sb.AppendLine($"- {d}");
                    sb.AppendLine();
                }
                sb.AppendLine($"**内容摘要**: {canvas.EmotionalPremiumCard.Content}");
            }
            else
            {
                sb.AppendLine("**状态**: ⏳ 待挖掘\n");
                sb.AppendLine($"**提示**: {canvas.EmotionalPremiumCard.Content}");
            }
            sb.AppendLine("\n---\n");

            // 商业蓝图
            sb.AppendLine("## 📋 商业蓝图 (Business Blueprint)");
            sb.AppendLine();
            if (canvas.BlueprintCard.IsActivated || canvas.BlueprintCard.DeliveryMode != null)
            {
                sb.AppendLine($"**状态**: ✅ 已生成\n");

                if (canvas.BlueprintCard.SuperSignature != null)
                {
                    sb.AppendLine("### 超级签名");
                    sb.AppendLine();
                    sb.AppendLine($"- **身份定位**: {canvas.BlueprintCard.SuperSignature.Identity}");
                    sb.AppendLine($"- **方法论**: {canvas.BlueprintCard.SuperSignature.Method}");
                    sb.AppendLine($"- **目标受众**: {canvas.BlueprintCard.SuperSignature.TargetAudience}");
                    sb.AppendLine($"- **解决的问题**: {canvas.BlueprintCard.SuperSignature.Problem}");
                    sb.AppendLine();
                    sb.AppendLine($"> \"{canvas.BlueprintCard.SuperSignature}\"");
                    sb.AppendLine();
                }

                if (canvas.BlueprintCard.DeliveryMode != null)
                {
                    sb.AppendLine("### 交付模式");
                    sb.AppendLine();
                    sb.AppendLine($"- **模式名称**: {canvas.BlueprintCard.DeliveryMode.Name}");
                    sb.AppendLine($"- **模式描述**: {canvas.BlueprintCard.DeliveryMode.Description}");
                    sb.AppendLine($"- **高触感**: {(canvas.BlueprintCard.DeliveryMode.IsHighTouch ? "是" : "否")}");
                    sb.AppendLine();
                }

                if (canvas.BlueprintCard.TrustBuildingSop != null)
                {
                    sb.AppendLine("### 信任建立 SOP");
                    if (canvas.BlueprintCard.TrustBuildingSop.CaseStudies.Any())
                    {
                        sb.AppendLine();
                        sb.AppendLine("**案例**:");
                        foreach (var c in canvas.BlueprintCard.TrustBuildingSop.CaseStudies)
                            sb.AppendLine($"- {c}");
                    }
                    if (canvas.BlueprintCard.TrustBuildingSop.Qualifications.Any())
                    {
                        sb.AppendLine();
                        sb.AppendLine("**资质**:");
                        foreach (var q in canvas.BlueprintCard.TrustBuildingSop.Qualifications)
                            sb.AppendLine($"- {q}");
                    }
                    if (canvas.BlueprintCard.TrustBuildingSop.SocialProofs.Any())
                    {
                        sb.AppendLine();
                        sb.AppendLine("**社会证明**:");
                        foreach (var s in canvas.BlueprintCard.TrustBuildingSop.SocialProofs)
                            sb.AppendLine($"- {s}");
                    }
                    sb.AppendLine();
                }

                sb.AppendLine($"**内容摘要**: {canvas.BlueprintCard.Content}");
            }
            else
            {
                sb.AppendLine("**状态**: ⏳ 待生成\n");
                sb.AppendLine($"**提示**: {canvas.BlueprintCard.Content}");
            }
            sb.AppendLine("\n---\n");

            // 完成度
            sb.AppendLine("## 📊 完成度");
            sb.AppendLine();
            sb.AppendLine($"- 护城河: {(canvas.MoatCard.IsActivated ? "✅" : "❌")}");
            sb.AppendLine($"- 客户痛点: {(canvas.PainPointCard.IsActivated ? "✅" : "❌")}");
            sb.AppendLine($"- 情感溢价: {(canvas.EmotionalPremiumCard.IsActivated ? "✅" : "❌")}");
            sb.AppendLine($"- 商业蓝图: {(canvas.BlueprintCard.IsActivated || canvas.BlueprintCard.DeliveryMode != null ? "✅" : "❌")}");
            sb.AppendLine();
            sb.AppendLine($"**总体完成度**: {canvas.CompletionPercentage:P0}");

            File.WriteAllText(mdFile, sb.ToString(), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "导出商业画布失败");
        }
    }

    private void UpdateSessionHistoryInfo()
    {
        lock (_historyLock)
        {
            var existing = _sessionHistory.FirstOrDefault(s => s.Id == CurrentSessionId);
            var folderPath = GetSessionFolderPath(CurrentSessionId);

            if (existing != null)
            {
                existing.LastModifiedAt = _currentSession.LastModifiedAt;
                existing.MessageCount = _currentSession.Messages.Count;
                existing.LastStage = _currentSession.CurrentStage;
                existing.Title = GenerateSessionTitle(_currentSession);
            }
            else
            {
                _sessionHistory.Add(new SessionInfo
                {
                    Id = _currentSession.Id,
                    Title = GenerateSessionTitle(_currentSession),
                    CreatedAt = _currentSession.CreatedAt,
                    LastModifiedAt = _currentSession.LastModifiedAt,
                    MessageCount = _currentSession.Messages.Count,
                    LastStage = _currentSession.CurrentStage,
                    FolderPath = folderPath
                });
            }
            _sessionHistory.Sort((a, b) => b.LastModifiedAt.CompareTo(a.LastModifiedAt));
        }
    }

    public void SaveSession()
    {
        AutoSave();
    }

    public void NewSession()
    {
        // 保存当前会话
        AutoSave();

        // 创建新会话
        _currentSession = new DiagnosticSession();
        CurrentSessionId = _currentSession.Id;

        // 确保文件夹存在
        EnsureSessionFolder(CurrentSessionId);

        // 添加到内存历史列表
        lock (_historyLock)
        {
            _sessionHistory.Insert(0, new SessionInfo
            {
                Id = _currentSession.Id,
                Title = GenerateSessionTitle(_currentSession),
                CreatedAt = _currentSession.CreatedAt,
                LastModifiedAt = _currentSession.LastModifiedAt,
                MessageCount = 0,
                LastStage = _currentSession.CurrentStage,
                FolderPath = GetSessionFolderPath(CurrentSessionId)
            });
        }

        Log.Information("已创建新会话: {SessionId}", _currentSession.Id);
    }

    public void SwitchSession(string sessionId)
    {
        if (sessionId == CurrentSessionId)
            return;

        // 检查会话是否还存在（从内存列表检查，不需要重新加载）
        lock (_historyLock)
        {
            if (!_sessionHistory.Any(s => s.Id == sessionId))
            {
                Log.Warning("尝试切换到已删除的会话: {SessionId}", sessionId);
                return;
            }
        }

        // 检查磁盘文件
        var sessionFile = GetSessionFilePath(sessionId);
        if (!File.Exists(sessionFile))
        {
            Log.Warning("会话文件不存在: {SessionId}", sessionId);
            return;
        }

        AutoSave();
        RestoreSessionById(sessionId);

        Log.Information("已切换到会话: {SessionId}", sessionId);
    }

    public void DeleteSession(string sessionId)
    {
        var folderPath = GetSessionFolderPath(sessionId);
        Log.Information("尝试删除会话文件夹: {SessionId}, Path: {Path}", sessionId, folderPath);

        // 如果删除的是当前会话，清空 CurrentSessionId。
        // 不要重置 _currentSession，让上层（ConfirmDelete/SwitchToSessionAsync）决定下一步动作。
        // AutoSave 看到 CurrentSessionId 为空时会自动跳过，避免重建已删除的文件夹。
        if (sessionId == CurrentSessionId)
        {
            Log.Information("删除的是当前会话，清空 CurrentSessionId");
            CurrentSessionId = null;
        }

        // 删除整个文件夹
        if (Directory.Exists(folderPath))
        {
            try
            {
                Directory.Delete(folderPath, recursive: true);
                Log.Information("Directory.Delete 已执行");

                // 验证删除是否成功
                if (Directory.Exists(folderPath))
                {
                    Log.Warning("文件夹仍然存在，尝试强制删除");
                    Thread.Sleep(100);
                    try
                    {
                        Directory.Delete(folderPath, recursive: true);
                        Log.Information("重试删除成功");
                    }
                    catch (Exception retryEx)
                    {
                        Log.Error(retryEx, "重试删除失败");
                    }
                }
                else
                {
                    Log.Information("已删除会话文件夹: {SessionId}", sessionId);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "删除会话文件夹失败: {SessionId}", sessionId);
            }
        }
        else
        {
            Log.Information("文件夹不存在，无需删除: {SessionId}", sessionId);
        }

        // 从内存列表中移除
        lock (_historyLock)
        {
            var sessionInfo = _sessionHistory.FirstOrDefault(s => s.Id == sessionId);
            if (sessionInfo != null)
            {
                _sessionHistory.Remove(sessionInfo);
                Log.Information("已从历史记录中移除: {SessionId}", sessionId);
            }
        }
    }
}
