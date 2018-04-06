using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;
using Windows.Media.Core;
using Windows.Media.Devices;
using Windows.Media.Playback;
using Windows.Media.Render;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Security.Cryptography;
using Windows.Storage;
using Windows.Storage.BulkAccess;
using Windows.Storage.Search;
using Windows.Storage.Streams;
using Windows.System.Profile;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
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

        //private Timer timer;
        private StreamSocketListener listener;

        IReadOnlyList<FileInformation> fil;

        private string systemId;
        public string SystemId
        {
            get
            {
                if(systemId == null)
                {
                    systemId = BitConverter.ToString(SystemIdentification.GetSystemIdForPublisher().Id.ToArray()).Replace("-", "");
                }

                return systemId;
            }
        }

        public string TimestampFormat { get; } = "yyyy-MM-dd HH:mm:ss.ffff";

        public MainPage()
        {
            this.InitializeComponent();
        }

        private async Task<JsonObject> GetCurrentStatus()
        {
            var myIp = await GetCurrentHostName();
            var jsonStatus = new JsonObject
            {
                new KeyValuePair<string, IJsonValue>("SystemId", JsonValue.CreateStringValue(SystemId)),
                new KeyValuePair<string, IJsonValue>("IPAddress", JsonValue.CreateStringValue(myIp?.ToString() ?? string.Empty)),
                new KeyValuePair<string, IJsonValue>("mediaPlayer.PlaybackSession.PlaybackState", JsonValue.CreateStringValue(mediaPlayer?.PlaybackSession.PlaybackState.ToString() ?? string.Empty)),
                new KeyValuePair<string, IJsonValue>("mediaPlayer.Source", JsonValue.CreateStringValue((mediaPlayer?.Source as MediaSource)?.Uri.ToString() ?? string.Empty)),
                new KeyValuePair<string, IJsonValue>("ResponseTimestamp", JsonValue.CreateStringValue(DateTime.Now.ToString(TimestampFormat)))
            };
            return jsonStatus;
        }

        private string GetHTTPResponse(JsonObject response)
        {
            string msg = response.Stringify();

            return "HTTP/1.1 200 OK\r\n" +
                    "Content-Type: text/html\r\n" +
                    "Date: " + DateTime.Now.ToString("R") + "\r\n" +
                    "Server: IotPlayMusic/1.0\r\n" +
                    "Content-Length: " + msg.Length + "\r\n" +
                    "Connection: close\r\n\r\n" + 
                    msg;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            //await ScanFiles();

            await CreateMediaPlayer();

            //PlayMusic();

            //await CreateAudioGraph();
            //await PlayMyMusic();

            listener = new StreamSocketListener();
            await listener.BindServiceNameAsync("8000");
            listener.ConnectionReceived += Listener_ConnectionReceived;

            var timer = new System.Timers.Timer(10000);
            timer.Elapsed += Timer_SendStatusBeacon;
            timer.AutoReset = true;
            timer.Enabled = true;

            //timer = new Timer(SendStatusBeacon, null, TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(10));

            //await AzureIoTHub.RegisterDirectMethodsAsync();

            //ListenToIot();
        }

        private async void Timer_SendStatusBeacon(object sender, System.Timers.ElapsedEventArgs e)
        {
            using (var ds = new DatagramSocket())
            {
                using (var opS = new DataWriter(await ds.GetOutputStreamAsync(new HostName("255.255.255.255"), "8377")))
                {
                    opS.WriteBuffer(Encoding.UTF8.GetBytes((await GetCurrentStatus()).Stringify()).AsBuffer());
                    await opS.StoreAsync();

                    //Debug.WriteLine(jsonStatus.Stringify());
                    await opS.FlushAsync();
                    opS.DetachStream();
                }
            }
        }

        

        private async Task<IPAddress> GetCurrentHostName()
        {
            if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
            {
                return null;
            }

            var hosts = await Dns.GetHostEntryAsync(Dns.GetHostName());

            return hosts.AddressList.FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
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
            var requestTime = DateTime.Now.ToString(TimestampFormat);

            StringBuilder request = new StringBuilder();

            using (var input = new DataReader(args.Socket.InputStream))
            {
                input.InputStreamOptions = InputStreamOptions.Partial;

                uint dataRead = 8192;
                while (dataRead == 8192)
                {
                    var loaded = await input.LoadAsync(8192);
                    var inbuf = input.ReadBuffer(loaded);
                    request.Append(Encoding.UTF8.GetString(inbuf.ToArray(), 0, inbuf.ToArray().Length));
                    dataRead = inbuf.Length;
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

            var response = await GetCurrentStatus();

            response.Add(new KeyValuePair<string, IJsonValue>("RequestTimestamp", JsonValue.CreateStringValue(requestTime)));

            string httpResponse = GetHTTPResponse(response);

            Debug.WriteLine(httpResponse);
            //var buf = Encoding.UTF8.GetBytes("OK").AsBuffer();

            var buf = Encoding.UTF8.GetBytes(httpResponse).AsBuffer();

            using (var output = new DataWriter(args.Socket.OutputStream))
            {
                output.WriteBuffer(buf);
                await output.StoreAsync();
                output.DetachStream();

                //await output.WriteAsync(buf);

                //await output.FlushAsync();
            }

            //args.Socket.Dispose();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            // Destroy the graph if the page is naviated away from

            //graph?.Dispose();

            //mediaPlayer?.Dispose();

            //listener?.Dispose();
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
