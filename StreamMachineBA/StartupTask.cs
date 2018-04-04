using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Http;
using Windows.ApplicationModel.Background;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Diagnostics;
using Windows.Media.Core;
using Windows.Media.Playback;
using System.Threading.Tasks;
using Windows.Networking.Sockets;
using Windows.Devices.Enumeration;
using Windows.Media.Devices;
using System.Net;
using Windows.Networking;
using System.Threading;

// The Background Application template is documented at http://go.microsoft.com/fwlink/?LinkID=533884&clcid=0x409

namespace StreamMachineBA
{
    public sealed class StartupTask : IBackgroundTask
    {
        private MediaPlayer mediaPlayer;
        private Timer timer;

        public async void Run(IBackgroundTaskInstance taskInstance)
        {
            // 
            // TODO: Insert code to perform background work
            //
            // If you start any asynchronous methods here, prevent the task
            // from closing prematurely by using BackgroundTaskDeferral as
            // described in http://aka.ms/backgroundtaskdeferral
            //

            //
            // Create the deferral by requesting it from the task instance.
            //
            BackgroundTaskDeferral deferral = taskInstance.GetDeferral();

            //
            // Call asynchronous method(s) using the await keyword.
            //
            //var result = await ExampleMethodAsync();

            await CreateMediaPlayer();

            var listener = new StreamSocketListener();
            await listener.BindServiceNameAsync("8000");
            listener.ConnectionReceived += Listener_ConnectionReceived;

            timer = new Timer(SendStatusBeacon, null, TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(10));

            //
            // Once the asynchronous method(s) are done, close the deferral.
            //
            //deferral.Complete();
        }

        private async void SendStatusBeacon(object status)
        {
            var ds = new DatagramSocket();

            using (var opS = await ds.GetOutputStreamAsync(new HostName("255.255.255.255"), "8377"))
            {
                var myIp = await GetCurrentHostName();

                if (myIp != null)
                {
                    await opS.WriteAsync(Encoding.UTF8.GetBytes(myIp.ToString()).AsBuffer());
                    Debug.WriteLine(myIp.ToString());
                }
                await opS.FlushAsync();
            }
        }

        private async Task<IPAddress> GetCurrentHostName()
        {
            if(!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
            {
                return null;
            }

            var hosts = await Dns.GetHostEntryAsync(Dns.GetHostName());

            return hosts.AddressList.FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
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
    }
}
