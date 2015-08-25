using System.Collections.Generic;
using System.Runtime.Serialization;
using Common.Business;


namespace LakeBtcBot.Business
{
    [DataContract]
    internal class MarketDepthResponse : IMarketDepthResponse<Order>
    {
        public List</*List<double>*/Order> asks { get; set; }
        public List</*List<double>*/Order> bids { get; set; }

        #region IMarketDepthResponse members

        public List<Order> Bids { get { return bids; } }
        public List<Order> Asks { get { return asks; } }
        #endregion
    }


    [DataContract]
    internal class Order : List<double>, IMarketOrder
    {
        #region IMarketOrder implementations

        public double Price
        {
            get { return this[0]; }
        }

        public double Amount
        {
            get { return this[1]; }
        }
        #endregion
    }
}
