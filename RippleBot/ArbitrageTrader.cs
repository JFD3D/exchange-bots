using System;

using Common;
using RippleBot.Business.DataApi;


namespace RippleBot
{
    /// <summary>
    /// Do arbitrage between 2 fiat currencies using fixed conversion setting. Watch for over-xrp-ratio and if it
    /// changes in behoof of currency we hold (i.e. it becomes more "expensive"), do 2 trades: to XRP and then
    /// to the other fiat currency. Then wait to buy back with profit.
    /// </summary>
    public class ArbitrageTrader : TraderBase
    {
        private string _baseCurrency;
        private string _arbCurrency;
        private string _baseGateway;
        private string _arbGateway;
        private double _parity;
        private double _arbFactor = 1.007;              //The price of arbitrage currency must be at least 0.7% higher than parity to buy (if not configured)
        private const double MIN_TRADE_VOLUME = 1.0;    //Minimum trade volume in XRP so we don't lose on fees

        private const int ZOMBIE_CHECK = 12;            //Check for dangling orders to cancel every 12th round
        private int _counter;

        //I need to watch XRP balance to revert filled abandoned fiat->XRP orders
        private double _lastValidXrpBalance = -1.0;

        private RippleApi _baseRequestor;      //TODO: No! Use only one requestor, gateway is input param
        private RippleApi _arbRequestor;



        public ArbitrageTrader(Logger logger)
            : base(logger)
        { }

        protected override void Initialize()
        {
            _baseCurrency = Configuration.GetValue("base_currency_code");
            _baseGateway = Configuration.GetValue("base_gateway_address");

            _arbCurrency = Configuration.GetValue("arbitrage_currency_code");
            _arbGateway = Configuration.GetValue("arbitrage_gateway_address");

            _parity = double.Parse(Configuration.GetValue("parity_ratio"));
            _arbFactor = double.Parse(Configuration.GetValue("profit_factor"));
            _intervalMs = 8000;

            _baseRequestor = new RippleApi(_logger, _baseGateway, _baseCurrency);
            _baseRequestor.Init();
            _arbRequestor = new RippleApi(_logger, _arbGateway, _arbCurrency);
            _arbRequestor.Init();
            log("Arbitrage trader started for currencies {0}, {1} with parity={2:0.000}; profit factor={3}", _baseCurrency, _arbCurrency, _parity, _arbFactor);
        }

