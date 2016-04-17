using System;
using System.Collections.Generic;
using System.Linq;

using Common;
using RippleBot.Business;


namespace RippleBot
{
    /// <summary>
    /// Direct arbitrage with fiat assets on two different gateway. No XRP mid-step.
    /// Uses constant minimum price both for buy and sell.
    /// </summary>
    public class DirectArbitrage : TraderBase
    {
        private string _baseAssetCode;
        private string _arbAssetCode;
        private string _baseGateway;
        private string _arbGateway;
        private string _baseGatewayName;
        private string _arbGatewayName;
        private double _baseMinPrice;
        private double _arbMinPrice;
        private double _baseMaxPrice = Double.MaxValue;
        private double _arbMaxPrice = Double.MaxValue;

        private double _lastBaseBalance = -1.0;
        private double _lastArbBalance = -1.0;

        private RippleApi _baseRequestor;
        private RippleApi _arbRequestor;

        //Thresholds to ignore small balance updates
        private double base_threshold;
        private double arb_threshold;
        private double _baseMinPriceUpdate = 0.00005;
        private double _arbMinPriceUpdate = 0.00005;

        //Check for dangling orders to cancel every 10th round
        private const int ZOMBIE_CHECK = 10;
        private int _counter;

        //Active BASE order
        private int _baseOrderId = -1;
        private double _baseOrderAmount;
        private double _baseOrderPrice;

        //Active ARB order
        private int _arbOrderId = -1;
        private double _arbOrderAmount;
        private double _arbOrderPrice;


        public DirectArbitrage(Logger logger)
            : base(logger)
        { }

        protected override void Initialize()
        {
            _baseAssetCode = Configuration.GetValue("base_asset_code");
            _arbAssetCode = Configuration.GetValue("arbitrage_asset_code");

            _baseGateway = Configuration.GetValue("base_gateway_address");
            _baseGatewayName = Configuration.GetValue("base_gateway_name");

            _arbGateway = Configuration.GetValue("arbitrage_gateway_address");
            _arbGatewayName = Configuration.GetValue("arbitrage_gateway_name");

            _baseMinPrice = double.Parse(Configuration.GetValue("min_base_price"));
            _arbMinPrice = double.Parse(Configuration.GetValue("min_arb_price"));
            _baseMaxPrice = Configuration.GetValue("max_base_price") != null
                                ? double.Parse(Configuration.GetValue("max_base_price"))
                                : Double.MaxValue;
            _arbMaxPrice = Configuration.GetValue("max_arb_price") != null
                            ? double.Parse(Configuration.GetValue("max_arb_price"))
                            : Double.MaxValue;
            base_threshold = double.Parse(Configuration.GetValue("base_amount_threshold"));
            arb_threshold = double.Parse(Configuration.GetValue("arb_amount_threshold"));

            if (null != Configuration.GetValue("base_min_price_update"))
            {
                _baseMinPriceUpdate = double.Parse(Configuration.GetValue("base_min_price_update"));
            }
            if (null != Configuration.GetValue("arb_min_price_update"))
            {
                _arbMinPriceUpdate = double.Parse(Configuration.GetValue("arb_min_price_update"));
            }

            _intervalMs = 15000;

            var cleanup = Configuration.GetValue("cleanup_zombies");
            _cleanup = bool.Parse(cleanup ?? false.ToString());

            string dataApiUrl = Configuration.GetValue("data_api_url");
            if (String.IsNullOrEmpty(dataApiUrl))
            {
                throw new Exception("Configuration value data_api_url not found!");
            }

            _baseRequestor = new RippleApi(_logger, dataApiUrl, _baseGateway, _baseAssetCode);
            _baseRequestor.Init();
            _arbRequestor = new RippleApi(_logger, dataApiUrl, _arbGateway, _arbAssetCode);
            _arbRequestor.Init();
            log("Direct arbitrage trader started for pair {0}.{1} / {2}.{3}; base_min={4:0.0000}; arb_min={5:0.0000}",
                _baseAssetCode, _baseGatewayName, _arbAssetCode, _arbGatewayName, _arbMinPrice, _baseMinPrice);
        }

