using System;
using System.Collections.Generic;
using System.Linq;
using Common;


namespace RippleBot
{
    //TODO: so far only report ratios

    /// <summary>
    /// Do arbitrage between 2 fiat currencies. Watch for over-xrp-ratio and if it changes in behoof of currency we
    /// hold (i.e. it becomes more "expensive"), do 2 trades: to XRP and then to the other fiat currency. Then wait to
    /// buy back.
    /// </summary>
    public class ArbitrageTrader : TraderBase
    {
        private string baseCurrency = "USD";       //TODO: input
        private string arbCurrency = "CNY";
        private string baseGateway = "rMwjYedjc7qqtKYVLiAccJSmCwih4LnE2q"; //SnapSwap TODO: input
        private string arbGateway = "rnuF96W4SZoCJmbHYBFoJZpR8eCaxNvekK";  //RippleCN

        //TODO: find and incorporate gateway fees (RTJ has around 1%)
        private double _baseFeeFactor = 0.0;
        private double _arbFeeFactor = 0.00;    //0%

        private List<double> _ratioHistory = new List<double>();
        private const int MIN_HISTORY = 30;

        private readonly RippleApi _baseRequestor;
        private readonly RippleApi _arbRequestor;



        public ArbitrageTrader(Logger logger)
            : base(logger)
        {
//            _currencyCode = Configuration.GetValue("base_currency_code");
//            _currencyCode = Configuration.GetValue("arbitrage_currency_code");

            _intervalMs = 8000;    //TODO: When trying to return back to basic state, be more frequent

            _baseRequestor = new RippleApi(logger, baseGateway, baseCurrency);
            _baseRequestor.Init();
            _arbRequestor = new RippleApi(logger, arbGateway, arbCurrency);
            _arbRequestor.Init();
            log("Arbitrage trader started for currencies {0}, {1}", baseCurrency, arbCurrency);
        }


        protected override void Check()
        {
            var baseMarket = _baseRequestor.GetMarketDepth();

            if (null == baseMarket)
                return;

            var arbMarket = _arbRequestor.GetMarketDepth();

            if (null == arbMarket)
                return;

            var baseBalance = _baseRequestor.GetBalance(baseCurrency);
            log("Balance: {0} {1}", baseBalance, baseCurrency);

            var arbBalance = _arbRequestor.GetBalance(arbCurrency);
            log("Balance: {0} {1}", arbBalance, arbCurrency);

//TODO            if (!baseBalance.eq(0.0, 0.08))         //TODO: NO! the buy-back order might be partially filled, this tells nothing
            {
                //Basic state, check for conversion to arbitrage currency
                var lowestAskPrice = baseMarket.Asks[0].Price;
                var highestBidPrice = arbMarket.Bids[0].Price;
                double baseRatio = highestBidPrice / lowestAskPrice;

                log("[BASIC] ratio is {0}:{1} ({2})", lowestAskPrice, highestBidPrice, baseRatio);

                if (double.IsNaN(baseRatio))
                    return;     //Happens sometimes after bad JSON parsing

                if (_ratioHistory.Count < MIN_HISTORY)
                {
                    _ratioHistory.Add(baseRatio);
                    log("Not enough data...");
                    return;
                }

                double minPastRatio = Double.MaxValue;
                foreach (var pastRatio in _ratioHistory)
                {
                    if (pastRatio < minPastRatio)
                        minPastRatio = pastRatio;
                }

                _ratioHistory.RemoveAt(0);
                _ratioHistory.Add(baseRatio);

                if (baseRatio > minPastRatio * (1.0 + 2.0 * _arbFeeFactor))
                {
                    log("Possible arbitrage oportunity", ConsoleColor.Cyan);

                    if (lowestAskPrice * baseMarket.Asks[0].Amount < baseBalance)
                    {
                        log("Unsufficient volume {0} XRP", baseMarket.Asks[0].Amount);
                        return;
                    }

                    //Buy XRP for base currency
//TODO                    _baseRequestor.PlaceBuyOrder(lowestAskPrice + 1.0/*that makes it market order*/, baseMarket.Asks[0].Amount);
//                    log("Bought XRP...", ConsoleColor.Cyan);
//                    var amount = arbMarket.Bids[0].Amount;
//                    _arbRequestor.PlaceSellOrder(highestBidPrice - 1.0, ref amount);
//                    log("Sold all XRP");
                }
            }

            

            var lowestArbAskPrice = arbMarket.Asks[0].Price;
            var highestArbBidPrice = baseMarket.Bids[0].Price;
            var arbRatio = lowestArbAskPrice / highestArbBidPrice;

            log("[ARB] ratio is {0}:{1} ({2})", lowestArbAskPrice, highestArbBidPrice, arbRatio);
        }
    }
}
