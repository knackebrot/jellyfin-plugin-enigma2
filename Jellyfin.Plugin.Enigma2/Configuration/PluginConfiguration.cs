using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Enigma2.Configuration
{
    /// <summary>
    /// Class PluginConfiguration
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        public string HostName { get; set; }
        public string StreamingPort { get; set; }
        public string WebInterfacePort { get; set; }
        public string WebInterfaceUsername { get; set; }
        public string WebInterfacePassword { get; set; }
        public bool UseLoginForStreams { get; set; }
        public bool UseSecureHTTPS { get; set; }
        public bool UseSecureHTTPSForStreams { get; set; }
        public bool OnlyOneBouquet { get; set; }
        public string TVBouquet { get; set; }
        public bool ZapToChannel { get; set; }
        public bool FetchPiconsFromWebInterface { get; set; }
        public string PiconsPath { get; set; }

        public string RecordingPath { get; set; }

        public bool TranscodedStream { get; set; }
        public string TranscodingPort { get; set; }
        public string TranscodingBitrate { get; set; }
        public bool TranscodingCodecH265 { get; set; }

        public bool EnableDebugLogging { get; set; }


        public PluginConfiguration()
        {
            HostName = "localhost";
            StreamingPort = "8001";
            WebInterfacePort = "8000";
            WebInterfaceUsername = "";
            WebInterfacePassword = "";
            UseLoginForStreams = false;
            UseSecureHTTPS = false;
            UseSecureHTTPSForStreams = false;
            OnlyOneBouquet = true;
            TVBouquet = "Favourites (TV)";
            ZapToChannel = false;
            FetchPiconsFromWebInterface = true;
            PiconsPath = "";

            RecordingPath = "";

            TranscodedStream = false;
            TranscodingPort = "8002";
            TranscodingBitrate = "1000";
            TranscodingCodecH265 = false;

            TranscodingBitrate = "1000";

            EnableDebugLogging = false;
        }
    }
}
