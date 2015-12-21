using System;
using Common;


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
        private double _baseSellPrice;
        private double _arbSellPrice;

        private double _lastBaseBalance = -1.0;
        private double _lastArbBalance = -1.0;

        private RippleApi _baseRequestor;
        private RippleApi _arbRequestor;

        private double _threshold;

        //Active BASE order ID
        private int _baseOrderId = -1;
        //Active BASE order amount
        private double _baseOrderAmount;

        //Active ARB order ID
        private int _arbOrderId = -1;
        //Active ARB order amount
        private double _arbOrderAmount;


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

            _arbSellPrice = double.Parse(Configuration.GetValue("arb_sell_price"));
            _baseSellPrice = double.Parse(Configuration.GetValue("base_sell_price"));
            _threshold = double.Parse(Configuration.GetValue("amount_threshold"));
            _intervalMs = 15000;

            _baseRequestor = new RippleApi(_logger, _baseGateway, _currency);
            _baseRequestor.Init();
            _arbRequestor = new RippleApi(_logger, _arbGateway, _currency);
            _arbRequestor.Init();
            log("Direct arbitrage trader started for currency {0}; gateways {1}/{2}; lower={3:0.0000}; upper={4:0.0000}",
                _currency, _baseGatewayName, _arbGatewayName, _arbSellPrice, _baseSellPrice);
        }

        /// <summary>
        /// Zero knowledge about order book. Simply try to trade the fiat of one gateway for the other
        /// </summary>
        protected override void Check()
        {
            double baseBalance = _baseRequestor.GetBalance(_currency);
            double arbBalance = _arbRequestor.GetBalance(_currency);

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
                                ConsoleColor.Cyan, _baseOrderId, _baseOrderAmount, _currency, _baseGatewayName, _baseSellPrice, _arbGatewayName);
                        }
                    }
                    else
                    {
                        log("BASE order ID={0} (amount={1:0.0000} {2}) was closed at price={3}",
                            ConsoleColor.Green, _baseOrderId, _baseOrderAmount, _currency, _baseSellPrice);
                        _baseOrderId = -1;
                        _baseOrderAmount = 0;

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
                }
            }
            else if (baseBalance > _threshold)
            {
                //We have some spare BASE assets to sell
                createBaseOrder(baseBalance);
                if (-1 != _baseOrderId)
                {
                    log("Successfully created BASE order with ID={0}; amount={1:0.0000} {2}.{3}; price={4:0.0000} {2}.{5}",
                        ConsoleColor.Cyan, _baseOrderId, _baseOrderAmount, _currency, _baseGatewayName, _baseSellPrice, _arbGatewayName);
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
                                ConsoleColor.Cyan, _arbOrderId, _arbOrderAmount, _currency, _arbGatewayName, _arbSellPrice, _baseGatewayName);
                        }
                    }
                    else
                    {
                        log("ARB order ID={0} (amount={1:0.0000} {2}) was closed at price={3}",
                            ConsoleColor.Green, _arbOrderId, _arbOrderAmount, _currency, _arbSellPrice);
                        _arbOrderId = -1;
                        _arbOrderAmount = 0;

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
                }
            }
            else if (arbBalance > _threshold)
            {
                //We have some spare ARB assets to sell
                createArbOrder(arbBalance);
                if (-1 != _arbOrderId)
                {
                    log("Successfully created ARB order with ID={0}; amount={1:0.0000} {2}.{3}; price={4:0.0000} {2}.{5}",
                        ConsoleColor.Cyan, _arbOrderId, _arbOrderAmount, _currency, _arbGatewayName, _arbSellPrice, _baseGatewayName);
                }
            }

            _lastBaseBalance = baseBalance;
            _lastArbBalance = arbBalance;
            log(new string('=', 74));
        }


        private void createBaseOrder(double baseBalance)
        {
            if (-1 != _baseOrderId)
            {
                if (!_baseRequestor.CancelOrder(_baseOrderId))
                {
                    return;
                }
            }

            int newOrderId = _baseRequestor.PlaceOrder(baseBalance, _baseSellPrice, _currency, _arbGateway);
            if (-1 != newOrderId)
            {
                _baseOrderId = newOrderId;
                _baseOrderAmount = baseBalance;
            }
        }

        private void createArbOrder(double arbBalance)
        {
            if (-1 != _arbOrderId)
            {
                if (!_arbRequestor.CancelOrder(_arbOrderId))
                {
                    return;
                }
            }

            int newOrderId = _arbRequestor.PlaceOrder(arbBalance, _arbSellPrice, _currency, _baseGateway);
            if (-1 != newOrderId)
            {
                _arbOrderId = newOrderId;
                _arbOrderAmount = arbBalance;
            }
        }
    }
}
