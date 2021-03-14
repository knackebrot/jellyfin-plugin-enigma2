using MediaBrowser.Model.Plugins;

namespace MediaBrowser.Plugins.VuPlus.Configuration
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
        public bool UseSecureHTTPS { get; set; }
        public bool OnlyOneBouquet { get; set; }
        public string TVBouquet { get; set; }
        public bool ZapToChannel { get; set; }
        public bool FetchPiconsFromWebInterface { get; set; }
        public string PiconsPath { get; set; }

        public string RecordingPath { get; set; }

        public bool EnableDebugLogging { get; set; }


        public PluginConfiguration()
        {
            HostName = "http://localhost";
            StreamingPort = "8001";
            WebInterfacePort = "8000";
            WebInterfaceUsername = "";
            WebInterfacePassword = "";
            UseSecureHTTPS = false;
            OnlyOneBouquet = true;
            TVBouquet = "Favourites (TV)";
            ZapToChannel = false;
            FetchPiconsFromWebInterface = true;
            PiconsPath = "";

            RecordingPath = "";

            EnableDebugLogging = false;

        }
    }
}
