using System;
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
        public Boolean UseSecureHTTPS { get; set; }
        public Boolean OnlyOneBouquet { get; set; }       
        public string TVBouquet { get; set; }
        public Boolean ZapToChannel { get; set; }       
        public Boolean FetchPiconsFromWebInterface { get; set; }
        public string PiconsPath { get; set; }

        public string RecordingPath { get; set; }

        public Boolean EnableDebugLogging { get; set; }


        public PluginConfiguration()
        {
            HostName = "http://localhost";
            StreamingPort = "8001";
            WebInterfacePort = "8000";
            WebInterfaceUsername= "";
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
