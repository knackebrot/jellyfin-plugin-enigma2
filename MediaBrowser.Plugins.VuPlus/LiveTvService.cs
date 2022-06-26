using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Plugins.VuPlus.Helpers;
using Microsoft.Extensions.Logging;

namespace MediaBrowser.Plugins.VuPlus
{
    /// <summary>
    /// Class LiveTvService
    /// </summary>
    public class LiveTvService : ILiveTvService
    {
        private readonly ILogger<LiveTvService> _logger;
        private int _liveStreams;
        private readonly Dictionary<int, int> _heartBeat = new Dictionary<int, int>();

        private string tvBouquetSRef;
        private List<ChannelInfo> tvChannelInfos = new List<ChannelInfo>();

        public DateTime LastRecordingChange = DateTime.MinValue;

        private readonly HttpClient _httpClient;

        public LiveTvService(ILogger<LiveTvService> logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClient = GetHttpClient(httpClientFactory);
        }


        /// <summary>
        /// Ensure that we are connected to the VuPlus server
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task EnsureConnectionAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[VuPlus] Start EnsureConnectionAsync");

            var config = Plugin.Instance.Configuration;

            // log settings
            _logger.LogInformation(string.Format("[VuPlus] EnsureConnectionAsync HostName: {0}", config.HostName));
            _logger.LogInformation(string.Format("[VuPlus] EnsureConnectionAsync StreamingPort: {0}", config.StreamingPort));
            _logger.LogInformation(string.Format("[VuPlus] EnsureConnectionAsync WebInterfacePort: {0}", config.WebInterfacePort));
            if (string.IsNullOrEmpty(config.WebInterfaceUsername))
            {
                _logger.LogInformation("[VuPlus] EnsureConnectionAsync WebInterfaceUsername: ");
            }
            else
            {
                _logger.LogInformation(string.Format("[VuPlus] EnsureConnectionAsync WebInterfaceUsername: {0}", "********"));
            }

            if (string.IsNullOrEmpty(config.WebInterfacePassword))
            {
                _logger.LogInformation("[VuPlus] EnsureConnectionAsync WebInterfacePassword: ");
            }
            else
            {
                _logger.LogInformation(string.Format("[VuPlus] EnsureConnectionAsync WebInterfaceUsername: {0}", "********"));
            }

            _logger.LogInformation(string.Format("[VuPlus] EnsureConnectionAsync UseSecureHTTPS: {0}", config.UseSecureHTTPS));
            _logger.LogInformation(string.Format("[VuPlus] EnsureConnectionAsync OnlyOneBouquet: {0}", config.OnlyOneBouquet));
            _logger.LogInformation(string.Format("[VuPlus] EnsureConnectionAsync TVBouquet: {0}", config.TVBouquet));
            _logger.LogInformation(string.Format("[VuPlus] EnsureConnectionAsync ZapToChannel: {0}", config.ZapToChannel));
            _logger.LogInformation(string.Format("[VuPlus] EnsureConnectionAsync FetchPiconsFromWebInterface: {0}", config.FetchPiconsFromWebInterface));
            _logger.LogInformation(string.Format("[VuPlus] EnsureConnectionAsync PiconsPath: {0}", config.PiconsPath));
            _logger.LogInformation(string.Format("[VuPlus] EnsureConnectionAsync RecordingPath: {0}", config.RecordingPath));
            _logger.LogInformation(string.Format("[VuPlus] EnsureConnectionAsync EnableDebugLogging: {0}", config.EnableDebugLogging));

            // validate settings
            if (string.IsNullOrEmpty(config.HostName))
            {
                _logger.LogError("[VuPlus] HostName must be configured.");
                throw new InvalidOperationException("VuPlus HostName must be configured.");
            }

            if (string.IsNullOrEmpty(config.StreamingPort))
            {
                _logger.LogError("[VuPlus] Streaming Port must be configured.");
                throw new InvalidOperationException("VuPlus Streaming Port must be configured.");
            }

            if (string.IsNullOrEmpty(config.WebInterfacePort))
            {
                _logger.LogError("[VuPlus] Web Interface Port must be configured.");
                throw new InvalidOperationException("VuPlus Web Interface Port must be configured.");
            }

            if (config.OnlyOneBouquet)
            {
                if (string.IsNullOrEmpty(config.TVBouquet))
                {
                    _logger.LogError("[VuPlus] TV Bouquet must be configured if Fetch only one TV bouquet selected.");
                    throw new InvalidOperationException("VuPlus TVBouquet must be configured if Fetch only one TV bouquet selected.");
                }
            }

            if (!config.FetchPiconsFromWebInterface)
            {
                if (string.IsNullOrEmpty(config.PiconsPath))
                {
                    _logger.LogError("[VuPlus] Picons location must be configured if Fetch Picons from Web Service is disabled.");
                    throw new InvalidOperationException("VuPlus Picons location must be configured if Fetch Picons from Web Service is disabled.");
                }
            }

            _logger.LogInformation("[VuPlus] EnsureConnectionAsync Validation of config parameters completed");

            if (config.OnlyOneBouquet)
            {
                // connect to VuPlus box to test connectivity and at same time get sRef for TV Bouquet.
                tvBouquetSRef = await InitiateSession(cancellationToken, config.TVBouquet).ConfigureAwait(false);
            }
            else
            {
                // connect to VuPlus box to test connectivity.
                var resultNotRequired = await InitiateSession(cancellationToken, null).ConfigureAwait(false);
                tvBouquetSRef = null;
            }
        }

        /// <summary>
        /// Creates HttpClient for connection to Enigma2
        /// </summary>
        /// <param name="httpClientFactory"></param>
        /// <returns></returns>
        private HttpClient GetHttpClient(IHttpClientFactory httpClientFactory)
        {
            var httpClient = httpClientFactory.CreateClient(NamedClient.Default);
            httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue(Name, Plugin.Instance.Version.ToString()));

