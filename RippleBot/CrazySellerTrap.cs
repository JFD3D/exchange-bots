﻿using System;
using System.Linq;

using Common;
using RippleBot.Business;
using RippleBot.Business.DataApi;


namespace RippleBot
{
    /// <summary>
    /// General CST strategy for Ripple network. Particular ripple account, gateway and currency pair are
    /// parameters given by configuration.
    /// </summary>
    internal class CrazySellerTrap : TraderBase
    {
        private RippleApi _requestor;

        //XRP amount to trade
        private double _operativeAmount;
        private double _minWallVolume;
        private double _maxWallVolume;
        private string _gateway;
        //Volumen of XRP necessary to accept our offer
        private double _volumeWall;
        //Minimum difference between BUY price and subsequent SELL price (so we have at least some profit). Value from config.
        private double _minDifference;
        //Tolerance of BUY price. Usefull if possible price change is minor, to avoid frequent order updates. Value from config.
        private double _minPriceUpdate;    //fiat/XRP
        private const double MIN_ORDER_AMOUNT = 0.5;
        private string _currencyCode;

        private const int ZOMBIE_CHECK = 10;            //Check for dangling orders to cancel every 10th round
        private int _counter;

        //Active BUY order ID
        private int _buyOrderId = -1;
        //Active BUY order amount
        private double _buyOrderAmount;
        //Active BUY order price
        private double _buyOrderPrice;

        //Active SELL order ID
        private int _sellOrderId = -1;
        //Active SELL order amount
        private double _sellOrderAmount;
        //Active SELL order price
        private double _sellOrderPrice;
        //The price at which we bought from crazy buyer
        private double _executedBuyPrice = -1.0;

        private double _xrpBalance;


        public CrazySellerTrap(Logger logger) : base(logger)
        { }


        protected override void Initialize()
        {
            _counter = 0;
            _operativeAmount = double.Parse(Configuration.GetValue("operative_amount"));
            _minWallVolume = double.Parse(Configuration.GetValue("min_volume"));
            _maxWallVolume = double.Parse(Configuration.GetValue("max_volume"));
            _gateway = Configuration.GetValue("gateway_address");
            if (null == _gateway)
            {
                throw new Exception("Configuration key 'gateway_address' missing");
            }
            _currencyCode = Configuration.GetValue("currency_code");
            if (null == _currencyCode)
            {
                throw new Exception("Configuration key 'currency_code' missing");
            }
            _minDifference = double.Parse(Configuration.GetValue("trade_spread"));
            _minPriceUpdate = double.Parse(Configuration.GetValue("min_price_update"));
            var cleanup = Configuration.GetValue("cleanup_zombies");
            _cleanup = bool.Parse(cleanup ?? false.ToString());
            log("Zombie cleanup: " + cleanup);

            string dataApiUrl = Configuration.GetValue("data_api_url");
            if (String.IsNullOrEmpty(dataApiUrl))
            {
                throw new Exception("Configuration value data_api_url not found!");
            }

            _requestor = new RippleApi(_logger, dataApiUrl, _gateway, _currencyCode);
            _requestor.Init();
            log("CST trader started for currency {0} with operative={1}; MinWall={2}; MaxWall={3}",
                _currencyCode, _operativeAmount, _minWallVolume, _maxWallVolume);
        }

