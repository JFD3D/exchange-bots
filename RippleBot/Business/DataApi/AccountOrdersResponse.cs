using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

//TODO: when refactoring is finished, delete Offers.cs
namespace RippleBot.Business.DataApi
{
    /// <summary>Response data deserialization for response from data API.</summary>
    /// <remarks>See <code>GET_orders.json</code> for sample response being deserialized with this class</remarks>
    [DataContract]
    internal class AccountOrdersResponse
    {
        [DataMember] internal string result { get; set; }
        [DataMember] internal int ledger_index { get; set; }
        [DataMember] internal string close_time { get; set; }
        [DataMember] internal int limit { get; set; }
        [DataMember] internal List<Order> orders { get; set; }
    }

    [DataContract]
    internal class Order
    {
        [DataMember] internal Specification specification { get; set; }
        [DataMember] internal Properties properties { get; set; }

        internal int OrderId
        {
            get { return properties.sequence; }
        }

        /// <summary>True if this order was fully filled or cancelled</summary>
        internal bool Closed { get; private set; }

        internal Order()
        {
            //Serialization purposes
        }

        internal Order(bool closed)
        {
            Closed = closed;
        }

        /// <summary>Base asset code</summary>
        internal string BaseAsset
        {
            get { return specification.quantity.currency; }
        }

        /// <summary>Counter asset code</summary>
        internal string CounterAsset
        {
            get { return specification.totalPrice.currency; }
        }

        /// <summary>Base asset gateway address</summary>
        internal string BaseGateway
        {
            get
            {
                return String.IsNullOrWhiteSpace(specification.quantity.counterparty)
                    ? null
                    : specification.quantity.counterparty;
            }
        }

        /// <summary>Counter asset gateway address</summary>
        internal string CounterGateway
        {
            get
            {
                return String.IsNullOrWhiteSpace(specification.quantity.counterparty)
                    ? null
                    : specification.totalPrice.counterparty;
            }
        }

        /// <summary>Get buy price of one of the assets of this order</summary>
        /// <param name="assetToBuy">Code for one of the assets on this offer</param>
        /// <param name="assetGateway">
        /// Optional gateway address of the asset being bought. Makes sense only if an asset is traded for the same
        /// asset between two gateways.
        /// </param>
        internal double BuyPrice(string assetToBuy, string assetGateway = null)
        {
            gatewayCheck(assetGateway);

            if ("buy" == specification.direction.ToLower() && assetToBuy == specification.quantity.currency &&
                (String.IsNullOrEmpty(assetGateway) || assetGateway == specification.quantity.counterparty))
            {
                return specification.totalPrice.Amount / specification.quantity.Amount;
            }
            //"sell" OR counter-asset
            return specification.quantity.Amount / specification.totalPrice.Amount;
        }

        internal double Amount(string assetCode, string assetGateway = null)
        {
            gatewayCheck(assetGateway);

            if (assetCode == specification.quantity.currency &&
                (String.IsNullOrEmpty(assetGateway) || assetGateway == specification.quantity.counterparty))
            {
                return specification.quantity.Amount;
            }

            return specification.totalPrice.Amount;
        }

        private void gatewayCheck(string gatewayAddress)
        {
            if (specification.quantity.currency == specification.totalPrice.currency && String.IsNullOrEmpty(gatewayAddress))
            {
                throw new ArgumentException("Insufficient asset identification. When buy and sell asset codes are equal, gateway must be specified.");
            }
        }
    }

    [DataContract]
    internal class Specification
    {
        [DataMember] internal string direction { get; set; }
        [DataMember] internal Quantity quantity { get; set; }
        [DataMember] internal TotalPrice totalPrice { get; set; }
    }

    [DataContract]
    internal class Properties
    {
        [DataMember] internal string maker { get; set; }
        [DataMember] internal int sequence { get; set; }
        [DataMember] internal string makerExchangeRate { get; set; }
    }

    [DataContract]
    internal class Quantity
    {
        [DataMember] internal string currency { get; set; }
        [DataMember] internal string value { get; set; }
        [DataMember] internal string counterparty { get; set; }

        internal double Amount
        {
            get { return Double.Parse(value); }
        }
    }

    [DataContract]
    internal class TotalPrice
    {
        [DataMember] internal string currency { get; set; }
        [DataMember] internal string value { get; set; }
        [DataMember] internal string counterparty { get; set; }

        internal double Amount
        {
            get { return Double.Parse(value); }
        }
    }
}