        protected override void Check()
        {
            var baseMarket = _baseRequestor.GetMarketDepth();

            if (null == baseMarket || null == baseMarket.Asks || null == baseMarket.Bids)
            {
                return;
            }

            var arbMarket = _arbRequestor.GetMarketDepth();

            if (null == arbMarket || null == arbMarket.Asks || null == arbMarket.Bids)
            {
                return;
            }

            double baseBalance = _baseRequestor.GetBalance2(_baseCurrency, _baseGateway);
            double arbBalance = _arbRequestor.GetBalance2(_arbCurrency, _arbGateway);
            double xrpBalance = _baseRequestor.GetXrpBalance();
            log("Balances: {0:0.000} {1}; {2:0.000} {3}; {4:0.000} XRP", baseBalance, _baseCurrency, arbBalance, _arbCurrency, xrpBalance);

            var lowestBaseAsk = TradeHelper.GetFirstLiquidOrder(baseMarket.Asks, MIN_TRADE_VOLUME);
            var highestArbBid = TradeHelper.GetFirstLiquidOrder(arbMarket.Bids, MIN_TRADE_VOLUME);
            double baseRatio = highestArbBid.Price / lowestBaseAsk.Price;

            var lowestArbAsk = TradeHelper.GetFirstLiquidOrder(arbMarket.Asks, MIN_TRADE_VOLUME);
            var highestBaseBid = TradeHelper.GetFirstLiquidOrder(baseMarket.Bids, MIN_TRADE_VOLUME);
            double arbRatio = lowestArbAsk.Price / highestBaseBid.Price;

            log("BASIC ratio={0:0.00000}; ARB ratio={1:0.00000}", baseRatio, arbRatio);

            if (double.IsNaN(baseRatio) || double.IsNaN(arbRatio))
            {
                return;     //Happens sometimes after bad JSON parsing
            }

            //Trade from basic to arbitrage currency
            if (baseBalance >= 0.1)
            {
                if (baseRatio > _parity * _arbFactor)
                {
                    if (baseRatio > _parity * 1.15 || baseRatio < _parity * 0.9)
                    {
                        log("BASIC ratio has suspicious value {0:0.00000}. Let's leave it be", ConsoleColor.Yellow, baseRatio);
                        return;
                    }

                    log("Chance to buy cheap {0} (BASIC ratio {1:0.00000} > {2:0.00000})", ConsoleColor.Cyan, _arbCurrency, baseRatio, _parity*_arbFactor);
                    double baseVolume = lowestBaseAsk.Amount;
                    double arbVolume = highestArbBid.Amount;

                    //Try to buy XRP for BASIC
                    double amount = Math.Min(baseVolume, arbVolume);
                    int orderId = _baseRequestor.PlaceBuyOrder(lowestBaseAsk.Price + 0.00001, amount);
                    log("Tried to buy {0} XRP for {1} {2} each. OrderID={3}", amount, lowestBaseAsk.Price, _baseCurrency, orderId);
                    Order orderInfo = _baseRequestor.GetOrderInfo2(orderId);

                    if (null != orderInfo && orderInfo.Closed)
                    {
                        var newXrpBalance = _baseRequestor.GetXrpBalance();
                        amount = newXrpBalance - xrpBalance;
                        log("Buy XRP orderID={0} filled OK, bought {1} XRP", ConsoleColor.Green, orderId, amount);
                        amount -= 0.048;    //So we don't fall into "lack of funds" due to fees
                        //Try to sell XRP for ARB
                        int arbBuyOrderId = _arbRequestor.PlaceSellOrder(highestArbBid.Price * 0.9, ref amount);     //price*0.9 basically does market order
                        log("Tried to sell {0} XRP for {1} {2} each. OrderID={3}", amount, highestArbBid.Price, _arbCurrency, arbBuyOrderId);
                        Order arbBuyOrderInfo = _arbRequestor.GetOrderInfo2(arbBuyOrderId);
                        if (null != arbBuyOrderInfo && arbBuyOrderInfo.Closed)
                        {
                            log("Buy {0} orderID={1} filled OK", ConsoleColor.Green, _arbCurrency, arbBuyOrderId);
                            log("{0} -> {1} ARBITRAGE SUCCEEDED!", ConsoleColor.Green, _baseCurrency, _arbCurrency);
                        }
                        else if (null != arbBuyOrderInfo)
                        {
                            log("OrderID={0} (sell {1:0.000} XRP for {2} {3} each) remains dangling. Forgetting it...", ConsoleColor.Yellow,
                                arbBuyOrderId, arbBuyOrderInfo.Amount(Const.NATIVE_ASSET), arbBuyOrderInfo.BuyPrice(_arbCurrency, _arbGateway), _arbCurrency);
                            //NOTE: If it's closed later, the arbitrage is just successfully finished silently
                        }
                        else
                        {
                            //TODO: data API is unreliable 
                            log("Couldn't get data for buy OrderID={0}. TODO: drop data API, revert to ws?", ConsoleColor.Yellow, arbBuyOrderId);
                        }
                    }
                    else if (null != orderInfo)
                    {
                        log("OrderID={0} (buy {1:0.000} XRP for {2} {3} each) remains dangling. Trying to cancel...", ConsoleColor.Yellow,
                            orderId, orderInfo.Amount(Const.NATIVE_ASSET), orderInfo.BuyPrice(Const.NATIVE_ASSET), _baseCurrency);
                        if (_baseRequestor.CancelOrder(orderId))
                        {
                            log("...success?", ConsoleColor.Cyan);
                        }
                        else
                        {
                            log("...failed", ConsoleColor.Cyan);
                        }
                    }
                }
            }

            if (arbBalance >= 0.1)
            {
                if (arbRatio < _parity)
                {
                    if (arbRatio > _parity * 1.15 || arbRatio < _parity * 0.9)
                    {
                        log("ARB ratio has suspicious value {0:0.00000}. Let's leave it be", ConsoleColor.Yellow, arbRatio);
                        return;
                    }

                    log("Chance to sell {0} for {1} (ARB ratio {2:0.00000} < {3:0.00000})", ConsoleColor.Cyan, _arbCurrency, _baseCurrency, arbRatio, _parity);
                    double arbVolume = lowestArbAsk.Amount;
                    double baseVolume = highestBaseBid.Amount;

                    //Try to buy XRP for ARB
                    double amount = Math.Min(baseVolume, arbVolume);
                    int orderId = _arbRequestor.PlaceBuyOrder(lowestArbAsk.Price + 0.00001, amount);
                    log("Tried to buy {0} XRP for {1} {2} each. OrderID={3}", amount, lowestArbAsk.Price, _arbCurrency, orderId);
                    Order orderInfo = _arbRequestor.GetOrderInfo2(orderId);

                    if (null != orderInfo && orderInfo.Closed)
                    {
                        var newXrpBalance = _arbRequestor.GetXrpBalance();
                        amount = newXrpBalance - xrpBalance;
                        log("Buy XRP orderID={0} filled OK, bought {1} XRP", ConsoleColor.Green, orderId, amount);
                        //Try to sell XRP for BASIC
                        var baseBuyOrderId = _baseRequestor.PlaceSellOrder(highestBaseBid.Price * 0.9, ref amount);      //price*0.9 basically does market order
                        log("Tried to sell {0} XRP for {1} {2} each. OrderID={3}", amount, highestBaseBid.Price, _baseCurrency, baseBuyOrderId);
                        Order baseBuyOrderInfo = _baseRequestor.GetOrderInfo2(baseBuyOrderId);
                        if (null != baseBuyOrderInfo && baseBuyOrderInfo.Closed)
                        {
                            log("Buy {0} orderID={1} filled OK", ConsoleColor.Green, _baseCurrency, baseBuyOrderId);
                            log("{0} -> {1} ARBITRAGE SUCCEEDED!", ConsoleColor.Green, _arbCurrency, _baseCurrency);
                        }
                        else if (null != baseBuyOrderInfo)
                        {
                            log("OrderID={0} (sell {1:0.000} XRP for {2} {3} each) remains dangling. Forgetting it...", ConsoleColor.Yellow,
                                baseBuyOrderId, baseBuyOrderInfo.Amount(Const.NATIVE_ASSET), baseBuyOrderInfo.BuyPrice(_baseCurrency, _baseGateway), _baseCurrency);
                            //NOTE: If it's closed later, the arbitrage is just successfully finished silently
                        }
                    }
                    else if (null != orderInfo)
                    {
                        log("OrderID={0} (buy {1:0.000} XRP for {2} {3} each) remains dangling. Trying to cancel...", ConsoleColor.Yellow,
                            orderId, orderInfo.Amount(Const.NATIVE_ASSET), orderInfo.BuyPrice(Const.NATIVE_ASSET), _arbCurrency);
                        if (_arbRequestor.CancelOrder(orderId))
                        {
                            log("...success?", ConsoleColor.Cyan);
                        }
                        else
                        {
                            log("...failed", ConsoleColor.Cyan);
                        }
                    }
                }
            }

            //Clear any dangling orders
            if (++_counter == ZOMBIE_CHECK)
            {
                _counter = 0;
                _baseRequestor.CleanupZombies(-1, -1);
            }

            //Change any extra XRP balance to a fiat. Any conversion is better than staying in XRP and potential loss of value.
            if (_lastValidXrpBalance > 0.0 && xrpBalance - 2.0 > _lastValidXrpBalance)
            {
                var amount = xrpBalance - _lastValidXrpBalance;
                log("Balance {0:0.000} XRP is too high. Must convert {1:0.000} to fiat.", ConsoleColor.Yellow, xrpBalance, amount);

                //Check which fiat is better now
                if (baseRatio > _parity * _arbFactor)
                {
                    log("Converting to {0}", _arbCurrency);
                    _arbRequestor.PlaceSellOrder(highestArbBid.Price * 0.9, ref amount);
                }
                else if (arbRatio < _parity)
                {
                    log("Converting to {0}", _baseCurrency);
                    _baseRequestor.PlaceSellOrder(highestBaseBid.Price * 0.9, ref amount);
                }
                else
                {
                    double baseDiffFromSell = (_parity * _arbFactor) - baseRatio;
                    double arbDiffFromBuyback = arbRatio - _parity;

                    if (baseDiffFromSell < arbDiffFromBuyback)
                    {
                        log("Better converting to {0}", _arbCurrency);
                        _arbRequestor.PlaceSellOrder(highestArbBid.Price * 0.9, ref amount);
                    }
                    else
                    {
                        log("Better converting to {0}", _baseCurrency);
                        _baseRequestor.PlaceSellOrder(highestBaseBid.Price * 0.9, ref amount);
                    }
                }
            }
            else if (xrpBalance > -1.0)
            {
                _lastValidXrpBalance = xrpBalance;
            }

            log(new string('=', 70));
        }
    }
}