        protected override void Check()
        {
            ExchangeHistoryResponse tradeHistory = _requestor.GetTradeStatistics(Const.NATIVE_ASSET, null, _currencyCode, _gateway);
            var market = _requestor.GetMarketDepth();

            if (null == market)
            {
                return;
            }

            float coef = TradeHelper.GetMadness(tradeHistory, Const.NATIVE_ASSET, 500.0, 3000.0);
            _volumeWall = Helpers.SuggestWallVolume(coef, _minWallVolume, _maxWallVolume);
            _intervalMs = Helpers.SuggestInterval(coef, 8000, 20000);
            log("Madness={0}; Volume={1} XRP; Interval={2} ms", coef, _volumeWall, _intervalMs);

            //Cancel abandoned ordes if needed
            if (_cleanup && _counter++ % ZOMBIE_CHECK == 0)
            {
                _requestor.CleanupZombies(_buyOrderId, _sellOrderId);
            }

            //We have active BUY order
            if (-1 != _buyOrderId)
            {
                Order buyOrder = _requestor.GetOrderInfo2(_buyOrderId);

                if (null == buyOrder)
                {
                    return;
                }

                //The order is still open
                if (!buyOrder.Closed)
                {
                    //Untouched
                    if (buyOrder.Amount(Const.NATIVE_ASSET).eq(_buyOrderAmount))
                    {
                        log("BUY order ID={0} untouched (amount={1} XRP, price={2} {3})", _buyOrderId, _buyOrderAmount, _buyOrderPrice, _currencyCode);

                        double price = suggestBuyPrice(market);
                        var newAmount = _operativeAmount - _sellOrderAmount;

                        //Evaluate and update if needed
                        if (newAmount > _buyOrderAmount || !_buyOrderPrice.eq(price))
                        {
                            _buyOrderAmount = newAmount;
                            _buyOrderId = _requestor.UpdateBuyOrder(_buyOrderId, price, newAmount);
                            _buyOrderPrice = price;
                            log("Updated BUY order ID={0}; amount={1} XRP; price={2} {3}", _buyOrderId, _buyOrderAmount, price, _currencyCode);
                        }
                    }
                    else    //Partially filled
                    {
                        _executedBuyPrice = _buyOrderPrice; //TODO:DEL  buyOrder.BuyPrice(Const.NATIVE_ASSET);
                        _buyOrderAmount = buyOrder.Amount(Const.NATIVE_ASSET);
                        log("BUY order ID={0} partially filled at price={1} {2}. Remaining amount={3} XRP;",
                            ConsoleColor.Green, _buyOrderId, _executedBuyPrice, _currencyCode, buyOrder.Amount(Const.NATIVE_ASSET));

                        //Check remaining amount, drop the BUY if it's very tiny
                        if (buyOrder.Amount(Const.NATIVE_ASSET) < MIN_ORDER_AMOUNT)
                        {
                            log("The remaining BUY amount is too small, canceling the order ID={0}", ConsoleColor.Cyan, _buyOrderId);
                            _requestor.CancelOrder(_buyOrderId);    //Note: no problem if the cancel fails, the breadcrumbs can live own life
                            _executedBuyPrice = _buyOrderPrice;
                            _buyOrderId = -1;
                            _buyOrderAmount = 0.0;
                        }
                        else
                        {
                            var price = suggestBuyPrice(market);
                            //The same price is totally unlikely, so we don't check it here
                            _buyOrderId = _requestor.UpdateBuyOrder(_buyOrderId, price, buyOrder.Amount(Const.NATIVE_ASSET));
                            _buyOrderPrice = price;
                            log("Updated BUY order ID={0}; amount={1} XRP; price={2} {3}", _buyOrderId, _buyOrderAmount, _buyOrderPrice, _currencyCode);
                        }
                    }
                }
                else
                {
                    //Check if cancelled by Ripple due to "lack of funds"
                    var balance = _requestor.GetXrpBalance();
                    if (balance.eq(_xrpBalance, 0.1))
                    {
                        log("BUY order ID={0} closed but asset validation failed (balance={1} XRP). Asuming was cancelled, trying to recreate",
                            ConsoleColor.Yellow, _buyOrderId, balance);
                        _buyOrderPrice = suggestBuyPrice(market);
                        _buyOrderId = _requestor.PlaceBuyOrder(_buyOrderPrice, _buyOrderAmount);

                        if (-1 != _buyOrderId)
                        {
                            log("Successfully created BUY order with ID={0}; amount={1} XRP; price={2} {3}",
                                ConsoleColor.Cyan, _buyOrderId, _buyOrderAmount, _buyOrderPrice, _currencyCode);
                        }
                    }
                    else
                    {
                        _executedBuyPrice = _buyOrderPrice;
                        log("BUY order ID={0} (amount={1} XRP) was closed at price={2} {3}",
                            ConsoleColor.Green, _buyOrderId, _buyOrderAmount, _executedBuyPrice, _currencyCode);
                        _buyOrderId = -1;
                        _buyOrderAmount = 0;
                    }
                }
            }
            else if (_operativeAmount - _sellOrderAmount > 0.00001)    //No BUY order (and there are some money available). So create one
            {
                _buyOrderPrice = suggestBuyPrice(market);
                _buyOrderAmount = _operativeAmount - _sellOrderAmount;
                _buyOrderId = _requestor.PlaceBuyOrder(_buyOrderPrice, _buyOrderAmount);

                if (-1 != _buyOrderId)
                {
                    log("Successfully created BUY order with ID={0}; amount={1} XRP; price={2} {3}",
                        ConsoleColor.Cyan, _buyOrderId, _buyOrderAmount, _buyOrderPrice, _currencyCode);
                }
            }

            //Handle SELL order
            if (_operativeAmount - _buyOrderAmount > 0.00001)
            {
                //SELL order already existed
                if (-1 != _sellOrderId)
                {
                    Order sellOrder = _requestor.GetOrderInfo2(_sellOrderId);

                    if (null == sellOrder)
                    {
                        return;
                    }

                    //The order is still open
                    if (!sellOrder.Closed)
                    {
                        log("SELL order ID={0} open (amount={1} XRP, price={2} {3})", _sellOrderId, sellOrder.Amount(Const.NATIVE_ASSET), _sellOrderPrice, _currencyCode);

                        double price = suggestSellPrice(market);

                        //Partially filled
                        if (!sellOrder.Amount(Const.NATIVE_ASSET).eq(_sellOrderAmount))
                        {
                            log("SELL order ID={0} partially filled at price={1} {2}. Remaining amount={3} XRP;",
                                ConsoleColor.Green, _sellOrderId, /*TODO:DEL 1.0/sellOrder.BuyPrice(_currencyCode)*/_sellOrderPrice, _currencyCode, sellOrder.Amount(Const.NATIVE_ASSET));

                            //Check remaining amount, drop the SELL if it's very tiny
                            if (sellOrder.Amount(Const.NATIVE_ASSET) < MIN_ORDER_AMOUNT)
                            {
                                log("The remaining SELL amount is too small, canceling the order ID={0}", ConsoleColor.Cyan, _sellOrderId);
                                _requestor.CancelOrder(_sellOrderId);    //Note: no problem if the cancel fails, the breadcrumbs can live own life
                                _sellOrderId = -1;
                                _sellOrderAmount = 0.0;
                            }
                            else
                            {
                                var amount = sellOrder.Amount(Const.NATIVE_ASSET);
                                _sellOrderId = _requestor.UpdateSellOrder(_sellOrderId, price, ref amount);
                                _sellOrderAmount = amount;
                                _sellOrderPrice = price;
                                log("Updated SELL order ID={0}; amount={1} XRP; price={2} {3}", _sellOrderId, _sellOrderAmount, price, _currencyCode);
                            }
                        }
                        //If there were some money released by filling a BUY order, increase this SELL order
                        else if (_operativeAmount - _buyOrderAmount > _sellOrderAmount)
                        {
                            var newAmount = _operativeAmount - _buyOrderAmount;
                            _sellOrderId = _requestor.UpdateSellOrder(_sellOrderId, price, ref newAmount);
                            _sellOrderAmount = newAmount;
                            _sellOrderPrice = price;
                            log("Updated SELL order ID={0}; amount={1} XRP; price={2} {3}", _sellOrderId, _sellOrderAmount, price, _currencyCode);
                        }
                        //Or if we simply need to change price.
                        else if (!_sellOrderPrice.eq(price))
                        {
                            _sellOrderId = _requestor.UpdateSellOrder(_sellOrderId, price, ref _sellOrderAmount);
                            _sellOrderPrice = price;
                            log("Updated SELL order ID={0}; amount={1} XRP; price={2} {3}", _sellOrderId, _sellOrderAmount, price, _currencyCode);
                        }
                    }
                    else        //Closed or cancelled
                    {
                        //Check if cancelled by the network
                        var balance = _requestor.GetXrpBalance();
                        if (balance.eq(_xrpBalance, 0.1))
                        {
                            log("SELL order ID={0} closed but asset validation failed (balance={1} XRP). Asuming was cancelled, trying to recreate",
                                ConsoleColor.Yellow, _sellOrderId, balance);
                            _sellOrderPrice = suggestSellPrice(market);
                            _sellOrderId = _requestor.PlaceSellOrder(_sellOrderPrice, ref _sellOrderAmount);

                            if (-1 != _sellOrderId)
                            {
                                log("Successfully created SELL order with ID={0}; amount={1} XRP; price={2} {3}",
                                    ConsoleColor.Cyan, _sellOrderId, _sellOrderAmount, _sellOrderPrice, _currencyCode);
                            }
                        }
                        else
                        {
                            log("SELL order ID={0} (amount={1} XRP) was closed at price={2} {3}",
                                ConsoleColor.Green, _sellOrderId, _sellOrderAmount, _sellOrderPrice, _currencyCode);
                            _sellOrderAmount = 0;
                            _sellOrderId = -1;
                        }
                    }
                }
                else    //No SELL order, create one
                {
                    _sellOrderPrice = suggestSellPrice(market);
                    var amount = _operativeAmount - _buyOrderAmount;
                    _sellOrderId = _requestor.PlaceSellOrder(_sellOrderPrice, ref amount);
                    _sellOrderAmount = amount;

                    if (-1 != _sellOrderId)
                    {
                        log("Successfully created SELL order with ID={0}; amount={1} XRP; price={2} {3}",
                            ConsoleColor.Cyan, _sellOrderId, _sellOrderAmount, _sellOrderPrice, _currencyCode);
                    }
                }
            }

            _xrpBalance = _requestor.GetXrpBalance();
            log("### Balance= {0} XRP", _xrpBalance);
            log(new string('=', 84));
        }

