using System;
using System.Linq;

using BitfinexBot.Business;
using Common;
using Common.Business;


namespace BitfinexBot
{
    internal class CrazySellerTrap : TraderBase
    {
        private BitfinexApi _requestor;

        private string _cryptoCurrencyCode;

        //CRYPTO amount to trade
        private double _operativeAmount;
        private double _minWallVolume;
        private double _maxWallVolume;
        //Volumen of CRYPTO necessary to accept our offer
        private double _volumeWall;
        //Minimum difference between SELL price and subsequent BUY price (so we have at least some profit)
        private double _minDifference;
        //Tolerance of SELL price (absolute value in USD). Usefull if possible price change is minor, to avoid frequent order updates.
        private double _minPriceUpdate;

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


        public CrazySellerTrap(Logger logger) : base(logger)
        { }

        protected override void Initialize()
        {
            _cryptoCurrencyCode = Configuration.GetValue("crypto_currency_code");
            _operativeAmount = double.Parse(Configuration.GetValue("operative_amount"));
            _minWallVolume = double.Parse(Configuration.GetValue("min_volume"));
            _maxWallVolume = double.Parse(Configuration.GetValue("max_volume"));

            _minDifference = double.Parse(Configuration.GetValue("trade_spread"));              //TODO: CBT needs this too
            _minPriceUpdate = double.Parse(Configuration.GetValue("min_price_update"));

            log(String.Format("Bitfinex {0} CST trader initialized with operative={1}; minWall={2}; maxWall={3}",
                              _cryptoCurrencyCode, _operativeAmount, _minWallVolume, _maxWallVolume));
            _requestor = new BitfinexApi(_logger);
        }