        protected override void Check()
        {
            double baseBalance = _baseRequestor.GetBalance2(_baseAssetCode, _baseGateway);
            double arbBalance = _arbRequestor.GetBalance2(_arbAssetCode, _arbGateway);

            log("### Balances: {0:0.0000} {1}.{2};  {3:0.0000} {4}.{5}",
                baseBalance, _baseAssetCode, _baseGatewayName, arbBalance, _arbAssetCode, _arbGatewayName);

            if (baseBalance.eq(-1.0, 0.01) || arbBalance.eq(-1.0, 0.01))
            {
                return;
            }

            //We have active BASE sell order
            if (-1 != _baseOrderId)
            {
                var baseOrder = _baseRequestor.GetOrderInfo(_baseOrderId);

                if (null == baseOrder)
                {
                    return;
                }

                //Filled or cancelled
                if (baseOrder.Closed)
                {
                    if (baseBalance > 0.0 && baseBalance.eq(_baseOrderAmount, 0.1) && _baseOrderAmount > base_threshold)
                    {
                        log("BASE order ID={0} closed but asset validation failed (balance={1:0.0000} {2}). Assuming was cancelled, trying to recreate",
                            ConsoleColor.Yellow, _baseOrderId, baseBalance, _baseAssetCode);
                        createBaseOrder(baseBalance);

                        if (-1 != _baseOrderId)
                        {
                            log("Created BASE order with ID={0}; amount={1:0.0000} {2}.{3}; price={4:0.0000} {5}.{6}", ConsoleColor.Cyan,
                                _baseOrderId, _baseOrderAmount, _baseAssetCode, _baseGatewayName, _baseOrderPrice, _arbAssetCode, _arbGatewayName);
                        }
                    }
                    else
                    {
                        log("BASE order ID={0} (amount={1:0.0000} {2}) was closed at price={3}",
                            ConsoleColor.Green, _baseOrderId, _baseOrderAmount, _baseAssetCode, _baseOrderPrice);
                        _baseOrderId = -1;
                        _baseOrderPrice = 0.0;
                        _baseOrderAmount = 0.0;

                        //Increase ARB order
                        createArbOrder(arbBalance);
                        if (-1 != _arbOrderId)
                        {
                            log("Updated ARB order ID={0}; amount={1:0.0000} {2}.{3};", _arbOrderId, _arbOrderAmount, _arbAssetCode, _arbGatewayName);
                        }
                    }
                }
                else if (baseOrder.Amount + base_threshold < _baseOrderAmount)
                {
                    //Double-check with ARB balance that BASE order was indeed partially filled
                    if (arbBalance - arb_threshold > _lastArbBalance)
                    {
                        _baseOrderAmount = baseOrder.Amount;
                        log("BASE order ID={0} partially filled. Remaining amount={1:0.0000} {2}.{3}",
                            ConsoleColor.Green, _baseOrderId, _baseOrderAmount, _baseAssetCode, _baseGatewayName);

                        //Increase ARB order
                        createArbOrder(arbBalance);
                        if (-1 != _arbOrderId)
                        {
                            log("Updated ARB order ID={0}; amount={1:0.0000} {2}.{3};", _arbOrderId, _arbOrderAmount, _arbAssetCode, _arbGatewayName);
                        }
                    }
                    else
                    {
                        log("BASE order ID={0} amount decreased but ARB balance validation failed (was {1:0.0000}, is {2:0.0000}). Nothing to do here",
                            ConsoleColor.Yellow, _baseOrderId, _lastArbBalance, arbBalance);
                    }
                }
                else if (baseOrder.Amount + base_threshold < baseBalance)
                {
                    //BASE balance increased => ARB order was (partially) filled
                    log("ARB order bought some {0}.{1} (BASE balance={2:0.0000})", ConsoleColor.Cyan, _baseAssetCode, _baseGatewayName, baseBalance);

                    //Increase BASE order
                    createBaseOrder(baseBalance);
                    if (-1 != _baseOrderId)
                    {
                        log("Updated BASE order ID={0}; amount={1:0.0000} {2}.{3};", _baseOrderId, _baseOrderAmount, _baseAssetCode, _baseGatewayName);
                    }
                }
                else
                {
                    log("BASE order ID={0} untouched (amount={1:0.0000} {2}.{3})", _baseOrderId, baseOrder.Amount, _baseAssetCode, _baseGatewayName);

                    List<FiatAsk> asks = _baseRequestor.GetOrderBookAsks(_arbAssetCode, _arbGateway, true);
                    double newPrice = suggestPrice(asks, _baseMinPrice, _baseMaxPrice, _baseOrderId, _baseOrderPrice, _baseMinPriceUpdate);

                    if (!_baseOrderPrice.eq(newPrice))
                    {
                        createBaseOrder(baseBalance, newPrice);
                        log("Updated BASE order ID={0}; price={1:0.0000};", _baseOrderId, newPrice);
                    }
                }
            }
            else if (baseBalance > base_threshold)
            {
                //We have some spare BASE assets to sell
                createBaseOrder(baseBalance);
                if (-1 != _baseOrderId)
                {
                    log("Successfully created BASE order with ID={0}; amount={1:0.0000} {2}.{3}; price={4:0.0000} {5}.{6}", ConsoleColor.Cyan,
                        _baseOrderId, _baseOrderAmount, _baseAssetCode, _baseGatewayName, _baseOrderPrice, _arbAssetCode, _arbGatewayName);
                }
            }

            //ARB order
            if (-1 != _arbOrderId)
            {
                var arbOrder = _arbRequestor.GetOrderInfo(_arbOrderId);

                if (null == arbOrder)
                {
                    return;
                }

                //Filled or cancelled
                if (arbOrder.Closed)
                {
                    if (arbBalance > 0.0 && arbBalance.eq(_arbOrderAmount, 0.1) && _arbOrderAmount > arb_threshold)
                    {
                        log("ARB order ID={0} closed but asset validation failed (balance={1:0.0000} {2}). Assuming was cancelled, trying to recreate",
                            ConsoleColor.Yellow, _arbOrderId, arbBalance, _arbAssetCode);
                        createArbOrder(arbBalance);

                        if (-1 != _arbOrderId)
                        {
                            log("Created ARB order with ID={0}; amount={1:0.0000} {2}.{3}; price={4:0.0000} {5}.{6}", ConsoleColor.Cyan,
                                _arbOrderId, _arbOrderAmount, _arbAssetCode, _arbGatewayName, _arbOrderPrice, _baseAssetCode, _baseGatewayName);
                        }
                    }
                    else
                    {
                        log("ARB order ID={0} (amount={1:0.0000} {2}) was closed at price={3}",
                            ConsoleColor.Green, _arbOrderId, _arbOrderAmount, _arbAssetCode, _arbOrderPrice);
                        _arbOrderId = -1;
                        _arbOrderPrice = 0.0;
                        _arbOrderAmount = 0.0;

                        //Increase BASE order
                        createBaseOrder(baseBalance);
                        if (-1 != _baseOrderId)
                        {
                            log("Updated BASE order ID={0}; amount={1:0.0000} {2}.{3};", _baseOrderId, _baseOrderAmount, _baseAssetCode, _baseGatewayName);
                        }
                    }
                }
                else if (arbOrder.Amount + arb_threshold < _arbOrderAmount)
                {
                    //Double-check with BASE balance that ARB order was indeed partially filled
                    if (baseBalance - base_threshold > _lastBaseBalance)
                    {
                        _arbOrderAmount = arbOrder.Amount;
                        log("ARB order ID={0} partially filled. Remaining amount={1:0.0000} {2}.{3}",
                            ConsoleColor.Green, _arbOrderId, _arbOrderAmount, _arbAssetCode, _arbGatewayName);

                        //Increase BASE order
                        createBaseOrder(baseBalance);
                        if (-1 != _baseOrderId)
                        {
                            log("Updated BASE order ID={0}; amount={1:0.0000} {2}.{3};", _baseOrderId, _baseOrderAmount, _baseAssetCode, _baseGatewayName);
                        }
                    }
                    else
                    {
                        log("ARB order ID={0} amount decreased but BASE balance validation failed (was {1:0.0000}, is {2:0.0000}). Nothing to do here",
                            ConsoleColor.Yellow, _baseOrderId, _lastBaseBalance, baseBalance);
                    }
                }
                else if (arbOrder.Amount + arb_threshold < arbBalance)
                {
                    //ARB balance increased => BASE order was (partially) filled
                    log("BASE order bought some {0}.{1} (ARB balance={2:0.0000})", ConsoleColor.Cyan, _arbAssetCode, _arbGatewayName, arbBalance);

                    //Increase ARB order
                    createArbOrder(arbBalance);
                    if (-1 != _arbOrderId)
                    {
                        log("Updated ARB order ID={0}; amount={1:0.0000} {2}.{3};", _arbOrderId, _arbOrderAmount, _arbAssetCode, _arbGatewayName);
                    }
                }
                else
                {
                    log("ARB order ID={0} untouched (amount={1:0.0000} {2}.{3})", _arbOrderId, arbOrder.Amount, _arbAssetCode, _arbGatewayName);

                    List<FiatAsk> asks = _arbRequestor.GetOrderBookAsks(_baseAssetCode, _baseGateway, true);
                    double newPrice = suggestPrice(asks, _arbMinPrice, _arbMaxPrice, _arbOrderId, _arbOrderPrice, _arbMinPriceUpdate);

                    if (!_arbOrderPrice.eq(newPrice))
                    {
                        createArbOrder(arbBalance, newPrice);
                        log("Updated ARB order ID={0}; price={1:0.0000};", _arbOrderId, newPrice);
                    }
                }
            }
            else if (arbBalance > arb_threshold)
            {
                //We have some spare ARB assets to sell
                createArbOrder(arbBalance);
                if (-1 != _arbOrderId)
                {
                    log("Successfully created ARB order with ID={0}; amount={1:0.0000} {2}.{3}; price={4:0.0000} {5}.{6}", ConsoleColor.Cyan,
                        _arbOrderId, _arbOrderAmount, _arbAssetCode, _arbGatewayName, _arbOrderPrice, _baseAssetCode, _baseGatewayName);
                }
            }

            if (_cleanup && ++_counter == ZOMBIE_CHECK)
            {
                _counter = 0;
                _baseRequestor.CleanupZombies(_baseOrderId, _arbOrderId);
            }

            _lastBaseBalance = baseBalance;
            _lastArbBalance = arbBalance;
            log(new string('=', 74));
        }

