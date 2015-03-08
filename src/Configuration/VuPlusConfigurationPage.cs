using System.IO;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;

namespace MediaBrowser.Plugins.VuPlus.Configuration
{
    /// <summary>
    /// Class VuPlusConfigurationPage
    /// </summary>
    class VuPlusConfigurationPage : IPluginConfigurationPage
    {
        /// <summary>
        /// Gets the type of the configuration page.
        /// </summary>
        /// <value>The type of the configuration page.</value>
        public ConfigurationPageType ConfigurationPageType
        {
            get { return ConfigurationPageType.PluginConfiguration; }
        }

        /// <summary>
        /// Gets the HTML stream.
        /// </summary>
        /// <returns>Stream.</returns>
        public Stream GetHtmlStream()
        {
            return GetType().Assembly.GetManifestResourceStream("MediaBrowser.Plugins.VuPlus.Configuration.configPage.html");
        }

        /// <summary>
        /// Gets the name.
        /// </summary>
        /// <value>The name.</value>
        public string Name
        {
            get { return "VuPlus"; }
        }

        public IPlugin Plugin
        {
            get { return VuPlus.Plugin.Instance; }
        }
    }
}
