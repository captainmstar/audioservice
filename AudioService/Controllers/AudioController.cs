using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NAudio.Utils;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Prism.Commands;

namespace AudioService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AudioController : ControllerBase
    {
        private readonly ILogger logger;
        private String IpAddress = "172.162.4.200";
        private int Audioport = 3003;

        private IWaveIn waveIn;
        private WaveFileWriter RecordedAudioWriter = null;
        private MemoryStream memoryStream = null;
        private TcpClient audioClient;
        private NetworkStream AudioStream;
        public DelegateCommand<object> _audiostreamingcommand;
        byte[] buffer = new byte[1024 * 10];

        private Thread recorded { get; set; }

        private bool isAudioStreamingenabled, Isstopaudioenabled, IsPlayPauseenabled, isaudiostreamiggoingon, IsAudioStreamingenabled;

        public event PropertyChangedEventHandler PropertyChanged;

        public AudioController(ILogger<AudioController> logger)
        {
            this.logger = logger;
        }

        [HttpGet]
        public IActionResult Get()
        {
            logger.LogInformation("Calling Get");
            StartAudioStreaming();
            Thread.Sleep(5000);
            PlayAudioStream();
            return new OkObjectResult("Hello World!");
        }

        // Connect to audio tcp using socket on Connect button click
        private void StartAudioStreaming()
        {
            try
            {
                #region stream
                IPAddress ipAddress = IPAddress.Parse(IpAddress.Trim());
                audioClient = new TcpClient(IpAddress, Audioport);

                if (!audioClient.Connected)
                {
                    logger.LogInformation("\r\n--> Not able to connect to Server for audio streaming.");
                    return;
                }
                else
                {
                    logger.LogInformation("\r\n--> Connected to server at - " + IpAddress + ":" + Audioport);
                    AudioStream = audioClient.GetStream();
                    isAudioStreamingenabled = false;
                    Isstopaudioenabled = true;
                    IsPlayPauseenabled = true;

                }
            }
            catch (System.Exception ex)
            {
                logger.LogInformation(ex.Message.ToString());
            }
            #endregion
        }

        private void PlayAudioStream()
        {
            if (!audioClient.Connected)
            {
                logger.LogInformation("\r\n-->Audio TCP client not connected to IVI.");
                return;
            }
            recorded = new Thread(() => OnRecordedStream());
            recorded.Start();
        }

        private void PauseAudioStream()
        {
            if (!audioClient.Connected)
            {
                logger.LogInformation("\r\n-->Audio TCP client not connected to IVI.");
            }
            if (this.waveIn != null)
            {
                this.waveIn.StopRecording();
            }
        }

        private void OnRecordedStream()
        {
            try
            {

                if (this.waveIn == null)
                {
                    logger.LogInformation("\r\n--> Started capturing audio from recorder.");
                    this.waveIn = new WasapiLoopbackCapture();
                    this.waveIn.DataAvailable += new EventHandler<WaveInEventArgs>(this.OnDataAvailable);
                    if (memoryStream == null)
                        memoryStream = new MemoryStream();
                    this.RecordedAudioWriter = new WaveFileWriter(new IgnoreDisposeStream(memoryStream), this.waveIn.WaveFormat);
                    this.waveIn.RecordingStopped += new EventHandler<StoppedEventArgs>(this.OnRecordingStopped);
                    this.waveIn.StartRecording();
                    logger.LogInformation("\r\n--> Started streaming audio to IVI.");
                    isaudiostreamiggoingon = true;

                }
                else
                {
                    logger.LogInformation("\r\n-->Debug message: OnRecordedStream() method called- this.waveIn != null");
                }
            }
            catch (System.Exception ex)
            {
                logger.LogInformation(ex.Message.ToString());
                stopaudiostreaming();
            }
        }

        public DelegateCommand<object> Audiostreamingcommand
        {
            get
            {
                return _audiostreamingcommand;
            }
            set
            {
                _audiostreamingcommand = value;
                //this.OnPropertyChanged("Audiostreamingcommand");
            }
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            try
            {
                if (AudioStream != null && AudioStream.CanWrite)
                {
                    if (e.BytesRecorded > 0)
                    {
                        //------Below code sends the bytes with default audio encoding format used for by recorder-------------
                        //WriteTextIntoUI("\r\n--> Received number of audio bytes from audio card : " + e.BytesRecorded, true);
                        //WriteTextIntoUI("\r\n--> Sending audio data bytes from SC To IVI", true);
                        //AudioStream.Write(e.Buffer, 0, e.BytesRecorded);
                        //WriteTextIntoUI("\r\n--> Number of audio bytes sent to IVI: " + e.BytesRecorded, true);
                        //------------------------------------------------------------------------------------------------------

                        //---------------Below code converts the default audio encoding to PCM 16 bit format--------------------
                        logger.LogInformation("\r\n--> Received number of audio bytes from audio card : " + e.BytesRecorded, true);
                        logger.LogInformation("\r\n--> Sending audio data bytes from SC To IVI", true);
                        // AudioStream.Write(e.Buffer, 0, e.BytesRecorded);
                        byte[] data = ConvertToPCM16Bit(e.Buffer, e.BytesRecorded, this.waveIn.WaveFormat);
                        AudioStream.Write(data, 0, data.Length);
                        isaudiostreamiggoingon = true;
                        logger.LogInformation("\r\n--> Number of audio bytes sent to IVI: " + data.Length, true);
                        //--------------------------------------------------------------------------------------------------------
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogInformation(ex.Message.ToString());
                stopaudiostreaming();
            }
        }

        private void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            if (this.waveIn != null)
            {
                this.waveIn.Dispose();
                this.waveIn = null;
            }
            if (RecordedAudioWriter != null)
            {
                //this.RecordedAudioWriter.Dispose();
                this.RecordedAudioWriter = null;
            }
            isaudiostreamiggoingon = false;
            logger.LogInformation("\r\n--> Audio streaming is paused. ", true);

        }

        private void stopaudiostreaming()
        {
            #region stopstreaming

            if (this.waveIn != null)
            {
                this.waveIn.StopRecording();
            }
            isaudiostreamiggoingon = false;
            onAudioClientDisconnected();
            IsPlayPauseenabled = false;
            logger.LogInformation("\r\n--> TCP connection closed with IVI. ", true);

            #endregion
        }

        /// <summary>
        /// This method convert the Audio encoding to PCM 16 format from the given format
        /// </summary>
        /// <param name="input"></param>
        /// <param name="length"></param>
        /// <param name="format"></param>
        /// <returns></returns>
        private byte[] ConvertToPCM16Bit(byte[] input, int length, WaveFormat format)
        {
            if (length == 0)
                return new byte[0];

            using (var memStream = new MemoryStream(input, 0, length))
            {
                using (var inputStream = new RawSourceWaveStream(memStream, format))
                {
                    //convert bytes to floats for operations.
                    WaveToSampleProvider sampleStream = new WaveToSampleProvider(inputStream);

                    //resample to 48khz
                    var resamplingProvider = new WdlResamplingSampleProvider(sampleStream, 48000);

                    //convert float stream to PCM 16 bit.
                    var ieeeToPCM = new SampleToWaveProvider16(resamplingProvider);
                    return readStream(ieeeToPCM);
                }
            }
        }

        public void onAudioClientDisconnected()
        {
            if (AudioStream != null)
            {
                AudioStream.Close();
                AudioStream = null;
            }
            if (memoryStream != null)
            {
                memoryStream.Dispose();
                memoryStream = null;
            }
            if (audioClient != null)
            {
                audioClient.Close();
                audioClient = null;
            }

            IsAudioStreamingenabled = true;
            Isstopaudioenabled = false;
        }

        private byte[] readStream(IWaveProvider waveStream)
        {
            using (var stream = new MemoryStream())
            {
                int read;
                while ((read = waveStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    stream.Write(buffer, 0, read);
                }
                return stream.ToArray();
            }
        }
    }
}