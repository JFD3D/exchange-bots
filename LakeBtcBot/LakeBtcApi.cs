using System;
using System.Net;
using System.Threading;
using Common;
using LakeBtcBot.Business;


namespace LakeBtcBot
{
    internal class LakeBtcApi
    {
        private const string BASE_URL = "https://www.LakeBTC.com/api_v1/";
        private const byte RETRY_COUNT = 6;
        private const int RETRY_DELAY = 1000;

        private readonly string _baseCurrency;
        private readonly string _arbCurrency;

        private readonly Logger _logger;
//del?        private readonly long _nonceOffset;
        private readonly WebProxy _webProxy;


        public LakeBtcApi(Logger logger, string baseCurrencyCode, string arbCurrencyCode)
        {
            _logger = logger;
            var proxyHost = Configuration.GetValue("proxyHost");
            var proxyPort = Configuration.GetValue("proxyPort");
            if (null != proxyHost && null != proxyPort)
            {
                _webProxy = new WebProxy(proxyHost, int.Parse(proxyPort));
                _webProxy.Credentials = CredentialCache.DefaultCredentials;
            }
/*TODO:del?
            var nonceOffset = Configuration.GetValue("nonce_offset");
            if (!String.IsNullOrEmpty(nonceOffset))
                _nonceOffset = long.Parse(nonceOffset);*/
        }


        internal MarketDepthResponse GetMarketDepth(string currencyCode)
        {
            if ("usd" == currencyCode.ToLower())
            {
                currencyCode = "";
            }
            else
            {
                currencyCode = "_" + currencyCode.ToLower();
            }

            var data = sendGetRequest(String.Format("{0}bcorderbook{1}", BASE_URL, currencyCode));
            return Helpers.DeserializeJSON<MarketDepthResponse>(data);
        }




        #region private helpers

        private string sendGetRequest(string url)
        {
            var client = new WebClient();

            if (null != _webProxy)
                client.Proxy = _webProxy;

            WebException exc = null;
            var delay = 0;
            for (int i = 1; i <= RETRY_COUNT; i++)
            {
                delay += RETRY_DELAY;
                try
                {
                    var text = client.DownloadString(url);
                    _logger.LastResponse = text;
                    return text;
                }
                catch (WebException we)
                {
                    var text = String.Format("(ATTEMPT {0}/{1}) Web request failed with exception={2}; status={3}", i, RETRY_COUNT, we.Message, we.Status);
                    _logger.AppendMessage(text, true, ConsoleColor.Yellow);
                    exc = we;
                    Thread.Sleep(delay);
                }
            }

            throw new Exception(String.Format("Web request failed {0} times in a row with error '{1}'. Giving up.", RETRY_COUNT, exc.Message));
        }


        #endregion
    }
}
