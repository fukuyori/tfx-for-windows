using System.IO;
using System.Windows;

namespace Tfx;

public partial class MainWindow
{
    private async void UpdatePreview(FileItem? item)
    {
        var cts = ReplacePreviewToken();
        ImagePreview.Visibility = Visibility.Collapsed;
        TextPreview.Visibility = Visibility.Collapsed;
        TextPreview.Text = "";
        ImagePreview.Source = null;

        if (item is null)
        {
            InfoPreview.Text = "";
            return;
        }

        InfoPreview.Text = $"{item.Name}\n{item.FullPath}\n{item.Kind}\n{item.SizeText}\n{Loc.F("Modified: {0}", item.ModifiedText)}\n{Loc.F("Created: {0}", item.CreatedText)}\n{Loc.F("Owner: {0}", item.OwnerText)}\n{Loc.F("Attributes: {0}", item.AttributeText)}";

        if (item.IsDirectory || item.IsParent || !File.Exists(item.FullPath))
        {
            return;
        }

        try
        {
            var path = item.FullPath;
            var preview = await Task.Run(() => PreviewLoader.Load(path, cts.Token), cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            if (!string.IsNullOrEmpty(preview.ExtraInfo))
            {
                InfoPreview.Text += $"\n{preview.ExtraInfo}";
            }

            if (preview.Kind == PreviewKind.Image && preview.Image is not null)
            {
                ImagePreview.Source = preview.Image;
                ImagePreview.Visibility = Visibility.Visible;
            }
            else if (preview.Kind == PreviewKind.Text)
            {
                TextPreview.Text = preview.Text ?? "";
                TextPreview.Visibility = Visibility.Visible;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            InfoPreview.Text += $"\n{Loc.F("Preview error: {0}", ex.Message)}";
        }
    }

    private CancellationTokenSource ReplacePreviewToken()
    {
        var next = new CancellationTokenSource();
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = next;
        return next;
    }

    private void SetPreviewVisible(bool visible)
    {
        PreviewSplitterColumn.Width = visible ? new GridLength(5) : new GridLength(0);
        PreviewColumn.MinWidth = visible ? 240 : 0;
        var previewWidth = _settings.PreviewWidth >= 240 ? _settings.PreviewWidth : 320;
        PreviewColumn.Width = visible ? new GridLength(previewWidth) : new GridLength(0);
        PreviewHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        PreviewButton.IsChecked = visible;
    }

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        SetPreviewVisible(PreviewColumn.Width.Value == 0);
        SaveSettings();
    }
}
