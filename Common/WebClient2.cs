using System;
using System.Net;


namespace Common
{
    public class WebClient2 : WebClient
    {
        private readonly int _timeout;
        private readonly Logger _logger;
        private readonly IWebProxy _webProxy;
        private WebRequest _request;


        /// <summary>Create new WebClient2 instance</summary>
        /// <param name="logger">Logger for (mostly debugging) messages</param>
        /// <param name="timeout">Timeout in miliseconds</param>
        public WebClient2(Logger logger, int timeout)
        {
            _logger = logger;
            _timeout = timeout;

            var proxyHost = Configuration.GetValue("proxyHost");
            var proxyPort = Configuration.GetValue("proxyPort");
            if (null != proxyHost && null != proxyPort)
            {
                _webProxy = new WebProxy(proxyHost, int.Parse(proxyPort));
                _webProxy.Credentials = CredentialCache.DefaultCredentials;
            }
        }

        protected override WebRequest GetWebRequest(Uri uri)
        {
            _request = base.GetWebRequest(uri);
            _request.Timeout = _timeout;

            if (null != _webProxy)
            {
                _request.Proxy = _webProxy;
            }

            return _request;
        }


        /// <summary>Get or set the protocol method to use in underlying request.</summary>
        public string Method
        {
            get { return _request.Method; }
            set { _request.Method = value; }
        }

        public string DownloadStringSafe(string url)
        {
            try
            {
                var data = DownloadString(url);
                _logger.LastResponse = data;
                return data;
            }
            catch (Exception e)
            {
                _logger.AppendMessage("Error downloading string. Message=" + e.Message, true, ConsoleColor.Yellow);
                return null;
            }
        }

        public T DownloadObject<T>(string url) where T : new()
        {
            try
            {
                var data = DownloadString(url);
                _logger.LastResponse = data;
                return Helpers.DeserializeJSON<T>(data);
            }
            catch (Exception e)
            {
                _logger.AppendMessage("Error downloading string. Message=" + e.Message, true, ConsoleColor.Yellow);
                return default(T);
            }
        }
    }
}
