using MaterialDesignThemes.Wpf;
using Mixer.Base;
using Mixer.Base.Model.Channel;
using Mixer.Base.Model.Clips;
using Mixer.Base.Model.User;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace MixerClipsDownloader
{
    public class ClipListing : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public ClipModel Clip { get; set; }
        public UserModel User { get; set; }

        public bool Selected
        {
            get { return this.selected; }
            set
            {
                this.selected = value;
                this.NotifyPropertyChanged("Selected");
            }
        }
        private bool selected;

        public string Name { get { return this.Clip.title; } }

        public string Link { get { return string.Format("https://mixer.com/{0}?clip={1}", this.User.username, this.Clip.shareableId); } }

        public string DateTime { get { return this.Clip.uploadDate.ToLocalTime().ToString("g"); } }

        public ClipListing(ClipModel clip, UserModel user)
        {
            this.Clip = clip;
            this.User = user;
        }

        public void NotifyPropertyChanged(string propertyName)
        {
            if (this.PropertyChanged != null)
            {
                this.PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const string VideoFileContentLocatorType = "HlsStreaming";

        private const string FFMPEGDownloadLink = "https://ffmpeg.zeranoe.com/builds/win32/static/ffmpeg-4.0-win32-static.zip";

        private const string FFMPEGExecutablePath = "ffmpeg-4.0-win32-static\\bin\\ffmpeg.exe";

        private MixerConnection connection;
        private ExpandedChannelModel channel;
        private PrivatePopulatedUserModel user;

        private ObservableCollection<ClipListing> clips = new ObservableCollection<ClipListing>();

        private List<OAuthClientScopeEnum> scopes = new List<OAuthClientScopeEnum>()
        {
            OAuthClientScopeEnum.channel__details__self,

            OAuthClientScopeEnum.user__details__self,
        };

        public MainWindow()
        {
            this.Loaded += MainWindow_Loaded;

            InitializeComponent();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (File.Exists(FFMPEGExecutablePath))
            {
                this.DownloadFFMPEGButton.Visibility = Visibility.Collapsed;
                this.FFMPEGAlreadyDownloadedTextBlock.Visibility = Visibility.Visible;
            }

            this.ClipsDataGrid.ItemsSource = this.clips;
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            await this.LoadingOperation(async () =>
            {
                this.connection = await MixerConnection.ConnectViaLocalhostOAuthBrowser(ConfigurationManager.AppSettings["ClientID"], scopes);
                if (this.connection != null)
                {
                    try
                    {
                        this.user = await this.connection.Users.GetCurrentUser();
                        this.channel = await this.connection.Channels.GetChannel(this.user.username);

                        var clips = await this.connection.Clips.GetChannelClips(this.channel);
                        foreach (ClipModel clip in clips.OrderBy(c => c.uploadDate))
                        {
                            this.clips.Add(new ClipListing(clip, this.user));
                        }

                        this.LoginGrid.Visibility = Visibility.Collapsed;
                        this.MainGrid.Visibility = Visibility.Visible;
                    }
                    catch (Exception)
                    {
                        await this.ShowMessageDialog("Unable to get your user information from Mixer, please try again");
                    }
                }
                else
                {
                    await this.ShowMessageDialog("Unable to log in to Mixer, please ensure you approve in a timely manner");
                }
            });
        }

        private async void DownloadFFMPEGButton_Click(object sender, RoutedEventArgs e)
        {
            await this.LoadingOperation(async () =>
            {
                string zipFilePath = Path.Combine(this.GetApplicationDirectory(), "ffmpeg.zip");

                await Task.Run(() =>
                {
                    using (WebClient client = new WebClient())
                    {
                        client.DownloadFile(new Uri(FFMPEGDownloadLink), zipFilePath);
                    }

                    try
                    {
                        if (File.Exists(zipFilePath))
                        {
                            using (FileStream stream = File.OpenRead(zipFilePath))
                            {
                                ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read);
                                foreach (ZipArchiveEntry file in archive.Entries)
                                {
                                    string fullPath = Path.Combine(this.GetApplicationDirectory(), file.FullName);
                                    if (string.IsNullOrEmpty(file.Name))
                                    {
                                        string directoryPath = Path.GetDirectoryName(fullPath);
                                        if (!Directory.Exists(directoryPath))
                                        {
                                            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                                        }
                                    }
                                    else
                                    {
                                        file.ExtractToFile(fullPath, true);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception) { }
                });

                if (!File.Exists(FFMPEGExecutablePath))
                {
                    await this.ShowMessageDialog("Unable to download FFMPEG, please try again");
                }
                else
                {
                    this.DownloadFFMPEGButton.Visibility = Visibility.Collapsed;
                    this.FFMPEGAlreadyDownloadedTextBlock.Visibility = Visibility.Visible;
                }
            });
        }

        private void SelectAllCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            CheckBox checkBox = (CheckBox)sender;
            foreach (ClipListing clip in this.clips)
            {
                clip.Selected = checkBox.IsChecked.GetValueOrDefault();
            }
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Process.Start(e.Uri.AbsoluteUri);
            e.Handled = true;
        }

        private async void DownloadClipsButton_Click(object sender, RoutedEventArgs e)
        {
            await this.LoadingOperation(async () =>
            {
                if (!File.Exists(FFMPEGExecutablePath))
                {
                    await this.ShowMessageDialog("FFMPEG must be downloaded");
                    return;
                }

                if (this.clips.All(c => !c.Selected))
                {
                    await this.ShowMessageDialog("At least 1 clip must be selected");
                    return;
                }

                string folderPath = null;
                using (var folderDialog = new System.Windows.Forms.FolderBrowserDialog())
                {
                    if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        folderPath = folderDialog.SelectedPath;
                    }
                }

                if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                {
                    await this.ShowMessageDialog("A valid directory must be selected");
                    return;
                }

                DownloadStatusDialogControl downloadStatus = new DownloadStatusDialogControl();
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                this.ShowDialog(downloadStatus);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

                foreach (ClipListing clip in this.clips.Where(c => c.Selected))
                {
                    ClipLocatorModel clipLocator = clip.Clip.contentLocators.FirstOrDefault(cl => cl.locatorType.Equals(VideoFileContentLocatorType));
                    if (clipLocator != null)
                    {
                        downloadStatus.SetCurrentDownload(clip.Name);

                        char[] invalidChars = Path.GetInvalidFileNameChars();
                        string fileName = new string(clip.Name.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
                        string destinationFile = Path.Combine(folderPath, fileName + ".mp4");

                        Process process = new Process();
                        process.StartInfo.FileName = FFMPEGExecutablePath;
                        process.StartInfo.Arguments = string.Format("-i {0} -c copy -bsf:a aac_adtstoasc \"{1}\"", clipLocator.uri, destinationFile);
                        process.StartInfo.RedirectStandardOutput = true;
                        process.StartInfo.UseShellExecute = false;
                        process.StartInfo.CreateNoWindow = true;

                        process.Start();
                        while (!process.HasExited)
                        {
                            await Task.Delay(500);
                        }
                    }
                }

                this.HideDialog();

                await this.ShowMessageDialog("Clip downloading has finished. Please verify all clips have been successfully downloaded to the folder you selected and re-download any that did not.");
            });
        }

        private async Task LoadingOperation(Func<Task> function)
        {
            this.StatusBar.Visibility = Visibility.Visible;
            this.IsEnabled = false;

            await function();

            this.IsEnabled = true;
            this.StatusBar.Visibility = Visibility.Collapsed;
        }

        private async Task ShowMessageDialog(string message)
        {
            await this.ShowDialog(new MessageDialogControl(message));
        }

        private async Task ShowDialog(UserControl control)
        {
            IEnumerable<Window> windows = Application.Current.Windows.OfType<Window>();
            if (windows.Count() > 0)
            {
                object obj = windows.FirstOrDefault().FindName("MDDialogHost");
                if (obj != null)
                {
                    DialogHost dialogHost = (DialogHost)obj;
                    await dialogHost.ShowDialog(control);
                }
            }
        }

        private void HideDialog()
        {
            IEnumerable<Window> windows = Application.Current.Windows.OfType<Window>();
            if (windows.Count() > 0)
            {
                object obj = windows.FirstOrDefault().FindName("MDDialogHost");
                if (obj != null)
                {
                    DialogHost dialogHost = (DialogHost)obj;
                    dialogHost.IsOpen = false;
                }
            }
        }

        private string GetApplicationDirectory() { return Path.GetDirectoryName(Assembly.GetEntryAssembly().Location); }
    }
}
