using Common;


namespace LakeBtcBot
{
    public class ArbitrageTrader : TraderBase
    {
        private string _baseCurrency;
        private string _arbCurrency;
        private double _parity;
        private double _arbFactor = 1.007;        //The price of arbitrage currency must be at least 0.7% higher than parity to buy

        private LakeBtcApi _requestor;

        public ArbitrageTrader(Logger logger)
            : base(logger)
        { }

        protected override void Initialize()
        {
            _baseCurrency = Configuration.GetValue("base_currency_code");
            _arbCurrency = Configuration.GetValue("arbitrage_currency_code");

            _parity = double.Parse(Configuration.GetValue("parity_ratio"));
            _arbFactor = double.Parse(Configuration.GetValue("profit_factor"));
            _intervalMs = 8000;

            _requestor = new LakeBtcApi(_logger, _baseCurrency, _arbCurrency);
            log("LakeBTC arbitrage trader started for currencies {0}, {1} with parity={2:0.000}; profit factor={3}", _baseCurrency, _arbCurrency, _parity, _arbFactor);
        }

        protected override void Check()
        {
            var baseMarket = _requestor.GetMarketDepth(_baseCurrency);
            var arbMarket = _requestor.GetMarketDepth(_arbCurrency);

            var lowestBaseAskPrice = baseMarket.Asks[0].Price;
            var highestArbBidPrice = arbMarket.Bids[0].Price;
            double baseRatio = highestArbBidPrice / lowestBaseAskPrice;

            var lowestArbAskPrice = arbMarket.Asks[0].Price;
            var highestBaseBidPrice = baseMarket.Bids[0].Price;
            var arbRatio = lowestArbAskPrice / highestBaseBidPrice;

            log("BASIC ratio={0:0.00000}; ARB ratio={1:0.00000}", baseRatio, arbRatio);

            log(new string('=', 70));
        }
    }
}
