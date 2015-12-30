using System;
using System.Collections.Generic;
using System.Linq;

using Common;
using RippleBot.Business;


namespace RippleBot
{
    /// <summary>
    /// Direct arbitrage with one fiat currency on two different gateway. No XRP mid-step.
    /// Uses simple constant price both for buy and sell.
    /// </summary>
    public class DirectArbitrage : TraderBase
    {
        private string _currency;
        private string _baseGateway;
        private string _arbGateway;
        private string _baseGatewayName;
        private string _arbGatewayName;
        private double _baseMinPrice;
        private double _arbMinPrice;

        private double _lastBaseBalance = -1.0;
        private double _lastArbBalance = -1.0;

        private RippleApi _baseRequestor;
        private RippleApi _arbRequestor;

        private double _threshold;

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
            _currency = Configuration.GetValue("currency_code");

            _baseGateway = Configuration.GetValue("base_gateway_address");
            _baseGatewayName = Configuration.GetValue("base_gateway_name");

            _arbGateway = Configuration.GetValue("arbitrage_gateway_address");
            _arbGatewayName = Configuration.GetValue("arbitrage_gateway_name");

            _baseMinPrice = double.Parse(Configuration.GetValue("min_base_price"));
            _arbMinPrice = double.Parse(Configuration.GetValue("min_arb_price"));
            _threshold = double.Parse(Configuration.GetValue("amount_threshold"));
            _intervalMs = 15000;

            var cleanup = Configuration.GetValue("cleanup_zombies");
            _cleanup = bool.Parse(cleanup ?? false.ToString());

            _baseRequestor = new RippleApi(_logger, _baseGateway, _currency);
            _baseRequestor.Init();
            _arbRequestor = new RippleApi(_logger, _arbGateway, _currency);
            _arbRequestor.Init();
            log("Direct arbitrage trader started for currency {0}; gateways {1}/{2}; base_min={3:0.0000}; arb_min={4:0.0000}",
                _currency, _baseGatewayName, _arbGatewayName, _arbMinPrice, _baseMinPrice);
        }

