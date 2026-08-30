using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using Serilog;
using 商业超体价值与定位.Models;

namespace 商业超体价值与定位.Services;

public interface IExportService
{
    /// <summary>
    /// 根据文件扩展名自动选择导出格式。
    /// .pdf / .html / .md
    /// </summary>
    Task ExportBlueprintAsync(BlueprintCard blueprint, string filePath);

    /// <summary>
    /// 旧 API：导出为 PDF（headless 浏览器不可用时回退到打开浏览器）。
    /// </summary>
    Task ExportBlueprintToPdfAsync(BlueprintCard blueprint, string filePath);

    Task<string> GenerateMarkdownAsync(BlueprintCard blueprint);
}

public class ExportService : IExportService
{
    public async Task ExportBlueprintAsync(BlueprintCard blueprint, string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var markdown = await GenerateMarkdownAsync(blueprint);

        switch (ext)
        {
            case ".pdf":
                await ExportBlueprintToPdfAsync(blueprint, filePath);
                break;

            case ".html":
            case ".htm":
                var html = GenerateHtmlForPdf(markdown);
                await File.WriteAllTextAsync(filePath, html, Encoding.UTF8);
                Log.Information("HTML 导出完成: {FilePath}", filePath);
                break;

            default:
                await File.WriteAllTextAsync(filePath, markdown, Encoding.UTF8);
                Log.Information("Markdown 导出完成: {FilePath}", filePath);
                break;
        }
    }

    public async Task ExportBlueprintToPdfAsync(BlueprintCard blueprint, string pdfPath)
    {
        Log.Information("开始导出 PDF: {FilePath}", pdfPath);

        var markdown = await GenerateMarkdownAsync(blueprint);
        var htmlContent = GenerateHtmlForPdf(markdown);

        // 写入临时 HTML
        var tempHtml = Path.Combine(Path.GetTempPath(), $"商业蓝图_{Guid.NewGuid()}.html");
        await File.WriteAllTextAsync(tempHtml, htmlContent, Encoding.UTF8);

        // 1) 尝试 headless 浏览器（Edge / Chrome）
        var browser = FindHeadlessBrowser();
        if (browser != null)
        {
            try
            {
                if (await TryHeadlessPrintToPdfAsync(browser, tempHtml, pdfPath))
                {
                    Log.Information("PDF 导出成功（headless）: {FilePath}", pdfPath);
                    return;
                }
                Log.Warning("Headless 导出未生成 PDF 文件，回退到浏览器手动打印");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Headless PDF 导出失败，回退到浏览器手动打印");
            }
        }
        else
        {
            Log.Warning("未找到 Edge 或 Chrome，回退到浏览器手动打印");
        }

        // 2) 兜底：打开默认浏览器，让用户 Ctrl+P 打印为 PDF
        OpenInBrowserForManualPrint(tempHtml);
    }