        protected override void Check()
        {
            var market = _requestor.GetMarketDepth(_cryptoCurrencyCode);
            var tradeHistory = _requestor.GetTradeHistory(_cryptoCurrencyCode);
            var serverTime = _requestor.GetServerTime();

            var coef = TradeHelpers.GetMadness(tradeHistory, serverTime);
            _volumeWall = Helpers.SuggestWallVolume(coef, _minWallVolume, _maxWallVolume);
            _intervalMs = Helpers.SuggestInterval(coef, 8000, 20000);
            log("Coef={0}, Volume={1} {2}; Interval={3}ms; ", coef, _volumeWall, _cryptoCurrencyCode, _intervalMs);

            //We have active BUY order
            if (-1 != _buyOrderId)
            {
                var buyOrder = _requestor.GetOrderInfo(_buyOrderId);
                switch (buyOrder.Status)
                {
                    case OrderStatus.Open:
                        {
                            //Untouched
                            if (buyOrder.Amount.eq(_buyOrderAmount))
                            {
                                log("BUY order ID={0} untouched (amount={1} {2}, price={3} USD)", _buyOrderId, _buyOrderAmount, _cryptoCurrencyCode, _buyOrderPrice);

                                double price = SuggestBuyPrice(market);
                                var newAmount = _operativeAmount - _sellOrderAmount;

                                //Evaluate and update if needed
                                if (newAmount > _buyOrderAmount || !_buyOrderPrice.eq(price))
                                {
                                    _buyOrderAmount = newAmount;
                                    _buyOrderId = _requestor.UpdateBuyOrder(_buyOrderId, _cryptoCurrencyCode, price, newAmount);
                                    _buyOrderPrice = price;
                                    log("Updated BUY order ID={0}; amount={1} {2}; price={3} USD", _buyOrderId, _buyOrderAmount, _cryptoCurrencyCode, price);
                                }
                            }
                            else    //Partially filled
                            {
                                _executedBuyPrice = buyOrder.Price;
                                _buyOrderAmount = buyOrder.Amount;
                                log("BUY order ID={0} partially filled at price={1} USD. Remaining amount={2} {3};",
                                    ConsoleColor.Green, _buyOrderId, _executedBuyPrice, buyOrder.Amount, _cryptoCurrencyCode);
                                var price = SuggestBuyPrice(market);
                                //The same price is totally unlikely, so we don't check it here
                                _buyOrderId = _requestor.UpdateBuyOrder(_buyOrderId, _cryptoCurrencyCode, price, buyOrder.Amount);
                                _buyOrderPrice = price;
                                log("Updated BUY order ID={0}; amount={1} {2}; price={3} USD", _buyOrderId, _buyOrderAmount, _cryptoCurrencyCode, _buyOrderPrice);
                            }
                            break;
                        }
                    case OrderStatus.Closed:
                        {
                            _executedBuyPrice = buyOrder.Price;
                            _buyOrderId = -1;
                            log("BUY order ID={0} (amount={1} {2}) was closed at price={3} USD", ConsoleColor.Green, buyOrder.id, _buyOrderAmount, _cryptoCurrencyCode, _executedBuyPrice);
                            _buyOrderAmount = 0;
                            break;
                        }
                    default:
                        var message = String.Format("BUY order ID={0} has unexpected status '{1}'", _buyOrderId, buyOrder.Status);
                        log(message, ConsoleColor.Red);
                        throw new Exception(message);
                }
            }
            else if (_operativeAmount - _sellOrderAmount > 0.00001)    //No BUY order (and there are some money available). So create one
            {
                _buyOrderPrice = SuggestBuyPrice(market);
                _buyOrderAmount = _operativeAmount - _sellOrderAmount;
                _buyOrderId = _requestor.PlaceBuyOrder(_cryptoCurrencyCode, _buyOrderPrice, _buyOrderAmount);
                log("Successfully created BUY order with ID={0}; amount={1} {2}; price={3} USD",
                    ConsoleColor.Cyan, _buyOrderId, _buyOrderAmount, _cryptoCurrencyCode, _buyOrderPrice);
            }

            //Handle SELL order
            if (_operativeAmount - _buyOrderAmount > 0.00001)
            {
                //SELL order already existed
                if (-1 != _sellOrderId)
                {
                    var sellOrder = _requestor.GetOrderInfo(_sellOrderId);

                    switch (sellOrder.Status)
                    {
                        case OrderStatus.Open:
                            {
                                log("SELL order ID={0} open (amount={1} {2}, price={3} USD)", _sellOrderId, sellOrder.Amount, _cryptoCurrencyCode, _sellOrderPrice);

                                double price = SuggestSellPrice(market);

                                //Partially filled
                                if (!sellOrder.Amount.eq(_sellOrderAmount))
                                {
                                    log("SELL order ID={0} partially filled at price={1} USD. Remaining amount={2} {3};",
                                        ConsoleColor.Green, _sellOrderId, sellOrder.Price, sellOrder.Amount, _cryptoCurrencyCode);
                                    var amount = sellOrder.Amount;
                                    _sellOrderId = _requestor.UpdateSellOrder(_sellOrderId, _cryptoCurrencyCode, price, ref amount);
                                    _sellOrderAmount = amount;
                                    _sellOrderPrice = price;
                                    log("Updated SELL order ID={0}; amount={1} {2}; price={3} USD", _sellOrderId, _sellOrderAmount, _cryptoCurrencyCode, price);
                                }
                                //If there were some money released by filling a BUY order, increase this SELL order
                                else if (_operativeAmount - _buyOrderAmount > _sellOrderAmount)
                                {
                                    var newAmount = _operativeAmount - _buyOrderAmount;
                                    _sellOrderId = _requestor.UpdateSellOrder(_sellOrderId, _cryptoCurrencyCode, price, ref newAmount);
                                    _sellOrderAmount = newAmount;
                                    _sellOrderPrice = price;
                                    log("Updated SELL order ID={0}; amount={1} {2}; price={3} USD", _sellOrderId, _sellOrderAmount, _cryptoCurrencyCode, price);
                                }
                                //Or if we simply need to change price.
                                else if (!_sellOrderPrice.eq(price))
                                {
                                    _sellOrderId = _requestor.UpdateSellOrder(_sellOrderId, _cryptoCurrencyCode, price, ref _sellOrderAmount);
                                    _sellOrderPrice = price;
                                    log("Updated SELL order ID={0}; amount={1} {2}; price={3} USD", _sellOrderId, _sellOrderAmount, _cryptoCurrencyCode, price);
                                }
                                break;
                            }
                        case OrderStatus.Closed:
                            {
                                log("SELL order ID={0} (amount={1} {2}) was closed at price={3} USD", ConsoleColor.Green, _sellOrderId, _sellOrderAmount, _cryptoCurrencyCode, sellOrder.Price);
                                _sellOrderAmount = 0;
                                _sellOrderId = -1;
                                break;
                            }
                        default:
                            var message = String.Format("SELL order ID={0} has unexpected status '{1}", _sellOrderId, sellOrder.Status);
                            log(message, ConsoleColor.Red);
                            throw new Exception(message);
                    }
                }
                else    //No SELL order, create one
                {
                    _sellOrderPrice = SuggestSellPrice(market);
                    var amount = _operativeAmount - _buyOrderAmount;
                    _sellOrderId = _requestor.PlaceSellOrder(_cryptoCurrencyCode, _sellOrderPrice, ref amount);
                    _sellOrderAmount = amount;
                    log("Successfully created SELL order with ID={0}; amount={1} {2}; price={3} USD", ConsoleColor.Cyan, _sellOrderId, _sellOrderAmount, _cryptoCurrencyCode, _sellOrderPrice);
                }
            }

            var balances = _requestor.GetAccountBalances();
            var cryptoBalance = balances.GetExchangeBalance(_cryptoCurrencyCode).Available;
            var usdBalance = balances.GetExchangeBalance("usd"/*TODO: _fiatCurrencyCode*/).Amount;
            log("DEBUG: Balance = {0} {1}; {2} USD", cryptoBalance, _cryptoCurrencyCode, usdBalance);
            log(new string('=', 80));
        }