        public override void Kill()
        {
            base.Kill();
            _baseRequestor.Close();
            _arbRequestor.Close();
        }


        private double suggestPrice(List<FiatAsk> asks, double minPrice, double maxPrice, int currentOrderId, double currentOrderPrice, double minPriceUpdate)
        {
            if (null == asks || !asks.Any())
            {
                return currentOrderPrice > 0.0 ? currentOrderPrice : minPrice;
            }

            const int DEC_PLACES = 14;
            double increment = 2.0 * minPriceUpdate;

            //Find first ASK price higher than minPrice
            foreach (FiatAsk ask in asks)
            {
                //Don't consider own order
                if (Configuration.AccessKey == ask.Account && ask.Sequence == currentOrderId)
                {
                    continue;
                }

                if (ask.Price >= minPrice)
                {
                    double sellPrice = Math.Round(ask.Price - increment, DEC_PLACES);

                    //The difference is too small. Leave previous price to avoid server call
                    if (-1 != currentOrderId && Math.Abs(sellPrice - currentOrderPrice) < minPriceUpdate)
                    {
                        log("DEBUG: price {0:0.00000} too similar, using previous", sellPrice);
                        return currentOrderPrice;
                    }

                    if (sellPrice > maxPrice)
                    {
                        return maxPrice;
                    }
                    return sellPrice;
                }                
            }

            //Order book filled with junk. Use order before last, so we see it in chart
            double price = asks.Last().Price - increment;
            if (-1 != currentOrderId && Math.Abs(price - currentOrderPrice) < minPriceUpdate)
            {
                return currentOrderPrice;
            }
            return Math.Round(price, DEC_PLACES);
        }