    public Task<string> GenerateMarkdownAsync(BlueprintCard blueprint)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("# 商业超体 · 终极商业蓝图");
        sb.AppendLine();
        sb.AppendLine($"生成时间：{DateTime.Now:yyyy年MM月dd日 HH:mm}");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(blueprint.FullContent))
        {
            sb.AppendLine("## 完整商业蓝图");
            sb.AppendLine();
            sb.AppendLine(blueprint.FullContent);
            sb.AppendLine();
        }
        else
        {
            if (blueprint.SuperSignature != null)
            {
                sb.AppendLine("## 一句话超级签名");
                sb.AppendLine();
                sb.AppendLine($"**{blueprint.SuperSignature}**");
                sb.AppendLine();
            }

            if (blueprint.TrustBuildingSop != null)
            {
                sb.AppendLine("## 信任构建SOP");
                sb.AppendLine();

                if (blueprint.TrustBuildingSop.CaseStudies.Count > 0)
                {
                    sb.AppendLine("### 成功案例");
                    foreach (var c in blueprint.TrustBuildingSop.CaseStudies)
                    {
                        sb.AppendLine($"- {c}");
                    }
                    sb.AppendLine();
                }

                if (blueprint.TrustBuildingSop.Qualifications.Count > 0)
                {
                    sb.AppendLine("### 资质背书");
                    foreach (var q in blueprint.TrustBuildingSop.Qualifications)
                    {
                        sb.AppendLine($"- {q}");
                    }
                    sb.AppendLine();
                }

                if (blueprint.TrustBuildingSop.SocialProofs.Count > 0)
                {
                    sb.AppendLine("### 社交证明");
                    foreach (var s in blueprint.TrustBuildingSop.SocialProofs)
                    {
                        sb.AppendLine($"- {s}");
                    }
                    sb.AppendLine();
                }
            }

            if (blueprint.DeliveryMode != null)
            {
                sb.AppendLine("## 交付模式建议");
                sb.AppendLine();
                sb.AppendLine($"**模式名称：** {blueprint.DeliveryMode.Name}");
                sb.AppendLine();
                sb.AppendLine($"**模式描述：** {blueprint.DeliveryMode.Description}");
                sb.AppendLine();
                sb.AppendLine($"**服务类型：** {(blueprint.DeliveryMode.IsHighTouch ? "高客单陪伴式" : "轻量级自动化")}");
                sb.AppendLine();
            }
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("*本蓝图由【商业超体：价值与定位引擎】生成*");

        return Task.FromResult(sb.ToString());
    }

    /// <summary>
    /// 查找可用于 headless 打印的浏览器（Edge 优先，回退 Chrome）。
    /// </summary>
    private static string? FindHeadlessBrowser()
    {
        var candidates = new[]
        {
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
        };
        foreach (var p in candidates)
        {
            if (File.Exists(p)) return p;
        }
        // 在 PATH 中查找
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                foreach (var name in new[] { "msedge.exe", "chrome.exe" })
                {
                    var full = Path.Combine(dir, name);
                    if (File.Exists(full)) return full;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// 通过 headless Edge/Chrome 的 --print-to-pdf 命令生成 PDF。
    /// </summary>
    private static async Task<bool> TryHeadlessPrintToPdfAsync(string browserPath, string htmlPath, string pdfPath)
    {
        var fileUrl = new Uri(htmlPath).AbsoluteUri;
        var args = $"--headless --disable-gpu --no-sandbox --print-to-pdf={QuoteArg(pdfPath)} {QuoteArg(fileUrl)}";

        var psi = new ProcessStartInfo
        {
            FileName = browserPath,
            Arguments = args,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var proc = Process.Start(psi);
        if (proc == null) return false;

        var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await proc.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(true); } catch { }
            return false;
        }

        return File.Exists(pdfPath) && new FileInfo(pdfPath).Length > 1024;
    }

    private static string QuoteArg(string arg) => "\"" + arg.Replace("\"", "\\\"") + "\"";

    /// <summary>
    /// 兜底：在默认浏览器中打开 HTML，让用户手动 Ctrl+P 打印为 PDF。
    /// </summary>
    private static void OpenInBrowserForManualPrint(string htmlPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = htmlPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "打开浏览器失败");
        }

        Application.Current.Dispatcher.Invoke(() =>
        {
            MessageBox.Show(
                "未能自动生成 PDF。\n\n" +
                "已在默认浏览器中打开 HTML 文件，请：\n" +
                "1. 在浏览器中按 Ctrl+P\n" +
                "2. 目标打印机选择「Microsoft Print to PDF」\n" +
                "3. 保存为 PDF 文件",
                "PDF 导出提示",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        });
    }

    private static string GenerateHtmlForPdf(string markdown)
    {
        var htmlContent = ConvertMarkdownToHtml(markdown);

        return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""UTF-8"">
    <title>商业蓝图</title>
    <style>
        @page {{
            size: A4;
            margin: 2cm;
        }}
        body {{
            font-family: 'Microsoft YaHei', 'PingFang SC', sans-serif;
            max-width: 900px;
            margin: 0 auto;
            padding: 20px;
            line-height: 1.8;
            color: #333;
            background: #fff;
        }}
        h1 {{
            color: #1a1a2e;
            border-bottom: 3px solid #e94560;
            padding-bottom: 15px;
            page-break-after: avoid;
        }}
        h2 {{
            color: #16213e;
            margin-top: 35px;
            border-left: 4px solid #e94560;
            padding-left: 15px;
            page-break-after: avoid;
        }}
        h3 {{
            color: #0f3460;
            margin-top: 25px;
            page-break-after: avoid;
        }}
        p {{
            margin: 12px 0;
        }}
        strong {{
            color: #e94560;
        }}
        hr {{
            border: none;
            border-top: 1px solid #ddd;
            margin: 30px 0;
            page-break-after: avoid;
        }}
        ul, ol {{
            padding-left: 25px;
        }}
        li {{
            margin: 10px 0;
            page-break-inside: avoid;
        }}
        blockquote {{
            border-left: 4px solid #4CAF50;
            padding-left: 15px;
            margin: 15px 0;
            color: #555;
            background: #f5f5f5;
            padding: 10px;
        }}
        code {{
            background: #f0f0f0;
            padding: 2px 6px;
            border-radius: 3px;
        }}
        pre {{
            background: #f5f5f5;
            padding: 15px;
            border-radius: 8px;
            overflow-x: auto;
            page-break-inside: avoid;
        }}
        table {{
            border-collapse: collapse;
            width: 100%;
            page-break-inside: avoid;
        }}
        th, td {{
            border: 1px solid #ddd;
            padding: 8px;
            text-align: left;
        }}
        th {{
            background-color: #f5f5f5;
        }}
        .footer {{
            text-align: center;
            color: #999;
            margin-top: 50px;
            font-size: 12px;
        }}
        @media print {{
            body {{ padding: 0; }}
        }}
    </style>
</head>
<body>
    {htmlContent}
    <div class=""footer"">由【商业超体：价值与定位引擎】生成</div>
</body>
</html>";
    }

    private static string ConvertMarkdownToHtml(string markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return "";

        var lines = markdown.Split('\n');
        var result = new System.Text.StringBuilder();
        var inCodeBlock = false;
        var inList = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("```"))
            {
                if (inCodeBlock)
                {
                    result.AppendLine("</code></pre>");
                }
                else
                {
                    result.Append("<pre><code>");
                }
                inCodeBlock = !inCodeBlock;
                continue;
            }

            if (inCodeBlock)
            {
                result.Append(System.Web.HttpUtility.HtmlEncode(line));
                result.AppendLine();
                continue;
            }

            if (string.IsNullOrEmpty(trimmed))
            {
                if (inList)
                {
                    result.AppendLine("</ul>");
                    inList = false;
                }
                result.AppendLine("<br/>");
                continue;
            }

            if (trimmed.StartsWith("# "))
            {
                result.AppendLine($"<h1>{ProcessInlineMarkdown(trimmed.Substring(2))}</h1>");
            }
            else if (trimmed.StartsWith("## "))
            {
                result.AppendLine($"<h2>{ProcessInlineMarkdown(trimmed.Substring(3))}</h2>");
            }
            else if (trimmed.StartsWith("### "))
            {
                result.AppendLine($"<h3>{ProcessInlineMarkdown(trimmed.Substring(4))}</h3>");
            }
            else if (trimmed.StartsWith("> "))
            {
                result.AppendLine($"<blockquote>{ProcessInlineMarkdown(trimmed.Substring(2))}</blockquote>");
            }
            else if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
            {
                if (!inList)
                {
                    result.Append("<ul>");
                    inList = true;
                }
                result.AppendLine($"<li>{ProcessInlineMarkdown(trimmed.Substring(2))}</li>");
            }
            else
            {
                if (inList)
                {
                    result.AppendLine("</ul>");
                    inList = false;
                }
                result.AppendLine($"<p>{ProcessInlineMarkdown(trimmed)}</p>");
            }
        }

        if (inList)
        {
            result.AppendLine("</ul>");
        }

        return result.ToString();
    }

    private static string ProcessInlineMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var result = System.Web.HttpUtility.HtmlEncode(text);

        result = System.Text.RegularExpressions.Regex.Replace(result, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\*(.+?)\*", "<em>$1</em>");
        result = System.Text.RegularExpressions.Regex.Replace(result, @"`(.+?)`", "<code>$1</code>");

        return result;
    }
}