        protected virtual double SuggestBuyPrice(IMarketDepthResponse<Order> market)        //TODO: To TraderBase
        {
            double sum = 0;
            var lowestAsk = market.Asks.First().Price;

            foreach (var bid in market.Bids)
            {
                if (sum + _operativeAmount > _volumeWall && bid.Price + _minDifference < lowestAsk)
                {
                    double buyPrice = Math.Round(bid.Price + 0.001, 3);

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
                {
                    sum -= _buyOrderAmount;
                }
            }

            //Market too dry, use BUY order before last, so we see it in chart
            var price = market.Bids.Last().Price + 0.01;
            if (-1 != _buyOrderId && Math.Abs(price - _buyOrderPrice) < _minPriceUpdate)
            {
                return _buyOrderPrice;
            }
            return Math.Round(price, 3);
        }

        protected virtual double SuggestSellPrice(IMarketDepthResponse<Order> market)
        {
            const double MIN_WALL_VOLUME = 0.1;

            double sumVolume = 0.0;
            foreach (var ask in market.Asks)
            {
                //Don't count self
                if (ask.Price.eq(_sellOrderPrice) && ask.Amount.eq(_sellOrderAmount))
                {
                    continue;
                }
                //Skip SELL orders with tiny amount
                sumVolume += ask.Amount;
                if (sumVolume < MIN_WALL_VOLUME)
                {
                    continue;
                }

                if (ask.Price > _executedBuyPrice + _minDifference)
                {
                    return ask.Price.eq(_sellOrderPrice)
                        ? _sellOrderPrice
                        : ask.Price - 0.001;
                }
            }

            //All SELL orders are too low (probably some terrible fall). Suggest SELL order with minimum profit and hope :-( TODO: maybe some stop-loss strategy
            return _executedBuyPrice + _minDifference;
        }
    }
}
