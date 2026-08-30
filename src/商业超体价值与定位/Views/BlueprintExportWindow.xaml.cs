using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using 商业超体价值与定位.Models;
using 商业超体价值与定位.Services;

namespace 商业超体价值与定位.Views;

public partial class BlueprintExportWindow : Window
{
    private readonly BlueprintCard _blueprint;
    private readonly IExportService _exportService;

    public BlueprintExportWindow(BlueprintCard blueprint)
    {
        InitializeComponent();
        _blueprint = blueprint;
        _exportService = App.ServiceProvider.GetRequiredService<IExportService>();
        LoadBlueprintData();
    }

    private void LoadBlueprintData()
    {
        if (_blueprint.SuperSignature != null)
        {
            SuperSignatureText.Text = _blueprint.SuperSignature.ToString();
        }

        if (_blueprint.TrustBuildingSop != null)
        {
            CaseStudiesControl.ItemsSource = _blueprint.TrustBuildingSop.CaseStudies;
            QualificationsControl.ItemsSource = _blueprint.TrustBuildingSop.Qualifications;
            SocialProofsControl.ItemsSource = _blueprint.TrustBuildingSop.SocialProofs;
        }

        if (_blueprint.DeliveryMode != null)
        {
            DeliveryModeName.Text = _blueprint.DeliveryMode.Name;
            DeliveryModeType.Text = _blueprint.DeliveryMode.IsHighTouch
                ? "高客单陪伴式"
                : "轻量级自动化";
        }

        if (!string.IsNullOrEmpty(_blueprint.FullContent))
        {
            FullContentText.Text = _blueprint.FullContent;
        }
        else if (_blueprint.SuperSignature != null)
        {
            FullContentText.Text = _blueprint.SuperSignature.ToString();
        }

        UpdateTrustSopEmptyState();
    }

    /// <summary>
    /// 当信任构建 SOP 三项均为空时，显示提示与软提取的内容。
    /// </summary>
    private void UpdateTrustSopEmptyState()
    {
        var sop = _blueprint.TrustBuildingSop;
        bool allEmpty = sop == null
            || (sop.CaseStudies.Count == 0
                && sop.Qualifications.Count == 0
                && sop.SocialProofs.Count == 0);

        if (allEmpty)
        {
            TrustSopEmptyHint.Visibility = Visibility.Visible;
            CaseStudiesControl.Visibility = Visibility.Collapsed;
            QualificationsControl.Visibility = Visibility.Collapsed;
            SocialProofsControl.Visibility = Visibility.Collapsed;
            CaseStudiesLabel.Visibility = Visibility.Collapsed;
            QualificationsLabel.Visibility = Visibility.Collapsed;
            SocialProofsLabel.Visibility = Visibility.Collapsed;
        }
        else
        {
            TrustSopEmptyHint.Visibility = Visibility.Collapsed;
            CaseStudiesControl.Visibility = Visibility.Visible;
            QualificationsControl.Visibility = Visibility.Visible;
            SocialProofsControl.Visibility = Visibility.Visible;
            CaseStudiesLabel.Visibility = Visibility.Visible;
            QualificationsLabel.Visibility = Visibility.Visible;
            SocialProofsLabel.Visibility = Visibility.Visible;
        }
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "PDF文档 (*.pdf)|*.pdf|HTML文档 (*.html)|*.html|Markdown文档 (*.md)|*.md",
            FilterIndex = 1,  // 默认选中 PDF
            DefaultExt = ".pdf",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"商业蓝图_{DateTime.Now:yyyyMMdd_HHmmss}",
            Title = "导出商业蓝图"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                await _exportService.ExportBlueprintAsync(_blueprint, dialog.FileName);

                MessageBox.Show(
                    $"商业蓝图已导出至：\n{dialog.FileName}",
                    "导出成功",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{dialog.FileName}\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"导出失败：{ex.Message}",
                    "导出错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private async void ExportMdButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var fileName = $"商业蓝图_{DateTime.Now:yyyyMMdd_HHmmss}.md";
            var filePath = System.IO.Path.Combine(documentsPath, fileName);

            var markdown = await _exportService.GenerateMarkdownAsync(_blueprint);
            await System.IO.File.WriteAllTextAsync(filePath, markdown, System.Text.Encoding.UTF8);

            MessageBox.Show(
                $"Markdown 文件已导出至：\n{filePath}",
                "导出成功",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"导出失败：{ex.Message}",
                "导出错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