            if (!string.IsNullOrEmpty(Plugin.Instance.Configuration.WebInterfaceUsername))
            {
                var authInfo = Plugin.Instance.Configuration.WebInterfaceUsername + ":" + Plugin.Instance.Configuration.WebInterfacePassword;
                authInfo = Convert.ToBase64String(Encoding.Default.GetBytes(authInfo));
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authInfo);
            }

            return httpClient;
        }

        /// <summary>
        /// Checks connection to VuPlus and retrieves service reference for channel if only one bouquet.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="tvBouquet">The TV Bouquet.</param>
        /// <returns>Task{String>}.</returns>
        public async Task<string> InitiateSession(CancellationToken cancellationToken, string tvBouquet)
        {
            _logger.LogInformation("[VuPlus] Start InitiateSession, validates connection and returns Bouquet reference if required");
            //await EnsureConnectionAsync(cancellationToken).ConfigureAwait(false);

            var protocol = "http";
            if (Plugin.Instance.Configuration.UseSecureHTTPS)
            {
                protocol = "https";
            }

            var baseUrl = protocol + "://" + Plugin.Instance.Configuration.HostName + ":" + Plugin.Instance.Configuration.WebInterfacePort;

            var url = string.Format("{0}/web/getservices", baseUrl);
            UtilsHelper.DebugInformation(_logger, string.Format("[VuPlus] InitiateSession url: {0}", url));

            using (var stream = await _httpClient.GetStreamAsync(url, cancellationToken).ConfigureAwait(false))
            {
                using (var reader = new StreamReader(stream))
                {
                    var xmlResponse = reader.ReadToEnd();
                    UtilsHelper.DebugInformation(_logger, string.Format("[VuPlus] InitiateSession response: {0}", xmlResponse));

                    try
                    {
                        var xml = new XmlDocument();
                        xml.LoadXml(xmlResponse);

                        string tvBouquetReference = null;

                        var e2services = xml.GetElementsByTagName("e2service");

                        // If TV Bouquet passed find associated service reference
                        if (!string.IsNullOrEmpty(tvBouquet))
                        {
                            foreach (XmlNode xmlNode in e2services)
                            {
                                var channelInfo = new ChannelInfo();

                                var e2servicereference = "?";
                                var e2servicename = "?";

                                foreach (XmlNode node in xmlNode.ChildNodes)
                                {
                                    if (node.Name == "e2servicereference")
                                    {
                                        e2servicereference = node.InnerText;
                                    }
                                    else if (node.Name == "e2servicename")
                                    {
                                        e2servicename = node.InnerText;
                                    }
                                }
                                if (tvBouquet == e2servicename)
                                {
                                    tvBouquetReference = e2servicereference;
                                    return tvBouquetReference;
                                }
                            }
                            // make sure we have found the TV Bouquet
                            if (!string.IsNullOrEmpty(tvBouquet))
                            {
                                _logger.LogError("[VuPlus] Failed to find TV Bouquet specified in VuPlus configuration.");
                                throw new ApplicationException("Failed to find TV Bouquet specified in VuPlus configuration.");
                            }
                        }
                        return tvBouquetReference;
                    }
                    catch (Exception e)
                    {
                        _logger.LogError("[VuPlus] Failed to parse services information.");
                        _logger.LogError(string.Format("[VuPlus] InitiateSession error: {0}", e.Message));
                        throw new ApplicationException("Failed to connect to VuPlus.");
                    }

                }
            }
        }


        /// <summary>
        /// Gets the channels async.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task{IEnumerable{ChannelInfo}}.</returns>
        public async Task<IEnumerable<ChannelInfo>> GetChannelsAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[VuPlus] Start GetChannelsAsync, retrieve all channels");
            await EnsureConnectionAsync(cancellationToken).ConfigureAwait(false);

            var protocol = "http";
            if (Plugin.Instance.Configuration.UseSecureHTTPS)
            {
                protocol = "https";
            }

            var baseUrl = protocol + "://" + Plugin.Instance.Configuration.HostName + ":" + Plugin.Instance.Configuration.WebInterfacePort;

            var baseUrlPicon = protocol + "://" + Plugin.Instance.Configuration.HostName + ":" + Plugin.Instance.Configuration.WebInterfacePort;

            var url = "";
            if (string.IsNullOrEmpty(tvBouquetSRef))
            {
                url = string.Format("{0}/web/getservices", baseUrl);
            }
            else
            {
                url = string.Format("{0}/web/getservices?sRef={1}", baseUrl, tvBouquetSRef);
            }

            UtilsHelper.DebugInformation(_logger, string.Format("[VuPlus] GetChannelsAsync url: {0}", url));

            if (!string.IsNullOrEmpty(Plugin.Instance.Configuration.WebInterfaceUsername))
            {
                baseUrlPicon = protocol + "://" + Plugin.Instance.Configuration.WebInterfaceUsername + ":" + Plugin.Instance.Configuration.WebInterfacePassword + "@" + Plugin.Instance.Configuration.HostName + ":" + Plugin.Instance.Configuration.WebInterfacePort;
            }

            using (var stream = await _httpClient.GetStreamAsync(url, cancellationToken).ConfigureAwait(false))
            {
                using (var reader = new StreamReader(stream))
                {

                    var xmlResponse = reader.ReadToEnd();
                    UtilsHelper.DebugInformation(_logger, string.Format("[VuPlus] GetChannelsAsync response: {0}", xmlResponse));

                    try
                    {
                        var xml = new XmlDocument();
                        xml.LoadXml(xmlResponse);

                        var channelInfos = new List<ChannelInfo>();

                        if (string.IsNullOrEmpty(tvBouquetSRef))
                        {
                            // Load channels from all TV Bouquets
                            _logger.LogInformation("[VuPlus] GetChannelsAsync for all TV Bouquets");

                            var e2services = xml.GetElementsByTagName("e2service");
                            foreach (XmlNode xmlNode in e2services)
                            {
                                var channelInfo = new ChannelInfo();
                                var e2servicereference = "?";
                                var e2servicename = "?";

                                foreach (XmlNode node in xmlNode.ChildNodes)
                                {
                                    if (node.Name == "e2servicereference")
                                    {
                                        e2servicereference = node.InnerText;
                                    }
                                    else if (node.Name == "e2servicename")
                                    {
                                        e2servicename = node.InnerText;
                                    }
                                }

                                // get all channels for TV Bouquet
                                var channelInfosForBouquet = await GetChannelsForTVBouquetAsync(cancellationToken, e2servicereference).ConfigureAwait(false);

                                // store all channels for TV Bouquet
                                channelInfos.AddRange(channelInfosForBouquet);
                            }

                            return channelInfos;
                        }
                        else
                        {
                            // Load channels for specified TV Bouquet only
                            var count = 1;

                            var e2services = xml.GetElementsByTagName("e2service");
                            foreach (XmlNode xmlNode in e2services)
                            {
                                var channelInfo = new ChannelInfo();

                                var e2servicereference = "?";
                                var e2servicename = "?";

                                foreach (XmlNode node in xmlNode.ChildNodes)
                                {
                                    if (node.Name == "e2servicereference")
                                    {
                                        e2servicereference = node.InnerText;
                                    }
                                    else if (node.Name == "e2servicename")
                                    {
                                        e2servicename = node.InnerText;
                                    }
                                }

                                // Check whether the current element is not just a label
                                if (!e2servicereference.StartsWith("1:64:"))
                                {
                                    //check for radio channel
                                    if (e2servicereference.ToUpper().Contains("RADIO"))
                                    {
                                        channelInfo.ChannelType = ChannelType.Radio;
                                    }
                                    else
                                    {
                                        channelInfo.ChannelType = ChannelType.TV;
                                    }

                                    channelInfo.HasImage = true;
                                    channelInfo.Id = e2servicereference;

                                    // image name is name is e2servicereference with last char removed, then replace all : with _, then add .png
                                    var imageName = e2servicereference.Remove(e2servicereference.Length - 1);
                                    imageName = imageName.Replace(":", "_");
                                    imageName = imageName + ".png";
                                    //var imageUrl = string.Format("{0}/picon/{1}", baseUrl, imageName);
                                    var imageUrl = string.Format("{0}/picon/{1}", baseUrlPicon, imageName);

                                    if (Plugin.Instance.Configuration.FetchPiconsFromWebInterface)
                                    {
                                        channelInfo.ImagePath = null;
                                        //channelInfo.ImageUrl = WebUtility.UrlEncode(imageUrl);
                                        channelInfo.ImageUrl = imageUrl;
                                    }
                                    else
                                    {
                                        channelInfo.ImagePath = Plugin.Instance.Configuration.PiconsPath + imageName;
                                        channelInfo.ImageUrl = null;
                                    }

                                    channelInfo.Name = e2servicename;
                                    channelInfo.Number = count.ToString();

                                    channelInfos.Add(channelInfo);
                                    count = count + 1;
                                }
                                else
                                {
                                    _logger.LogInformation("[VuPlus] ignoring channel label " + e2servicereference);
                                }
                            }
                        }
                        tvChannelInfos = channelInfos;
                        return channelInfos;
                    }
                    catch (Exception e)
                    {
                        _logger.LogError("[VuPlus] Failed to parse channel information.");
                        _logger.LogError(string.Format("[VuPlus] GetChannelsAsync error: {0}", e.Message));
                        throw new ApplicationException("Failed to parse channel information.");
                    }
                }
            }
        }


        /// <summary>
        /// Gets the channels async.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="sRef">Service reference</param>
        /// <returns>Task{List<ChannelInfo>}.</returns>
        public async Task<List<ChannelInfo>> GetChannelsForTVBouquetAsync(CancellationToken cancellationToken, string sRef)
        {
            _logger.LogInformation("[VuPlus] Start GetChannelsForTVBouquetAsync, retrieve all channels for TV Bouquet " + sRef);
            await EnsureConnectionAsync(cancellationToken).ConfigureAwait(false);

            var protocol = "http";
            if (Plugin.Instance.Configuration.UseSecureHTTPS)
            {
                protocol = "https";
            }

            var baseUrl = protocol + "://" + Plugin.Instance.Configuration.HostName + ":" + Plugin.Instance.Configuration.WebInterfacePort;

            var baseUrlPicon = protocol + "://" + Plugin.Instance.Configuration.WebInterfaceUsername + ":" + Plugin.Instance.Configuration.WebInterfacePassword + "@" + Plugin.Instance.Configuration.HostName + ":" + Plugin.Instance.Configuration.WebInterfacePort;

            var url = string.Format("{0}/web/getservices?sRef={1}", baseUrl, sRef);

            UtilsHelper.DebugInformation(_logger, string.Format("[VuPlus] GetChannelsForTVBouquetAsync url: {0}", url));

            if (!string.IsNullOrEmpty(Plugin.Instance.Configuration.WebInterfaceUsername))
            {
                baseUrlPicon = protocol + "://" + Plugin.Instance.Configuration.WebInterfaceUsername + ":" + Plugin.Instance.Configuration.WebInterfacePassword + "@" + Plugin.Instance.Configuration.HostName + ":" + Plugin.Instance.Configuration.WebInterfacePort;
            }

            using (var stream = await _httpClient.GetStreamAsync(url, cancellationToken).ConfigureAwait(false))
            {
                using (var reader = new StreamReader(stream))
                {
                    var xmlResponse = reader.ReadToEnd();
                    UtilsHelper.DebugInformation(_logger, string.Format("[VuPlus] GetChannelsForTVBouquetAsync response: {0}", xmlResponse));

                    try
                    {
                        var xml = new XmlDocument();
                        xml.LoadXml(xmlResponse);

                        var channelInfos = new List<ChannelInfo>();

                        // Load channels for specified TV Bouquet only

                        var count = 1;

                        var e2services = xml.GetElementsByTagName("e2service");
                        foreach (XmlNode xmlNode in e2services)
                        {
                            var channelInfo = new ChannelInfo();

                            var e2servicereference = "?";
                            var e2servicename = "?";

                            foreach (XmlNode node in xmlNode.ChildNodes)
                            {
                                if (node.Name == "e2servicereference")
                                {
                                    e2servicereference = node.InnerText;
                                }
                                else if (node.Name == "e2servicename")
                                {
                                    e2servicename = node.InnerText;
                                }
                            }

                            // Check whether the current element is not just a label
                            if (!e2servicereference.StartsWith("1:64:"))
                            {
                                //check for radio channel
                                if (e2servicereference.Contains("radio"))
                                {
                                    channelInfo.ChannelType = ChannelType.Radio;
                                }
                                else
                                {
                                    channelInfo.ChannelType = ChannelType.TV;
                                }

                                channelInfo.HasImage = true;
                                channelInfo.Id = e2servicereference;

                                // image name is name is e2servicereference with last char removed, then replace all : with _, then add .png
                                var imageName = e2servicereference.Remove(e2servicereference.Length - 1);
                                imageName = imageName.Replace(":", "_");
                                imageName = imageName + ".png";
                                //var imageUrl = string.Format("{0}/picon/{1}", baseUrl, imageName);
                                var imageUrl = string.Format("{0}/picon/{1}", baseUrlPicon, imageName);

                                if (Plugin.Instance.Configuration.FetchPiconsFromWebInterface)
                                {
                                    channelInfo.ImagePath = null;
                                    //channelInfo.ImageUrl = WebUtility.UrlEncode(imageUrl);
                                    channelInfo.ImageUrl = imageUrl;
                                }
                                else
                                {
                                    channelInfo.ImagePath = Plugin.Instance.Configuration.PiconsPath + imageName;
                                    channelInfo.ImageUrl = null;
                                }

                                channelInfo.Name = e2servicename;
                                channelInfo.Number = count.ToString();

                                channelInfos.Add(channelInfo);
                                count++;
                            }
                            else
                            {
                                UtilsHelper.DebugInformation(_logger, string.Format("[VuPlus] ignoring channel {0}", e2servicereference));
                            }
                        }
                        return channelInfos;
                    }
                    catch (Exception e)
                    {
                        _logger.LogError("[VuPlus] Failed to parse channel information.");
                        _logger.LogError(string.Format("[VuPlus] GetChannelsForTVBouquetAsync error: {0}", e.Message));
                        throw new ApplicationException("Failed to parse channel information.");
                    }
                }
            }
        }


        /// <summary>
        /// Gets the Recordings async
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task{IEnumerable{RecordingInfo}}</returns>
        public async Task<IEnumerable<RecordingInfo>> GetRecordingsAsync(CancellationToken cancellationToken)
        {
            return new List<RecordingInfo>();
        }

        public async Task<IEnumerable<MyRecordingInfo>> GetAllRecordingsAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[VuPlus] Start GetRecordingsAsync, retrieve all 'Inprogress' and 'Completed' recordings ");
            await EnsureConnectionAsync(cancellationToken).ConfigureAwait(false);

            var protocol = "http";
            if (Plugin.Instance.Configuration.UseSecureHTTPS)
            {
                protocol = "https";
            }

            var baseUrl = protocol + "://" + Plugin.Instance.Configuration.HostName + ":" + Plugin.Instance.Configuration.WebInterfacePort;

            var url = string.Format("{0}/web/movielist", baseUrl);
            UtilsHelper.DebugInformation(_logger, string.Format("[VuPlus] GetRecordingsAsync url: {0}", url));

            using (var stream = await _httpClient.GetStreamAsync(url, cancellationToken).ConfigureAwait(false))
            {
                using (var reader = new StreamReader(stream))
                {
                    var xmlResponse = reader.ReadToEnd();
                    UtilsHelper.DebugInformation(_logger, string.Format("[VuPlus] GetRecordingsAsync response: {0}", xmlResponse));

                    try
                    {
                        var xml = new XmlDocument();
                        xml.LoadXml(xmlResponse);

                        var recordingInfos = new List<MyRecordingInfo>();

                        var count = 1;

                        var e2movie = xml.GetElementsByTagName("e2movie");

                        foreach (XmlNode xmlNode in e2movie)
                        {
                            var recordingInfo = new MyRecordingInfo();

                            var e2servicereference = "?";
                            var e2title = "?";
                            var e2description = "?";
                            var e2servicename = "?";
                            var e2time = "?";
                            var e2length = "?";
                            var e2filename = "?";

                            foreach (XmlNode node in xmlNode.ChildNodes)
                            {
                                if (node.Name == "e2servicereference")
                                {
                                    e2servicereference = node.InnerText;
                                }
                                else if (node.Name == "e2title")
                                {
                                    e2title = node.InnerText;
                                }
                                else if (node.Name == "e2description")
                                {
                                    e2description = node.InnerText;
                                }
                                else if (node.Name == "e2servicename")
                                {
                                    e2servicename = node.InnerText;
                                }
                                else if (node.Name == "e2time")
                                {
                                    e2time = node.InnerText;
                                }
                                else if (node.Name == "e2length")
                                {
                                    e2length = node.InnerText;
                                }
                                else if (node.Name == "e2filename")
                                {
                                    e2filename = node.InnerText;
                                }
                            }

                            recordingInfo.Audio = null;

                            recordingInfo.ChannelId = null;
                            //check for radio channel
                            if (e2servicereference.ToUpper().Contains("RADIO"))
                            {
                                recordingInfo.ChannelType = ChannelType.Radio;
                            }
                            else
                            {
                                recordingInfo.ChannelType = ChannelType.TV;
                            }

                            recordingInfo.HasImage = false;
                            recordingInfo.ImagePath = null;
                            recordingInfo.ImageUrl = null;

                            foreach (var channelInfo in tvChannelInfos)
                            {
                                if (channelInfo.Name == e2servicename)
                                {
                                    UtilsHelper.DebugInformation(_logger, string.Format("[VuPlus] GetRecordingsAsync match on channel name : {0} for recording {1}", e2servicename, e2title));
                                    recordingInfo.ChannelId = channelInfo.Id;
                                    recordingInfo.ChannelType = channelInfo.ChannelType;
                                    recordingInfo.HasImage = true;
                                    recordingInfo.ImagePath = channelInfo.ImagePath;
                                    recordingInfo.ImageUrl = channelInfo.ImageUrl;
                                    break;
                                }
                            }

                            if (recordingInfo.ChannelId == null)
                            {
                                UtilsHelper.DebugInformation(_logger, string.Format("[VuPlus] GetRecordingsAsync no match on channel name : {0} for recording {1}", e2servicename, e2title));
                            }

                            recordingInfo.CommunityRating = 0;

                            var sdated = long.Parse(e2time);
                            var sdate = ApiHelper.DateTimeFromUnixTimestampSeconds(sdated);
                            recordingInfo.StartDate = sdate.ToUniversalTime();

                            //length in format mm:ss
                            var words = e2length.Split(':');
                            var mins = long.Parse(words[0]);
                            var seconds = long.Parse(words[1]);
                            var edated = long.Parse(e2time) + (mins * 60) + (seconds);
                            var edate = ApiHelper.DateTimeFromUnixTimestampSeconds(edated);
                            recordingInfo.EndDate = edate.ToUniversalTime();

                            //recordingInfo.EpisodeTitle = e2title;
                            recordingInfo.EpisodeTitle = null;

                            recordingInfo.Overview = e2description;

                            var genre = new List<string>
                            {
                                "Unknown"
                            };
                            recordingInfo.Genres = genre;

                            recordingInfo.Id = e2servicereference;
                            recordingInfo.IsHD = false;
                            recordingInfo.IsKids = false;
                            recordingInfo.IsLive = false;
                            recordingInfo.IsMovie = false;
                            recordingInfo.IsNews = false;
                            recordingInfo.IsPremiere = false;
                            recordingInfo.IsRepeat = false;
                            recordingInfo.IsSeries = false;
                            recordingInfo.IsSports = false;
                            recordingInfo.Name = e2title;
                            recordingInfo.OfficialRating = null;
                            recordingInfo.OriginalAirDate = null;
                            recordingInfo.Overview = e2description;
                            recordingInfo.Path = null;
                            recordingInfo.ProgramId = null;
                            recordingInfo.SeriesTimerId = null;
                            recordingInfo.Url = baseUrl + "/file?file=" + WebUtility.UrlEncode(e2filename);

                            recordingInfos.Add(recordingInfo);
                            count = count + 1;
                        }
                        return recordingInfos;
                    }
                    catch (Exception e)
                    {
                        _logger.LogError("[VuPlus] Failed to parse timer information.");
                        _logger.LogError(string.Format("[VuPlus] GetRecordingsAsync error: {0}", e.Message));
                        throw new ApplicationException("Failed to parse timer information.");
                    }
                }
            }
        }

        /// <summary>
        /// Delete the Recording async from the disk
        /// </summary>
        /// <param name="recordingId">The recordingId</param>
        /// <param name="cancellationToken">The cancellationToken</param>
        /// <returns></returns>
        public async Task DeleteRecordingAsync(string recordingId, CancellationToken cancellationToken)
        {
            _logger.LogInformation(string.Format("[VuPlus] Start Delete Recording Async for recordingId: {0}", recordingId));
            await EnsureConnectionAsync(cancellationToken).ConfigureAwait(false);

            var protocol = "http";
            if (Plugin.Instance.Configuration.UseSecureHTTPS)
            {
                protocol = "https";
            }

            var baseUrl = protocol + "://" + Plugin.Instance.Configuration.HostName + ":" + Plugin.Instance.Configuration.WebInterfacePort;

            var url = string.Format("{0}/web/moviedelete?sRef={1}", baseUrl, recordingId);
            UtilsHelper.DebugInformation(_logger, string.Format("[VuPlus] DeleteRecordingAsync url: {0}", url));

            using (var stream = await _httpClient.GetStreamAsync(url, cancellationToken).ConfigureAwait(false))
            {
                using (var reader = new StreamReader(stream))
                {
                    var xmlResponse = reader.ReadToEnd();
                    UtilsHelper.DebugInformation(_logger, string.Format("[VuPlus] DeleteRecordingAsync response: {0}", xmlResponse));

                    try
                    {
                        var xml = new XmlDocument();
                        xml.LoadXml(xmlResponse);

                        var e2simplexmlresult = xml.GetElementsByTagName("e2simplexmlresult");
                        foreach (XmlNode xmlNode in e2simplexmlresult)
                        {
                            var recordingInfo = new RecordingInfo();

                            var e2state = "?";
                            var e2statetext = "?";

                            foreach (XmlNode node in xmlNode.ChildNodes)
                            {
                                if (node.Name == "e2state")
                                {
                                    e2state = node.InnerText;
                                }
                                else if (node.Name == "e2statetext")
                                {
                                    e2statetext = node.InnerText;
                                }
                            }

                            if (e2state != "True")
                            {
                                _logger.LogError("[VuPlus] Failed to delete recording information.");
                                _logger.LogError(string.Format("[VuPlus] DeleteRecordingAsync e2statetext: {0}", e2statetext));
                                throw new ApplicationException("Failed to delete recording.");
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.LogError("[VuPlus] Failed to parse delete recording information.");
                        _logger.LogError(string.Format("[VuPlus] DeleteRecordingAsync error: {0}", e.Message));
                        throw new ApplicationException("Failed to parse delete recording information.");
                    }
                }
            }
        }


        /// <summary>
        /// Cancel pending scheduled Recording
        /// </summary>
        /// <param name="timerId">The timerId</param>
        /// <param name="cancellationToken">The cancellationToken</param>
        /// <returns></returns>
        public async Task CancelTimerAsync(string timerId, CancellationToken cancellationToken)
        {
            _logger.LogInformation(string.Format("[VuPlus] Start CancelTimerAsync for recordingId: {0}", timerId));
            await EnsureConnectionAsync(cancellationToken).ConfigureAwait(false);

            // extract sRef, id, begin and end from passed timerId
            var words = timerId.Split('~');
            var sRef = words[0];
            var id = words[1];
            var begin = words[2];
            var end = words[3];

            var protocol = "http";
            if (Plugin.Instance.Configuration.UseSecureHTTPS)
            {
                protocol = "https";
            }

            var baseUrl = protocol + "://" + Plugin.Instance.Configuration.HostName + ":" + Plugin.Instance.Configuration.WebInterfacePort;

            var url = string.Format("{0}/web/timerdelete?sRef={1}&begin={2}&end={3}", baseUrl, sRef, begin, end);
            UtilsHelper.DebugInformation(_logger, string.Format("[VuPlus] CancelTimerAsync url: {0}", url));

            using (var stream = await _httpClient.GetStreamAsync(url, cancellationToken).ConfigureAwait(false))
            {
                using (var reader = new StreamReader(stream))
                {
                    var xmlResponse = reader.ReadToEnd();
                    UtilsHelper.DebugInformation(_logger, string.Format("[VuPlus] CancelTimerAsync response: {0}", xmlResponse));

                    try
                    {
                        var xml = new XmlDocument();
                        xml.LoadXml(xmlResponse);

                        var e2simplexmlresult = xml.GetElementsByTagName("e2simplexmlresult");
                        foreach (XmlNode xmlNode in e2simplexmlresult)
                        {
                            var recordingInfo = new RecordingInfo();

                            var e2state = "?";
                            var e2statetext = "?";

                            foreach (XmlNode node in xmlNode.ChildNodes)
                            {
                                if (node.Name == "e2state")
                                {
                                    e2state = node.InnerText;
                                }
                                else if (node.Name == "e2statetext")
                                {
                                    e2statetext = node.InnerText;
                                }
                            }

                            if (e2state != "True")
                            {
                                _logger.LogError("[VuPlus] Failed to cancel timer.");
                                _logger.LogError(string.Format("[VuPlus] CancelTimerAsync e2statetext: {0}", e2statetext));
                                throw new ApplicationException("Failed to cancel timer.");
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.LogError("[VuPlus] Failed to parse cancel timer information.");
                        _logger.LogError(string.Format("[VuPlus] CancelTimerAsync error: {0}", e.Message));
                        throw new ApplicationException("Failed to parse cancel timer information.");
                    }
                }
            }
        }


        /// <summary>
        /// Create a new recording
        /// </summary>
        /// <param name="info">The TimerInfo</param>
        /// <param name="cancellationToken">The cancellationToken</param>
        /// <returns></returns>
        public async Task CreateTimerAsync(TimerInfo info, CancellationToken cancellationToken)
        {
            _logger.LogInformation(string.Format("[VuPlus] Start CreateTimerAsync for ChannelId: {0} & Name: {1}", info.ChannelId, info.Name));
            await EnsureConnectionAsync(cancellationToken).ConfigureAwait(false);

            // extract eventid from info.ProgramId
            var words = info.ProgramId.Split('~');
            var eventid = words[1];

            var protocol = "http";
            if (Plugin.Instance.Configuration.UseSecureHTTPS)
            {
                protocol = "https";
            }

            var baseUrl = protocol + "://" + Plugin.Instance.Configuration.HostName + ":" + Plugin.Instance.Configuration.WebInterfacePort;

            var url = string.Format("{0}/web/timeraddbyeventid?sRef={1}&eventid={2}", baseUrl, info.ChannelId, eventid);

            if (!string.IsNullOrEmpty(Plugin.Instance.Configuration.RecordingPath))
            {
                url = url + string.Format("&dirname={0}", WebUtility.UrlEncode(Plugin.Instance.Configuration.RecordingPath));
            }

            UtilsHelper.DebugInformation(_logger, string.Format("[VuPlus] CreateTimerAsync url: {0}", url));

            using (var stream = await _httpClient.GetStreamAsync(url, cancellationToken).ConfigureAwait(false))
            {
                using (var reader = new StreamReader(stream))
                {
                    var xmlResponse = reader.ReadToEnd();
                    UtilsHelper.DebugInformation(_logger, string.Format("[VuPlus] CancelTimerAsync response: {0}", xmlResponse));

                    try
                    {
                        var xml = new XmlDocument();
                        xml.LoadXml(xmlResponse);

                        var e2simplexmlresult = xml.GetElementsByTagName("e2simplexmlresult");
                        foreach (XmlNode xmlNode in e2simplexmlresult)
                        {
                            var recordingInfo = new RecordingInfo();

                            var e2state = "?";
                            var e2statetext = "?";

                            foreach (XmlNode node in xmlNode.ChildNodes)
                            {
                                if (node.Name == "e2state")
                                {
                                    e2state = node.InnerText;
                                }
                                else if (node.Name == "e2statetext")
                                {
                                    e2statetext = node.InnerText;
                                }
                            }

                            if (e2state != "True")
                            {
                                _logger.LogError("[VuPlus] Failed to create timer.");
                                _logger.LogError(string.Format("[VuPlus] CreateTimerAsync e2statetext: {0}", e2statetext));
                                throw new ApplicationException("Failed to create timer.");
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.LogError("[VuPlus] Failed to parse create timer information.");
                        _logger.LogError(string.Format("[VuPlus] CreateTimerAsync error: {0}", e.Message));
                        throw new ApplicationException("Failed to parse create timer information.");
                    }
                }
            }
        }


        /// <summary>
        /// Get the pending Recordings.
        /// </summary>
        /// <param name="cancellationToken">The CancellationToken</param>
        /// <returns>IEnumerable<TimerInfo></returns>
        public async Task<IEnumerable<TimerInfo>> GetTimersAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[VuPlus] Start GetTimerAsync, retrieve the 'Pending' recordings");
            await EnsureConnectionAsync(cancellationToken).ConfigureAwait(false);

            var protocol = "http";
            if (Plugin.Instance.Configuration.UseSecureHTTPS)
            {
                protocol = "https";
            }

            var baseUrl = protocol + "://" + Plugin.Instance.Configuration.HostName + ":" + Plugin.Instance.Configuration.WebInterfacePort;

            var url = string.Format("{0}/web/timerlist", baseUrl);
            UtilsHelper.DebugInformation(_logger, string.Format("[VuPlus] GetTimersAsync url: {0}", url));

            using (var stream = await _httpClient.GetStreamAsync(url, cancellationToken).ConfigureAwait(false))
            {
                using (var reader = new StreamReader(stream))
                {
                    var xmlResponse = reader.ReadToEnd();
                    UtilsHelper.DebugInformation(_logger, string.Format("[VuPlus] GetTimersAsync response: {0}", xmlResponse));

                    try
                    {
                        var xml = new XmlDocument();
                        xml.LoadXml(xmlResponse);

                        var timerInfos = new List<TimerInfo>();

                        var count = 1;

                        var e2timer = xml.GetElementsByTagName("e2timer");
                        foreach (XmlNode xmlNode in e2timer)
                        {
                            var timerInfo = new TimerInfo();

                            var e2servicereference = "?";
                            var e2name = "?";
                            var e2description = "?";
                            var e2eit = "?";
                            var e2timebegin = "?";
                            var e2timeend = "?";
                            var e2state = "?";

                            foreach (XmlNode node in xmlNode.ChildNodes)
                            {
                                if (node.Name == "e2servicereference")
                                {
                                    e2servicereference = node.InnerText;
                                }
                                else if (node.Name == "e2name")
                                {
                                    e2name = node.InnerText;
                                }
                                else if (node.Name == "e2description")
                                {
                                    e2description = node.InnerText;
                                }
                                else if (node.Name == "e2eit")
                                {
                                    e2eit = node.InnerText;
                                }
                                else if (node.Name == "e2timebegin")
                                {
                                    e2timebegin = node.InnerText;
                                }
                                else if (node.Name == "e2timeend")
                                {
                                    e2timeend = node.InnerText;
                                }
                                else if (node.Name == "e2state")
                                {
                                    e2state = node.InnerText;
                                }
                            }

                            // only interested in pending timers and ones recording now
                            if (e2state == "0" || e2state == "2")
                            {

                                timerInfo.ChannelId = e2servicereference;

                                var edated = long.Parse(e2timeend);
                                var edate = ApiHelper.DateTimeFromUnixTimestampSeconds(edated);
                                timerInfo.EndDate = edate.ToUniversalTime();

                                timerInfo.Id = e2servicereference + "~" + e2eit + "~" + e2timebegin + "~" + e2timeend + "~" + count;

                                timerInfo.IsPostPaddingRequired = false;
                                timerInfo.IsPrePaddingRequired = false;
                                timerInfo.Name = e2name;
                                timerInfo.Overview = e2description;
                                timerInfo.PostPaddingSeconds = 0;
                                timerInfo.PrePaddingSeconds = 0;
                                timerInfo.Priority = 0;
                                timerInfo.ProgramId = null;
                                timerInfo.SeriesTimerId = null;

                                var sdated = long.Parse(e2timebegin);
                                var sdate = ApiHelper.DateTimeFromUnixTimestampSeconds(sdated);
                                timerInfo.StartDate = sdate.ToUniversalTime();

                                if (e2state == "0")
                                {
                                    timerInfo.Status = RecordingStatus.New;
                                }

                                if (e2state == "2")
                                {
                                    timerInfo.Status = RecordingStatus.InProgress;
                                }

                                timerInfos.Add(timerInfo);
                                count = count + 1;
                            }
                            else
                            {
                                _logger.LogInformation("[VuPlus] ignoring timer " + e2name);
                            }
                        }
                        return timerInfos;
                    }
                    catch (Exception e)
                    {
                        _logger.LogError("[VuPlus] Failed to parse timer information.");
                        _logger.LogError(string.Format("[VuPlus] GetTimersAsync error: {0}", e.Message));
                        throw new ApplicationException("Failed to parse timer information.");
                    }
                }
            }
        }

        public Task<List<MediaSourceInfo>> GetChannelStreamMediaSources(string channelId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<List<MediaSourceInfo>> GetRecordingStreamMediaSources(string recordingId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Get the live channel stream, zap to channel if required.
        /// </summary>
        /// <param name="cancellationToken">The CancellationToken</param>
        /// <returns>MediaSourceInfo</returns>
        public async Task<MediaSourceInfo> GetChannelStream(string channelOid, string mediaSourceId, CancellationToken cancellationToken)
        {
            _logger.LogInformation("[VuPlus] Start GetChannelStream");

            var protocol = "http";
            if (Plugin.Instance.Configuration.UseSecureHTTPS)
            {
                protocol = "https";
            }

            var baseUrl = protocol + "://" + Plugin.Instance.Configuration.HostName + ":" + Plugin.Instance.Configuration.StreamingPort;

            //check if we need to zap to channel - single tuner
            if (Plugin.Instance.Configuration.ZapToChannel)
            {
                await ZapToChannel(cancellationToken, channelOid).ConfigureAwait(false);
            }

            _liveStreams++;
            var streamUrl = string.Format("{0}/{1}", baseUrl, channelOid);
            UtilsHelper.DebugInformation(_logger, string.Format("[VuPlus] GetChannelStream url: {0}", streamUrl));

            return new MediaSourceInfo
            {
                Id = _liveStreams.ToString(CultureInfo.InvariantCulture),
                Path = streamUrl,
                Protocol = MediaProtocol.Http,
                MediaStreams = new List<MediaStream>
                        {
                            new MediaStream
                            {
                                Type = MediaStreamType.Video,
                                // Set the index to -1 because we don't know the exact index of the video stream within the container
                                Index = -1,

                                // Set to true if unknown to enable deinterlacing
                                IsInterlaced = true

                            },
                            new MediaStream
                            {
                                Type = MediaStreamType.Audio,
                                // Set the index to -1 because we don't know the exact index of the audio stream within the container
                                Index = -1
                            }
                        }
            };
            throw new ResourceNotFoundException(string.Format("Could not stream channel {0}", channelOid));
        }


        /// <summary>
        /// zap to channel.
        /// </summary>
        /// <param name="cancellationToken">The CancellationToken</param>
        /// <param name="channelOid">The channel id</param>
        /// <returns></returns>
        public async Task ZapToChannel(CancellationToken cancellationToken, string channelOid)
        {
            _logger.LogInformation("[VuPlus] Start ZapToChannel");
            await EnsureConnectionAsync(cancellationToken).ConfigureAwait(false);

            var protocol = "http";
            if (Plugin.Instance.Configuration.UseSecureHTTPS)
            {
                protocol = "https";
            }

            var baseUrl = protocol + "://" + Plugin.Instance.Configuration.HostName + ":" + Plugin.Instance.Configuration.WebInterfacePort;

            var url = string.Format("{0}/web/zap?sRef={1}", baseUrl, channelOid);
            UtilsHelper.DebugInformation(_logger, string.Format("[VuPlus] ZapToChannel url: {0}", url));

            using (var stream = await _httpClient.GetStreamAsync(url, cancellationToken).ConfigureAwait(false))
            {
                using (var reader = new StreamReader(stream))
                {
                    var xmlResponse = reader.ReadToEnd();
                    UtilsHelper.DebugInformation(_logger, string.Format("[VuPlus] ZapToChannel response: {0}", xmlResponse));

                    try
                    {
                        var xml = new XmlDocument();
                        xml.LoadXml(xmlResponse);

                        var e2simplexmlresult = xml.GetElementsByTagName("e2simplexmlresult");
                        foreach (XmlNode xmlNode in e2simplexmlresult)
                        {
                            var recordingInfo = new RecordingInfo();

                            var e2state = "?";
                            var e2statetext = "?";

                            foreach (XmlNode node in xmlNode.ChildNodes)
                            {
                                if (node.Name == "e2state")
                                {
                                    e2state = node.InnerText;
                                }
                                else if (node.Name == "e2statetext")
                                {
                                    e2statetext = node.InnerText;
                                }
                            }

                            if (e2state != "True")
                            {
                                _logger.LogError("[VuPlus] Failed to zap to channel.");
                                _logger.LogError(string.Format("[VuPlus] ZapToChannel e2statetext: {0}", e2statetext));
                                throw new ApplicationException("Failed to zap to channel.");
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.LogError("[VuPlus] Failed to parse create timer information.");
                        _logger.LogError(string.Format("[VuPlus] ZapToChannel error: {0}", e.Message));
                        throw new ApplicationException("Failed to parse zap to channel information.");
                    }
                }
            }


        }


        /// <summary>
        /// Get new timer defaults.
        /// </summary>
        /// <param name="cancellationToken">The CancellationToken</param>
        /// <param name="program">null</param>
        /// <returns>SeriesTimerInfo</returns>
        public async Task<SeriesTimerInfo> GetNewTimerDefaultsAsync(CancellationToken cancellationToken, ProgramInfo program = null)
        {
            _logger.LogInformation("[VuPlus] Start GetNewTimerDefaultsAsync");

            var seriesTimerInfo = new SeriesTimerInfo();

            return seriesTimerInfo;
        }


        /// <summary>
        /// Get programs for specified channel within start and end date.
        /// </summary>
        /// <param name="channelId">channel id</param>
        /// <param name="startDateUtc">start date/time</param>
        /// <param name="endDateUtc">end date/time</param>
        /// <param name="cancellationToken">The CancellationToken</param>
        /// <returns>IEnumerable<ProgramInfo></returns>
        public async Task<IEnumerable<ProgramInfo>> GetProgramsAsync(string channelId, DateTime startDateUtc, DateTime endDateUtc, CancellationToken cancellationToken)
        {
            _logger.LogInformation("[VuPlus] Start GetProgramsAsync");
            await EnsureConnectionAsync(cancellationToken).ConfigureAwait(false);

            var rnd = new Random();

            var imagePath = "";
            var imageUrl = "";

            var protocol = "http";
            if (Plugin.Instance.Configuration.UseSecureHTTPS)
            {
                protocol = "https";
            }

            var baseUrl = protocol + "://" + Plugin.Instance.Configuration.HostName + ":" + Plugin.Instance.Configuration.WebInterfacePort;

            var url = string.Format("{0}/web/epgservice?sRef={1}", baseUrl, channelId);
            UtilsHelper.DebugInformation(_logger, string.Format("[VuPlus] GetProgramsAsync url: {0}", url));

            using (var stream = await _httpClient.GetStreamAsync(url, cancellationToken).ConfigureAwait(false))
            {
                using (var reader = new StreamReader(stream))
                {
                    var xmlResponse = reader.ReadToEnd();
                    UtilsHelper.DebugInformation(_logger, string.Format("[VuPlus] GetProgramsAsync response: {0}", xmlResponse));

                    try
                    {
                        var xml = new XmlDocument();
                        xml.LoadXml(xmlResponse);

                        var programInfos = new List<ProgramInfo>();

                        var count = 1;

                        var e2event = xml.GetElementsByTagName("e2event");
                        foreach (XmlNode xmlNode in e2event)
                        {
                            var programInfo = new ProgramInfo();

                            var e2eventid = "?";
                            var e2eventstart = "?";
                            var e2eventduration = "?";
                            var e2eventcurrenttime = "?";
                            var e2eventtitle = "?";
                            var e2eventdescription = "?";
                            var e2eventdescriptionextended = "?";
                            var e2eventservicereference = "?";
                            var e2eventservicename = "?";

                            foreach (XmlNode node in xmlNode.ChildNodes)
                            {
                                if (node.Name == "e2eventid")
                                {
                                    e2eventid = node.InnerText;
                                }
                                else if (node.Name == "e2eventstart")
                                {
                                    e2eventstart = node.InnerText;
                                }
                                else if (node.Name == "e2eventduration")
                                {
                                    e2eventduration = node.InnerText;
                                }
                                else if (node.Name == "e2eventcurrenttime")
                                {
                                    e2eventcurrenttime = node.InnerText;
                                }
                                else if (node.Name == "e2eventtitle")
                                {
                                    e2eventtitle = node.InnerText;
                                }
                                else if (node.Name == "e2eventdescription")
                                {
                                    e2eventdescription = node.InnerText;
                                }
                                else if (node.Name == "e2eventdescriptionextended")
                                {
                                    e2eventdescriptionextended = node.InnerText;
                                }
                                else if (node.Name == "e2eventservicereference")
                                {
                                    e2eventservicereference = node.InnerText;
                                }
                                else if (node.Name == "e2eventservicename")
                                {
                                    e2eventservicename = node.InnerText;
                                }
                            }

                            var sdated = long.Parse(e2eventstart);
                            var sdate = ApiHelper.DateTimeFromUnixTimestampSeconds(sdated);

                            // Check whether the current element is within the time range passed
                            if (sdate > endDateUtc)
                            {
                                UtilsHelper.DebugInformation(_logger, string.Format("[VuPlus] GetProgramsAsync epc full ending without adding channel name : {0} program : {1}", e2eventservicename, e2eventtitle));
                                return programInfos;
                            }
                            else
                            {
                                UtilsHelper.DebugInformation(_logger, string.Format("[VuPlus] GetProgramsAsync adding program for channel name : {0} program : {1}", e2eventservicename, e2eventtitle));
                                //programInfo.HasImage = false;
                                //programInfo.ImagePath = null;
                                //programInfo.ImageUrl = null;
                                if (count == 1)
                                {
                                    foreach (var channelInfo in tvChannelInfos)
                                    {
                                        if (channelInfo.Name == e2eventservicename)
                                        {
                                            UtilsHelper.DebugInformation(_logger, string.Format("[VuPlus] GetProgramsAsync match on channel name : {0}", e2eventservicename));
                                            //programInfo.HasImage = true;
                                            //programInfo.ImagePath = channelInfo.ImagePath;
                                            //programInfo.ImageUrl = channelInfo.ImageUrl;
                                            imagePath = channelInfo.ImagePath;
                                            imageUrl = channelInfo.ImageUrl;
                                            break;
                                        }
                                    }
                                }

                                programInfo.HasImage = true;
                                programInfo.ImagePath = imagePath;
                                programInfo.ImageUrl = imageUrl;

                                programInfo.ChannelId = e2eventservicereference;

                                // for some reason the Id appears to have to be unique so will make it so
                                programInfo.Id = e2eventservicereference + "~" + e2eventid + "~" + count + "~" + rnd.Next();

                                programInfo.Overview = e2eventdescriptionextended;

                                var edated = long.Parse(e2eventstart) + long.Parse(e2eventduration);
                                var edate = ApiHelper.DateTimeFromUnixTimestampSeconds(edated);

                                programInfo.StartDate = sdate.ToUniversalTime();
                                programInfo.EndDate = edate.ToUniversalTime();

                                var genre = new List<string>
                                {
                                    "Unknown"
                                };
                                programInfo.Genres = genre;

                                //programInfo.OriginalAirDate = null;
                                programInfo.Name = e2eventtitle;
                                //programInfo.OfficialRating = null;
                                //programInfo.CommunityRating = null;
                                //programInfo.EpisodeTitle = null;
                                //programInfo.Audio = null;
                                //programInfo.IsHD = false;
                                //programInfo.IsRepeat = false;
                                //programInfo.IsSeries = false;
                                //programInfo.IsNews = false;
                                //programInfo.IsMovie = false;
                                //programInfo.IsKids = false;
                                //programInfo.IsSports = false;

                                programInfos.Add(programInfo);
                                count = count + 1;
                            }
                        }
                        return programInfos;
                    }
                    catch (Exception e)
                    {
                        _logger.LogError("[VuPlus] Failed to parse program information.");
                        _logger.LogError(string.Format("[VuPlus] GetProgramsAsync error: {0}", e.Message));
                        throw new ApplicationException("Failed to parse channel information.");
                    }
                }
            }
        }


        /// <summary>
        /// Get server status info.
        /// </summary>
        /// <param name="cancellationToken">The CancellationToken</param>
        /// <returns>LiveTvServiceStatusInfo</returns>
        public async Task<LiveTvServiceStatusInfo> GetStatusInfoAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[VuPlus] Start GetStatusInfoAsync Async, retrieve status details");
            await EnsureConnectionAsync(cancellationToken).ConfigureAwait(false);

            //TODO: Version check

            var upgradeAvailable = false;
            var serverVersion = "Unknown";

            var protocol = "http";
            if (Plugin.Instance.Configuration.UseSecureHTTPS)
            {
                protocol = "https";
            }

            var baseUrl = protocol + "://" + Plugin.Instance.Configuration.HostName + ":" + Plugin.Instance.Configuration.WebInterfacePort;

            var url = string.Format("{0}/web/deviceinfo", baseUrl);
            UtilsHelper.DebugInformation(_logger, string.Format("[VuPlus] GetStatusInfoAsync url: {0}", url));

            var liveTvTunerInfos = new List<LiveTvTunerInfo>();

            using (var stream = await _httpClient.GetStreamAsync(url, cancellationToken).ConfigureAwait(false))
            {
                using (var reader = new StreamReader(stream))
                {
                    var xmlResponse = reader.ReadToEnd();
                    UtilsHelper.DebugInformation(_logger, string.Format("[VuPlus] GetStatusInfoAsync response: {0}", xmlResponse));

                    try
                    {
                        var xml = new XmlDocument();
                        xml.LoadXml(xmlResponse);

                        var e2frontend = xml.GetElementsByTagName("e2frontend");
                        foreach (XmlNode xmlNode in e2frontend)
                        {
                            var liveTvTunerInfo = new LiveTvTunerInfo();

                            var e2name = "?";
                            var e2model = "?";

                            foreach (XmlNode node in xmlNode.ChildNodes)
                            {
                                if (node.Name == "e2name")
                                {
                                    e2name = node.InnerText;
                                }
                                else if (node.Name == "e2model")
                                {
                                    e2model = node.InnerText;
                                }
                            }

                            liveTvTunerInfo.Id = e2model;
                            liveTvTunerInfo.Name = e2name;
                            liveTvTunerInfo.SourceType = "";

                            liveTvTunerInfos.Add(liveTvTunerInfo);
                        }

                        return new LiveTvServiceStatusInfo
                        {
                            HasUpdateAvailable = upgradeAvailable,
                            Version = serverVersion,
                            Tuners = liveTvTunerInfos
                        };

                    }
                    catch (Exception e)
                    {
                        _logger.LogError("[VuPlus] Failed to parse tuner information.");
                        _logger.LogError(string.Format("[VuPlus] GetStatusInfoAsync error: {0}", e.Message));
                        throw new ApplicationException("Failed to parse tuner information.");
                    }

                }
            }
        }


        /// <summary>
        /// Gets the homepage url.
        /// </summary>
        /// <value>The homepage url.</value>
        public string HomePageUrl => "http://www.VuPlus.com/";


        /// <summary>
        /// Gets the name.
        /// </summary>
        /// <value>The name.</value>
        public string Name => "VuPlus";


        public async Task<MediaSourceInfo> GetRecordingStream(string recordingId, string mediaSourceId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }


        public async Task CloseLiveStream(string id, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }


        public async Task CopyFilesAsync(StreamReader source, StreamWriter destination)
        {
            _logger.LogInformation("[VuPlus] Start CopyFiles Async");
            var buffer = new char[0x1000];
            int numRead;
            while ((numRead = await source.ReadAsync(buffer, 0, buffer.Length)) != 0)
            {
                await destination.WriteAsync(buffer, 0, numRead);
            }
        }


        public Task RecordLiveStream(string id, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }


        /// <summary>
        /// Get the recurrent recordings
        /// </summary>
        /// <param name="cancellationToken">The CancellationToken</param>
        /// <returns></returns>
        public async Task<IEnumerable<SeriesTimerInfo>> GetSeriesTimersAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[VuPlus] Start GetSeriesTimersAsync");

            var seriesTimerInfo = new List<SeriesTimerInfo>();
            return seriesTimerInfo;
        }


        /// <summary>
        /// Create a recurrent recording
        /// </summary>
        /// <param name="info">The recurrend program info</param>
        /// <param name="cancellationToken">The CancelationToken</param>
        /// <returns></returns>
        public async Task CreateSeriesTimerAsync(SeriesTimerInfo info, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }


        /// <summary>
        /// Update the series Timer
        /// </summary>
        /// <param name="info">The series program info</param>
        /// <param name="cancellationToken">The CancellationToken</param>
        /// <returns></returns>
        public async Task UpdateSeriesTimerAsync(SeriesTimerInfo info, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }


        /// <summary>
        /// Update a single Timer
        /// </summary>
        /// <param name="info">The program info</param>
        /// <param name="cancellationToken">The CancellationToken</param>
        /// <returns></returns>
        public async Task UpdateTimerAsync(TimerInfo info, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }


        /// <summary>
        /// Cancel the Series Timer
        /// </summary>
        /// <param name="timerId">The Timer Id</param>
        /// <param name="cancellationToken">The CancellationToken</param>
        /// <returns></returns>
        public async Task CancelSeriesTimerAsync(string timerId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }


        /// <summary>
        /// Get the DefaultScheduleSettings
        /// </summary>
        /// <param name="cancellationToken">The CancellationToken</param>
        /// <returns></returns>
        //private async Task<ScheduleSettings> GetDefaultScheduleSettings(CancellationToken cancellationToken)
        //{
        //    throw new NotImplementedException();
        //}


        public event EventHandler DataSourceChanged;


        public event EventHandler<RecordingStatusChangedEventArgs> RecordingStatusChanged;


        public Task ResetTuner(string id, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }


        public Task<ImageStream> GetChannelImageAsync(string channelId, CancellationToken cancellationToken)
        {
            // Leave as is. This is handled by supplying image url to ChannelInfo
            throw new NotImplementedException();
        }


        public Task<ImageStream> GetProgramImageAsync(string programId, string channelId, CancellationToken cancellationToken)
        {
            // Leave as is. This is handled by supplying image url to ProgramInfo
            throw new NotImplementedException();
        }


        public Task<ImageStream> GetRecordingImageAsync(string recordingId, CancellationToken cancellationToken)
        {
            // Leave as is. This is handled by supplying image url to RecordingInfo
            throw new NotImplementedException();
        }

    }

}
