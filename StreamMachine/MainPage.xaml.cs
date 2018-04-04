using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media.Audio;
using Windows.Media.Core;
using Windows.Media.Devices;
using Windows.Media.Playback;
using Windows.Media.Render;
using Windows.Networking.Sockets;
using Windows.Storage;
using Windows.Storage.BulkAccess;
using Windows.Storage.Search;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;


// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace StreamMachine
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private AudioGraph graph;
        private AudioFileInputNode fileInput;
        private AudioDeviceOutputNode deviceOutput;

        private MediaPlayer mediaPlayer;

        IReadOnlyList<FileInformation> fil;

        public MainPage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            //await ScanFiles();

            await CreateMediaPlayer();

            //PlayMusic();

            //await CreateAudioGraph();
            //await PlayMyMusic();

            var listener = new StreamSocketListener();
            await listener.BindServiceNameAsync("8000");
            listener.ConnectionReceived += Listener_ConnectionReceived;

            //

            await AzureIoTHub.RegisterDirectMethodsAsync();



            //ListenToIot();
        }

        private async Task ListenToIot()
        {
            string msg = string.Empty;

            while (true)
            {
                try
                {
                    msg = await AzureIoTHub.ReceiveCloudToDeviceMessageAsync();
                    //await AzureIoTHub.SendDeviceToCloudMessageAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }

                if (msg.Contains("play"))
                {
                    PlayMusic();
                }
                else if (msg.Contains("stop"))
                {
                    StopMusic();
                }

                Debug.WriteLine(msg);
            }
        }

        private async void Listener_ConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
        {
            StringBuilder request = new StringBuilder();

            using (IInputStream input = args.Socket.InputStream)
            {
                byte[] data = new byte[8192];
                IBuffer buffer = data.AsBuffer();
                uint dataRead = 8192;
                while (dataRead == 8192)
                {
                    await input.ReadAsync(buffer, 8192, InputStreamOptions.Partial);
                    request.Append(Encoding.UTF8.GetString(data, 0, data.Length));
                    dataRead = buffer.Length;
                }


            }

            var req = request.ToString();

            if (req.ToLower().Contains("play"))
            {
                PlayMusic();
            }
            else if (req.ToLower().Contains("stop"))
            {
                StopMusic();
            }

            Debug.WriteLine(request.ToString());

            string header = string.Format("HTTP/1.1 200 OK\r\n" +
                              "Content-Type: text/html\r\n" +
                              "Date: " + DateTime.Now.ToString("R") + "\r\n" +
                              "Server: IotPlayMusic/1.0\r\n" +
                              "Content-Length: 2\r\n" +
                              "Connection: close\r\n\r\n" +
                              "OK");

            Debug.WriteLine(header);
            //var buf = Encoding.UTF8.GetBytes("OK").AsBuffer();

            var buf = Encoding.UTF8.GetBytes(header).AsBuffer();

            using (IOutputStream output = args.Socket.OutputStream)
            {
                await output.WriteAsync(buf);

                await output.FlushAsync();
            }

        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            // Destroy the graph if the page is naviated away from

            if (graph != null)
            {
                graph.Dispose();
            }
        }

        private void MediaPlayerElement_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                //Uri pathUri = new Uri("ms-appx:///video-1448567804.mp4");
                //mediaPlayer.Source = MediaSource.CreateFromUri(pathUri);
            }
            catch (Exception ex)
            {
                if (ex is FormatException)
                {
                    // handle exception.
                    // For example: Log error or notify user problem with file
                }
            }

        }

        private async Task CreateMediaPlayer()
        {
            mediaPlayer = new MediaPlayer();

            mediaPlayer.AudioCategory = MediaPlayerAudioCategory.Media;

            var outputDevices = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector());

            foreach (var device in outputDevices)
            {
                Debug.WriteLine(device.Name);

                if (device.Name.Contains("Halide"))
                {
                    mediaPlayer.AudioDevice = device;
                }
            }
        }

        private void PlayMusic()
        {
            //var file = await StorageFile.GetFileFromPathAsync(fil[0].Path);

            //mediaPlayer.Source = MediaSource.CreateFromStorageFile(file);

            var uri = new Uri(@"http://http-live.sr.se/p2musik-mp3-192");
            //var uri = new Uri(@"http://sverigesradio.se/topsy/direkt/2562-hi-mp3.m3u");

            mediaPlayer.Source = MediaSource.CreateFromUri(uri);

            mediaPlayer.Play();
        }

        private void StopMusic()
        {
            mediaPlayer.Pause();
            mediaPlayer.Source = null;
        }

        private async Task ScanFiles()
        {

            var fileTypeFilter = new List<string>
            {
                ".mp3",
                ".flac"
            };
            var queryOptions = new QueryOptions(CommonFileQuery.OrderByName, fileTypeFilter)
            {
                FolderDepth = FolderDepth.Deep,
                IndexerOption = IndexerOption.DoNotUseIndexer
            };

            // Create query and retrieve files
            var query = KnownFolders.MusicLibrary.CreateFileQueryWithOptions(queryOptions);
            //IReadOnlyList<StorageFile> fileList = await query.GetFilesAsync();

            var fif = new FileInformationFactory(query, Windows.Storage.FileProperties.ThumbnailMode.MusicView);

            fil = await fif.GetFilesAsync();

            Debug.WriteLine("Count: " + fil.Count);

            foreach (var fi in fil)
            {
                Debug.WriteLine(fi.Path);

            }
        }

        private async Task PlayMyMusic()
        {
            var file = await StorageFile.GetFileFromPathAsync(fil[0].Path);
            //var file = await KnownFolders.MusicLibrary.GetFileAsync("smalltest.flac");

            CreateAudioFileInputNodeResult fileInputResult = await graph.CreateFileInputNodeAsync(file);

            if (fileInputResult.Status != AudioFileNodeCreationStatus.Success)
            {
                // Cannot create graph
                //rootPage.NotifyUser(String.Format("AudioGraph Creation Error because {0}", result.Status.ToString()), NotifyType.ErrorMessage);
                return;
            }

            fileInput = fileInputResult.FileInputNode;
            fileInput.AddOutgoingConnection(deviceOutput);
            //fileInput.LoopCount = 10;
            graph.Start();
        }

        private async Task CreateAudioGraph()
        {
            // Create an AudioGraph with default settings
            AudioGraphSettings settings = new AudioGraphSettings(AudioRenderCategory.Media)
            {
                //CreateAudioGraphResult result = await AudioGraph.CreateAsync(settings);

                QuantumSizeSelectionMode = QuantumSizeSelectionMode.LowestLatency
            };
            var outputDevices = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector());

            foreach (var device in outputDevices)
            {
                Debug.WriteLine(device.Name);

                if (device.Name.Contains("Halide"))
                {
                    settings.PrimaryRenderDevice = device;
                }
            }

            //settings.PrimaryRenderDevice = outputDevices[];

            CreateAudioGraphResult result = await AudioGraph.CreateAsync(settings);

            if (result.Status != AudioGraphCreationStatus.Success)
            {
                // Cannot create graph
                //rootPage.NotifyUser(String.Format("AudioGraph Creation Error because {0}", result.Status.ToString()), NotifyType.ErrorMessage);
                return;
            }

            graph = result.Graph;

            // Create a device output node
            CreateAudioDeviceOutputNodeResult deviceOutputNodeResult = await graph.CreateDeviceOutputNodeAsync();
            if (deviceOutputNodeResult.Status != AudioDeviceNodeCreationStatus.Success)
            {
                // Cannot create device output node
                //rootPage.NotifyUser(String.Format("Device Output unavailable because {0}", deviceOutputNodeResult.Status.ToString()), NotifyType.ErrorMessage);
                //speakerContainer.Background = new SolidColorBrush(Colors.Red);
                return;
            }

            deviceOutput = deviceOutputNodeResult.DeviceOutputNode;
            //rootPage.NotifyUser("Device Output Node successfully created", NotifyType.StatusMessage);
            //speakerContainer.Background = new SolidColorBrush(Colors.Green);

        }
    }
}
