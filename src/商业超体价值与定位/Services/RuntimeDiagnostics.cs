using System.Diagnostics;
using System.IO;
using System.Reflection;
using Serilog;

namespace 商业超体价值与定位.Services;

/// <summary>
/// 运行时环境诊断：版本、路径、权限、磁盘空间、当前会话状态。
/// 仅在启动时调用一次，结果写入日志用于排错。
/// </summary>
public static class RuntimeDiagnostics
{
    private static readonly Serilog.ILogger CrashLog =
        Serilog.Log.ForContext("SourceContext", "RuntimeDiagnostics");

    public static void RunStartupDiagnostics()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version?.ToString() ?? "unknown";
            var location = assembly.IsCollectible
                ? "(collectible dynamic assembly)"
                : (assembly.Location ?? "(unknown)");

            Log.Information("=== 运行时环境诊断 ===");
            Log.Information("  应用版本    : {Version}", version);
            Log.Information("  .NET 版本   : {Version}", Environment.Version.ToString());
            Log.Information("  操作系统    : {OS}", Environment.OSVersion.ToString());
            Log.Information("  64 位 OS    : {Flag}", Environment.Is64BitOperatingSystem);
            Log.Information("  64 位进程   : {Flag}", Environment.Is64BitProcess);
            Log.Information("  用户身份    : {User}", Environment.UserName);
            Log.Information("  是否管理员  : {Flag}", IsRunAsAdministrator());
            Log.Information("  工作目录    : {Cwd}", Environment.CurrentDirectory);
            Log.Information("  程序集路径  : {Path}", location);
            Log.Information("  LocalAppData: {Path}", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

            var sessionsRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "商业超体");
            Log.Information("  会话根目录  : {Path}", sessionsRoot);
            Log.Information("  根目录可写  : {Flag}", CanWriteTo(sessionsRoot));

            LogDiskSpace(sessionsRoot);
            Log.Information("========================");
        }
        catch (Exception ex)
        {
            CrashLog.Error(ex, "运行时环境诊断失败");
        }
    }

    public static string DescribeDiskSpace(string folderPath)
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(folderPath) ?? folderPath);
            return $"Drive={drive.Name} Total={drive.TotalSize / 1024 / 1024}MB " +
                   $"Free={drive.AvailableFreeSpace / 1024 / 1024}MB";
        }
        catch
        {
            return "(unknown)";
        }
    }

    private static bool IsRunAsAdministrator()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static bool CanWriteTo(string folderPath)
    {
        try
        {
            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);
            var probe = Path.Combine(folderPath, ".write-probe-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void LogDiskSpace(string folderPath)
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(folderPath) ?? folderPath);
            Log.Information("  数据盘符    : {Drive}", drive.Name);
            Log.Information("  总空间      : {Size} MB", drive.TotalSize / 1024 / 1024);
            Log.Information("  可用空间    : {Size} MB", drive.AvailableFreeSpace / 1024 / 1024);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "无法读取磁盘空间");
        }
    }

    /// <summary>
    /// 把崩溃异常写到独立的崩溃日志，方便与业务日志分离。
    /// </summary>
    public static string WriteCrashLog(Exception ex, string source, string? extra = null)
    {
        try
        {
            var crashDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "商业超体", "logs", "crashes");
            Directory.CreateDirectory(crashDir);

            var fileName = $"crash-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}.log";
            var crashFile = Path.Combine(crashDir, fileName);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== Crash Report @ {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ===");
            sb.AppendLine($"Source : {source}");
            sb.AppendLine($"OS     : {Environment.OSVersion}");
            sb.AppendLine(".NET   : " + Environment.Version);
            sb.AppendLine($"User   : {Environment.UserName} (Admin={IsRunAsAdministrator()})");
            sb.AppendLine($"Cwd    : {Environment.CurrentDirectory}");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(extra))
            {
                sb.AppendLine("--- Extra context ---");
                sb.AppendLine(extra);
                sb.AppendLine();
            }
            sb.AppendLine("--- Exception ---");
            sb.AppendLine(ex.ToString());

            File.WriteAllText(crashFile, sb.ToString());
            return crashFile;
        }
        catch (Exception writeEx)
        {
            // 兜底中的兜底：崩到连日志都写不进去了，至少 stderr
            Debug.WriteLine($"[CrashLog failed] {writeEx}");
            return string.Empty;
        }
    }
}