        public override void Kill()
        {
            base.Kill();
            _requestor.Close();
        }


        private double suggestBuyPrice(Market market)
        {
            const int decPlaces = 14;
            double increment = Math.Pow(10.0, -1.0*decPlaces); // 0.00000000000001;
            double sum = 0;
            var lowestAsk = market.Asks.First().Price;

            foreach (var bid in market.Bids)
            {
                if (sum + _operativeAmount > _volumeWall && bid.Price + 2.0 * _minDifference < lowestAsk)
                {
                    double buyPrice = Math.Round(bid.Price + increment, decPlaces);

                    //The difference is too small and we'd be not first in BUY orders. Leave previous price to avoid server call
                    if (-1 != _buyOrderId && buyPrice < market.Bids[0].Price && Math.Abs(buyPrice - _buyOrderPrice) < _minPriceUpdate)
                    {
                        log("DEBUG: BUY price {0} too similar, using previous", buyPrice);
                        return _buyOrderPrice;
                    }

                    return buyPrice;
                }
                sum += bid.Amount;

                //Don't consider volume of own order
                if (bid.Price.eq(_buyOrderPrice))
                    sum -= _buyOrderAmount;
            }

            //Market too dry, use BUY order before last, so we see it in chart
            var price = market.Bids.Last().Price + increment;
            if (-1 != _buyOrderId && Math.Abs(price - _buyOrderPrice) < _minPriceUpdate)
                return _buyOrderPrice;
            return Math.Round(price, 7);
        }

        private double suggestSellPrice(Market market)
        {
            const int decPlaces = 14;
            double increment = Math.Pow(10.0, -1.0 * decPlaces); // 0.00000000000001;
            //Ignore offers with tiny XRP volume (<100 XRP)
            const double MIN_WALL_VOLUME = 100.0;

            double sumVolume = 0.0;
            foreach (var ask in market.Asks)
            {
                //Don't count self
                if (ask.Price.eq(_sellOrderPrice) && ask.Amount.eq(_sellOrderAmount))
                    continue;
                //Skip SELL orders with tiny amount
                sumVolume += ask.Amount;
                if (sumVolume < MIN_WALL_VOLUME)
                    continue;

                if (ask.Price > _executedBuyPrice + _minDifference)
                {
                    return ask.Price.eq(_sellOrderPrice)
                        ? _sellOrderPrice
                        : Math.Round(ask.Price - increment, decPlaces);
                }
            }

            //All SELL orders are too low (probably some terrible fall). Suggest SELL order with minimum profit and hope :-( TODO: maybe some stop-loss strategy
            return _executedBuyPrice + _minDifference;
        }
    }
}
