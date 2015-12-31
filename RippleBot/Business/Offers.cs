using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Common;


namespace RippleBot.Business
{
    //TODO: delete when AccountOrders is ready and well tested

    [DataContract]
    internal class OffersResponse
    {
        [DataMember] internal int id { get; set; }
        [DataMember] internal OffersResult result { get; set; }
        [DataMember] internal string status { get; set; }
        [DataMember] internal string type { get; set; }
    }

    [DataContract]
    internal class OffersResult
    {
        [DataMember] internal string account { get; set; }
        [DataMember] internal List<Offer> offers { get; set; }
    }

    [DataContract]
    internal class Offer
    {
        [DataMember] internal int flags { get; set; }
        /// <summary>The ID number of this order</summary>
        [DataMember] internal int seq { get; set; }
        [DataMember] internal Take taker_gets { get; set; }
        [DataMember] internal Take taker_pays { get; set; }

        internal TradeType Type
        {
            get { return taker_gets.currency == "XRP" ? TradeType.SELL : TradeType.BUY; }
        }

        private double _amountXrp;//TODO = -1.0;
        internal double AmountXrp
        {
            get
            {
                if (_amountXrp.eq(0.0))
                {
                    var value = TradeType.BUY == Type
                        ? taker_pays.value
                        : taker_gets.value;
                    var valNumber = double.Parse(value);
                    _amountXrp = valNumber / Const.DROPS_IN_XRP;
                }

                return _amountXrp;
            }
        }

        private double _amount;//TODO = -1.0;
        internal double Amount
        {
            get
            {
                if (_amount.eq(0.0))
                {
                    var value = TradeType.BUY == Type
                        ? taker_gets.value
                        : taker_pays.value;
                    _amount = double.Parse(value);
                }

                return _amount;
            }
        }

        internal double GetAmount(string assetCode, string assetGateway = null)     //TODO: just "Amount"
        {
            if (assetCode == taker_gets.currency && assetCode == taker_pays.currency && String.IsNullOrEmpty(assetGateway))
            {
                throw new ArgumentNullException("Ambiguous asset identification. Gateway address must be given for same-asset offers", "assetGateway");
            }

            if (assetCode == taker_gets.currency &&
                (String.IsNullOrEmpty(assetGateway) || assetGateway == taker_gets.issuer))
            {
                return Double.Parse(taker_gets.value);
            }

            if (assetCode == taker_pays.currency &&
                (String.IsNullOrEmpty(assetGateway) || assetGateway == taker_pays.issuer))
            {
                return Double.Parse(taker_pays.value);
            }

            throw new Exception(String.Format("No such asset in this order: {0}.{1}", assetCode, assetGateway));
        }

        /// <summary>Price of one XRP in USD</summary>
        internal double Price
        {
            get { return Amount / AmountXrp; }
        }

        /// <summary>Currency code for the fiat side of an offer</summary>
        internal string Currency
        {
            get
            {
                return taker_gets.currency == "XRP"
                    ? taker_pays.currency
                    : taker_gets.currency;
            }
        }

        /// <summary>True if this order was fully filled or cancelled</summary>
        internal bool Closed { get; private set; }

        internal Offer()
        {
            //Serialization purposes
        }

        internal Offer(bool closed)
        {
            Closed = closed;
        }
    }

    [DataContract]
    internal class Take
    {
        [DataMember(EmitDefaultValue = false)]
        internal string currency { get; set; }

        [DataMember(EmitDefaultValue = false)]
        internal string issuer { get; set; }

        [DataMember(EmitDefaultValue = false)]
        internal string value { get; set; }
    }
}
