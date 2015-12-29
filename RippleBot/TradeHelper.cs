using System;
using System.Collections.Generic;
using System.Linq;

using Common.Business;
using RippleBot.Business.DataApi;


namespace RippleBot
{
    internal class TradeHelper
    {
        /// <summary>
        /// Returns numeric indicator of market activity between 0 and 1 inclusive. Higher value means
        /// higher activity (i.e. lot of trades with higher volume).
        /// </summary>
        /// <param name="tradeHistory">Recent trading statistics</param>
        /// <param name="assetCode">Asset code to evaluate volume for</param>
        /// <param name="minAverageVolume">
        /// Trade volume under this threshold is considered negligible (resulting madness will be at most 0.5)
        /// </param>
        /// <param name="maxAverageVolume">
        /// Trade volume above this threshold is considered very high (resulting madness will be at least 0.5)
        /// </param>
        /// <returns>Coeficient in [0.0, 1.0] where 0.0 means totally peacefull market, 1.0 is wild.</returns>
        internal static float GetMadness(ExchangeHistoryResponse tradeHistory, string assetCode, double minAverageVolume, double maxAverageVolume)
        {
            //Bad response or no recent trading
            if (null == tradeHistory || !tradeHistory.exchanges.Any())
            {
                return 0.0f;
            }

            var lastTrade = tradeHistory.exchanges[0];

            //Last trade is too old
            if (lastTrade.Time < DateTime.Now.Subtract(new TimeSpan(0, 5, 0)))
            {
                return 0.0f;
            }

            //Assuming the tradeHistory contains constant count of past trades (i.e. 10). The timeframe
            //in which they were executed and their volume decides the madness coeficient.
            float intenseCoef;

            TimeSpan MIN_TIME_FRAME = new TimeSpan(0, 2, 0);
            TimeSpan MAX_TIME_FRAME = new TimeSpan(0, 10, 0);

            Exchange oldestTrade = tradeHistory.exchanges.Last();

            if (oldestTrade.Time > DateTime.Now - MIN_TIME_FRAME)
            {
                intenseCoef = 1.0f;
            }
            else if (oldestTrade.Time < DateTime.Now - MAX_TIME_FRAME)
            {
                intenseCoef = 0.0f;
            }
            else
            {
                //Get average trade age in seconds
                double totalTimeSpan = (lastTrade.Time - oldestTrade.Time).TotalSeconds;

                double ageSum = 0.0;
                var now = DateTime.Now;
                foreach (Exchange trade in tradeHistory.exchanges)
                {
                    ageSum += (now - trade.Time).TotalSeconds;
                }

                double averageAge = ageSum / tradeHistory.exchanges.Count;

                intenseCoef = (float)(1.0 - (averageAge / totalTimeSpan));
            }

            float volumeCoef;
            //Count average volume
            double volumeSum = 0.0;

            foreach (Exchange trade in tradeHistory.exchanges)
            {
                double amount = assetCode == trade.base_currency
                                    ? Double.Parse(trade.base_amount)
                                    : Double.Parse(trade.counter_amount);
                volumeSum += amount;
            }

            double avgVolume = volumeSum / tradeHistory.exchanges.Count;

            if (avgVolume < minAverageVolume)
            {
                volumeCoef = 0.0f;
            }
            else if (avgVolume >= maxAverageVolume)
            {
                volumeCoef = 1.0f;
            }
            else
            {
                volumeCoef = (float)((avgVolume - minAverageVolume) / (maxAverageVolume - minAverageVolume));
            }

            //Average of volume and frequency coeficients
            return (intenseCoef + volumeCoef) / 2.0f;
        }

        /// <summary>
        /// From list of orders (can be either asks or bids) returns the first with accumulated
        /// amount higher than given threshold
        /// </summary>
        /// <param name="orders">
        /// Current orders on any side of the orderbook. Must be ordered in orderbook logic (e.g. highest bid/lowest ask first)
        /// </param>
        /// <param name="minAmount">Order size threshold</param>
        internal static T GetFirstLiquidOrder<T>(List<T> orders, double minAmount) where T : IMarketOrder
        {
            double amountSum = 0.0;
            foreach (var order in orders)
            {
                amountSum += order.Amount;

                if (amountSum > minAmount)
                {
                    return order;
                }                
            }

            //Hard to believe but orderbook is filled with only poor orders. Just return first here.
            return orders[0];
        }
    }
}