        private void createBaseOrder(double baseBalance, double price = -1.0)
        {
            if (-1 != _baseOrderId)
            {
                if (!_baseRequestor.CancelOrder(_baseOrderId))
                {
                    return;
                }
                _baseOrderId = -1;
                _baseOrderPrice = 0.0;
                _baseOrderAmount = 0.0;
            }

            if (price.eq(-1.0))
            {
                List<FiatAsk> asks = _baseRequestor.GetOrderBookAsks(_arbAssetCode, _arbGateway, true);
                price = suggestPrice(asks, _baseMinPrice, _baseMaxPrice, _baseOrderId, _baseOrderPrice, _baseMinPriceUpdate);
            }

            int newOrderId = _baseRequestor.PlaceOrder(baseBalance, price, _arbAssetCode, _arbGateway);
            if (-1 != newOrderId)
            {
                _baseOrderId = newOrderId;
                _baseOrderPrice = price;
                _baseOrderAmount = baseBalance;
            }
        }

        private void createArbOrder(double arbBalance, double price = -1.0)
        {
            if (-1 != _arbOrderId)
            {
                if (!_arbRequestor.CancelOrder(_arbOrderId))
                {
                    return;
                }
                _arbOrderId = -1;
                _arbOrderPrice = 0.0;
                _arbOrderAmount = 0.0;
            }

            if (price.eq(-1.0))
            {
                List<FiatAsk> asks = _arbRequestor.GetOrderBookAsks(_baseAssetCode, _baseGateway, true);
                price = suggestPrice(asks, _arbMinPrice, _arbMaxPrice, _arbOrderId, _arbOrderPrice, _arbMinPriceUpdate);
            }

            int newOrderId = _arbRequestor.PlaceOrder(arbBalance, price, _baseAssetCode, _baseGateway);
            if (-1 != newOrderId)
            {
                _arbOrderId = newOrderId;
                _arbOrderPrice = price;
                _arbOrderAmount = arbBalance;
            }
        }
    }
}
