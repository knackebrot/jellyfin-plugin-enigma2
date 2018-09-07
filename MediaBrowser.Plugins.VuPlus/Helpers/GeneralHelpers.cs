using MediaBrowser.Model.LiveTv;
using System;
using MediaBrowser.Model.Logging;

namespace MediaBrowser.Plugins.VuPlus.Helpers
{
    public static class ChannelHelper
    {
        public static ChannelType GetChannelType(string channelType)
        {
            ChannelType type = new ChannelType();

            if (channelType == "0x1")
            {
                type = ChannelType.TV;
            }
            else if (channelType == "0xa")
            {
                type = ChannelType.Radio;
            }

            return type;
        }
    }

    public static class UtilsHelper
    {
        public static void DebugInformation(ILogger logger, string message)
        {
            var config = Plugin.Instance.Configuration;
            bool enableDebugLogging = config.EnableDebugLogging;

            if (enableDebugLogging)
            {
                logger.Debug(message);
            }
        }
   
    }
}
