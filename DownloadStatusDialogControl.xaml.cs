using System.Windows.Controls;

namespace MixerClipsDownloader
{
    /// <summary>
    /// Interaction logic for DownloadStatusDialogControl.xaml
    /// </summary>
    public partial class DownloadStatusDialogControl : UserControl
    {
        public DownloadStatusDialogControl()
        {
            InitializeComponent();
        }

        public void SetCurrentDownload(string downloadName) { this.CurrentDownloadNameTextBlock.Text = downloadName; }
    }
}