        protected override void Check()
        {
            double baseBalance = _baseRequestor.GetBalance(_currency, _baseGateway);
            double arbBalance = _arbRequestor.GetBalance(_currency, _arbGateway);

            log("### Balances: {0:0.0000} {1}.{2};  {3:0.0000} {1}.{4}", baseBalance, _currency, _baseGatewayName, arbBalance, _arbGatewayName);

            if (baseBalance.eq(-1.0, 0.01) || arbBalance.eq(-1.0, 0.01))
            {
                return;
            }

            //We have active BASE sell order
            if (-1 != _baseOrderId)
            {
                var baseOrder = _baseRequestor.GetOrderInfo(_baseOrderId);

                if (null == baseOrder)
                    return;

                //Filled or cancelled
                if (baseOrder.Closed)
                {
                    if (baseBalance.eq(_baseOrderAmount, 0.1) && _baseOrderAmount > _threshold)
                    {
                        log("BASE order ID={0} closed but asset validation failed (balance={1} {2}). Assuming was cancelled, trying to recreate",
                            ConsoleColor.Yellow, _baseOrderId, baseBalance, _currency);
                        createBaseOrder(baseBalance);

                        if (-1 != _baseOrderId)
                        {
                            log("Created BASE order with ID={0}; amount={1:0.0000} {2}.{3}; price={4:0.0000} {2}.{5}",
                                ConsoleColor.Cyan, _baseOrderId, _baseOrderAmount, _currency, _baseGatewayName, _baseOrderPrice, _arbGatewayName);
                        }
                    }
                    else
                    {
                        log("BASE order ID={0} (amount={1:0.0000} {2}) was closed at price={3}",
                            ConsoleColor.Green, _baseOrderId, _baseOrderAmount, _currency, _baseOrderPrice);
                        _baseOrderId = -1;
                        _baseOrderPrice = 0.0;
                        _baseOrderAmount = 0.0;

                        //Increase ARB order
                        createArbOrder(arbBalance);
                        if (-1 != _arbOrderId)
                        {
                            log("Updated ARB order ID={0}; amount={1:0.0000} {2}.{3};", _arbOrderId, _arbOrderAmount, _currency, _arbGatewayName);
                        }
                    }
                }
                else if (baseOrder.Amount + _threshold < _baseOrderAmount)
                {
                    //Double-check with ARB balance that BASE order was indeed partially filled
                    if (arbBalance - _threshold > _lastArbBalance)
                    {
                        _baseOrderAmount = baseOrder.Amount;
                        log("BASE order ID={0} partially filled. Remaining amount={1:0.0000} {2}.{3}",
                            ConsoleColor.Green, _baseOrderId, _baseOrderAmount, _currency, _baseGatewayName);

                        //Increase ARB order
                        createArbOrder(arbBalance);
                        if (-1 != _arbOrderId)
                        {
                            log("Updated ARB order ID={0}; amount={1:0.0000} {2}.{3};", _arbOrderId, _arbOrderAmount, _currency, _arbGatewayName);
                        }
                    }
                    else
                    {
                        log("BASE order ID={0} amount decreased but ARB balance validation failed (was{1:0.0000}, is {2:0.0000}). Nothing to do here",
                            ConsoleColor.Yellow, _baseOrderId, _lastArbBalance, arbBalance);
                    }
                }
                else if (baseOrder.Amount + _threshold < baseBalance)
                {
                    //BASE balance increased => ARB order was (partially) filled
                    log("ARB order bought some {0}.{1} (BASE balance={2:0.0000})", ConsoleColor.Cyan, _currency, _baseGatewayName, baseBalance);

                    //Increase BASE order
                    createBaseOrder(baseBalance);
                    if (-1 != _baseOrderId)
                    {
                        log("Updated BASE order ID={0}; amount={1:0.0000} {2}.{3};", _baseOrderId, _baseOrderAmount, _currency, _baseGatewayName);
                    }
                }
                else
                {
                    log("BASE order ID={0} untouched (amount={1:0.0000} {2}.{3})", _baseOrderId, baseOrder.Amount, _currency, _baseGatewayName);

                    List<FiatAsk> asks = _baseRequestor.GetOrderBookAsks(_currency, _arbGateway);
                    double newPrice = suggestPrice(asks, _baseMinPrice, _baseOrderId, _baseOrderPrice);

                    if (!_baseOrderPrice.eq(newPrice))
                    {
                        createBaseOrder(baseBalance, newPrice);
                        log("Updated BASE order ID={0}; amount={1} {2}.{3}; price={2} {3}",
                            _baseOrderId, _baseOrderAmount, _currency, _baseGatewayName, _baseOrderPrice, _currency);
                    }
                }
            }
            else if (baseBalance > _threshold)
            {
                //We have some spare BASE assets to sell
                createBaseOrder(baseBalance);
                if (-1 != _baseOrderId)
                {
                    log("Successfully created BASE order with ID={0}; amount={1:0.0000} {2}.{3}; price={4:0.0000} {2}.{5}",
                        ConsoleColor.Cyan, _baseOrderId, _baseOrderAmount, _currency, _baseGatewayName, _baseOrderPrice, _arbGatewayName);
                }
            }

            //ARB order
            if (-1 != _arbOrderId)
            {
                var arbOrder = _arbRequestor.GetOrderInfo(_arbOrderId);

                if (null == arbOrder)
                    return;

                //Filled or cancelled
                if (arbOrder.Closed)
                {
                    if (arbBalance.eq(_arbOrderAmount, 0.1) && _arbOrderAmount > _threshold)
                    {
                        log("ARB order ID={0} closed but asset validation failed (balance={1:0.0000} {2}). Assuming was cancelled, trying to recreate",
                            ConsoleColor.Yellow, _arbOrderId, arbBalance, _currency);
                        createArbOrder(arbBalance);

                        if (-1 != _arbOrderId)
                        {
                            log("Created ARB order with ID={0}; amount={1:0.0000} {2}.{3}; price={4:0.0000} {2}.{5}",
                                ConsoleColor.Cyan, _arbOrderId, _arbOrderAmount, _currency, _arbGatewayName, _arbOrderPrice, _baseGatewayName);
                        }
                    }
                    else
                    {
                        log("ARB order ID={0} (amount={1:0.0000} {2}) was closed at price={3}",
                            ConsoleColor.Green, _arbOrderId, _arbOrderAmount, _currency, _arbOrderPrice);
                        _arbOrderId = -1;
                        _arbOrderPrice = 0.0;
                        _arbOrderAmount = 0.0;

                        //Increase BASE order
                        createBaseOrder(baseBalance);
                        if (-1 != _baseOrderId)
                        {
                            log("Updated BASE order ID={0}; amount={1:0.0000} {2}.{3};", _baseOrderId, _baseOrderAmount, _currency, _baseGatewayName);
                        }
                    }
                }
                else if (arbOrder.Amount + _threshold < _arbOrderAmount)
                {
                    //Double-check with BASE balance that ARB order was indeed partially filled
                    if (baseBalance - _threshold > _lastBaseBalance)
                    {
                        _arbOrderAmount = arbOrder.Amount;
                        log("ARB order ID={0} partially filled. Remaining amount={1:0.0000} {2}.{3}",
                            ConsoleColor.Green, _arbOrderId, _arbOrderAmount, _currency, _baseGatewayName);

                        //Increase BASE order
                        createBaseOrder(baseBalance);
                        if (-1 != _baseOrderId)
                        {
                            log("Updated BASE order ID={0}; amount={1:0.0000} {2}.{3};", _baseOrderId, _baseOrderAmount, _currency, _baseGatewayName);
                        }
                    }
                    else
                    {
                        log("ARB order ID={0} amount decreased but BASE balance validation failed (was{1:0.0000}, is {2:0.0000}). Nothing to do here",
                            ConsoleColor.Yellow, _baseOrderId, _lastBaseBalance, baseBalance);
                    }
                }
                else if (arbOrder.Amount + _threshold < arbBalance)
                {
                    //ARB balance increased => BASE order was (partially) filled
                    log("BASE order bought some {0}.{1} (ARB balance={2:0.0000})", ConsoleColor.Cyan, _currency, _arbGatewayName, arbBalance);

                    //Increase ARB order
                    createArbOrder(arbBalance);
                    if (-1 != _arbOrderId)
                    {
                        log("Updated ARB order ID={0}; amount={1:0.0000} {2}.{3};", _arbOrderId, _arbOrderAmount, _currency, _arbGatewayName);
                    }
                }
                else
                {
                    log("ARB order ID={0} untouched (amount={1:0.0000} {2}.{3})", _arbOrderId, arbOrder.Amount, _currency, _arbGatewayName);

                    List<FiatAsk> asks = _arbRequestor.GetOrderBookAsks(_currency, _baseGateway);
                    double newPrice = suggestPrice(asks, _arbMinPrice, _arbOrderId, _arbOrderPrice);

                    if (!_arbOrderPrice.eq(newPrice))
                    {
                        createArbOrder(arbBalance, newPrice);
                        log("Updated ARB order ID={0}; amount={1} {2}.{3}; price={2} {3}",
                            _arbOrderId, _arbOrderAmount, _currency, _arbGatewayName, _arbOrderPrice, _currency);
                    }
                }
            }
            else if (arbBalance > _threshold)
            {
                //We have some spare ARB assets to sell
                createArbOrder(arbBalance);
                if (-1 != _arbOrderId)
                {
                    log("Successfully created ARB order with ID={0}; amount={1:0.0000} {2}.{3}; price={4:0.0000} {2}.{5}",
                        ConsoleColor.Cyan, _arbOrderId, _arbOrderAmount, _currency, _arbGatewayName, _arbOrderPrice, _baseGatewayName);
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


        private double suggestPrice(List<FiatAsk> asks, double minPrice, int currentOrderId, double currentOrderPrice)
        {
            const int DEC_PLACES = 14;
            const double INCREMENT = 0.0001;            //In same-fiat arbitrage we can afford this
            const double MIN_PRICE_UPDATE = 0.00005;    //and this

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
                    double sellPrice = Math.Round(ask.Price - INCREMENT, DEC_PLACES);

                    //The difference is too small. Leave previous price to avoid server call
                    if (-1 != currentOrderId && Math.Abs(sellPrice - currentOrderPrice) < MIN_PRICE_UPDATE)
                    {
                        log("DEBUG: price {0:0.00000} too similar, using previous", sellPrice);
                        return currentOrderPrice;
                    }

                    return sellPrice;
                }                
            }

            //Order book filled with junk. Use order before last, so we see it in chart
            double price = asks.Last().Price - INCREMENT;
            if (-1 != currentOrderId && Math.Abs(price - currentOrderPrice) < MIN_PRICE_UPDATE)
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
                List<FiatAsk> asks = _baseRequestor.GetOrderBookAsks(_currency, _arbGateway);
                price = suggestPrice(asks, _baseMinPrice, _baseOrderId, _baseOrderPrice);
            }

            int newOrderId = _baseRequestor.PlaceOrder(baseBalance, price, _currency, _arbGateway);
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
                List<FiatAsk> asks = _arbRequestor.GetOrderBookAsks(_currency, _baseGateway);
                price = suggestPrice(asks, _arbMinPrice, _arbOrderId, _arbOrderPrice);
            }

            int newOrderId = _arbRequestor.PlaceOrder(arbBalance, price, _currency, _baseGateway);
            if (-1 != newOrderId)
            {
                _arbOrderId = newOrderId;
                _arbOrderPrice = price;
                _arbOrderAmount = arbBalance;
            }
        }
    }
}
