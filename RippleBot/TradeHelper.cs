using System;
using System.Collections.Generic;
using System.Linq;

using Common.Business;
using RippleBot.Business.DataApi;


namespace RippleBot
{
    internal class TradeHelper
    {
        /// <summary>Returns numeric indicator of market activity. Higher value means higher activity (i.e. lot of trades with higher volume).</summary>
        /// <param name="candles">Recent trading statistics</param>
        /// <returns>Coeficient in [0.0, 1.0] where 0.0 means totally peacefull market, 1.0 is wild.</returns>
        internal static float GetMadness(List<Candle> candles)
        {
            //Bad response or no recent trading
            if (null == candles || !candles.Any())
                return 0.0f;

            var last5mCandle = candles.Last();

            //Last candle is too old
            if (last5mCandle.StartTime < DateTime.Now.Subtract(new TimeSpan(0, 5, 0)))
                return 0.0f;

            //Last candle has just been open, merge it with previous
            if (last5mCandle.IsPartial)
            {
                if (candles.Count > 1)
                {
                    var beforeLast = candles[candles.Count - 2];
                    last5mCandle = new Candle
                    {
                        startTime = beforeLast.startTime,
                        count = beforeLast.count + last5mCandle.count,
                        baseVolume = beforeLast.baseVolume + last5mCandle.count
                    };
                }
            }

            const int MIN_TRADES = 2;
            const int MAX_TRADES = 10;
            float intenseCoef;
            if (last5mCandle.count < MIN_TRADES)        //Too few trades
                intenseCoef = 0.0f;
            else if (last5mCandle.count >= MAX_TRADES)  //Too many trades
                intenseCoef = 1.0f;
            else
                intenseCoef = (float)(last5mCandle.count - MIN_TRADES) / (MAX_TRADES - MIN_TRADES);

            const double MIN_AVG_VOLUME = 400.0;
            const double MAX_AVG_VOLUME = 3000.0;
            float volumeCoef;
            double avgVolume = last5mCandle.baseVolume / last5mCandle.count;

            if (avgVolume < MIN_AVG_VOLUME)
                volumeCoef = 0.0f;
            else if (avgVolume >= MAX_AVG_VOLUME)
                volumeCoef = 1.0f;
            else
                volumeCoef = (float)((avgVolume - MIN_AVG_VOLUME) / (MAX_AVG_VOLUME - MIN_AVG_VOLUME));

            //Average of volume and frequency coeficients
            return (intenseCoef + volumeCoef) / 2;
        }

        internal static float GetMadness2(ExchangeHistoryResponse tradeHistory, int minAverageVolume, int maxAverageVolume)
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


            //TODO: float volumeCoef; count average volume



            //Average of volume and frequency coeficients
            return (intenseCoef + volumeCoef) / 2;
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
