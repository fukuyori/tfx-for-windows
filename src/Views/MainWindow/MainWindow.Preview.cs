using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using Path = System.IO.Path;

namespace Tfx;

public partial class MainWindow
{
    private void UpdatePreview(FileItem? item)
    {
        ImagePreview.Visibility = Visibility.Collapsed;
        TextPreview.Visibility = Visibility.Collapsed;
        TextPreview.Text = "";
        ImagePreview.Source = null;

        if (item is null)
        {
            InfoPreview.Text = "";
            return;
        }

        InfoPreview.Text = $"{item.Name}\n{item.FullPath}\n{item.Kind}\n{item.SizeText}\nModified: {item.ModifiedText}\nCreated: {item.CreatedText}\nOwner: {item.OwnerText}\nAttributes: {item.AttributeText}";

        if (item.IsDirectory || item.IsParent || !File.Exists(item.FullPath))
        {
            return;
        }

        var extension = Path.GetExtension(item.FullPath).ToLowerInvariant();
        if (FsHelpers.IsImage(extension))
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(item.FullPath);
                image.DecodePixelWidth = 900;
                image.EndInit();
                ImagePreview.Source = image;
                ImagePreview.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                InfoPreview.Text += $"\nPreview error: {ex.Message}";
            }
        }
        else if (FsHelpers.IsText(extension, item.FullPath))
        {
            try
            {
                using var stream = File.OpenRead(item.FullPath);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                var buffer = new char[Math.Min(stream.Length, 64 * 1024)];
                var count = reader.Read(buffer, 0, buffer.Length);
                TextPreview.Text = new string(buffer, 0, count);
                TextPreview.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                InfoPreview.Text += $"\nPreview error: {ex.Message}";
            }
        }
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
