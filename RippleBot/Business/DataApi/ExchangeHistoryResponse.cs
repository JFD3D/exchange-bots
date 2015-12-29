using System;
using System.Collections.Generic;
using System.Runtime.Serialization;


namespace RippleBot.Business.DataApi
{
    /// <summary>
    /// Deserialization object for data API response of "Get Exchanges" call. Contains recent trades
    /// for an asset pair that was in request.
    /// </summary>
    [DataContract]
    internal class ExchangeHistoryResponse
    {
        [DataMember] internal string result { get; set; }
        [DataMember] internal int count { get; set; }
        [DataMember] internal string marker { get; set; }
        [DataMember] internal List<Exchange> exchanges { get; set; }
    }

    [DataContract]
    internal class Exchange
    {
        [DataMember] internal string base_amount { get; set; }
        [DataMember] internal string counter_amount { get; set; }
        [DataMember] internal int node_index { get; set; }
        [DataMember] internal string rate { get; set; }
        [DataMember] internal int tx_index { get; set; }
        [DataMember] internal string buyer { get; set; }
        [DataMember] internal string executed_time { get; set; }
        [DataMember] internal int ledger_index { get; set; }
        [DataMember] internal int offer_sequence { get; set; }
        [DataMember] internal string provider { get; set; }
        [DataMember] internal string seller { get; set; }
        [DataMember] internal string taker { get; set; }
        [DataMember] internal string tx_hash { get; set; }
        [DataMember] internal string tx_type { get; set; }
        [DataMember] internal string base_currency { get; set; }
        [DataMember] internal string counter_currency { get; set; }
        [DataMember] internal string counter_issuer { get; set; }
        [DataMember] internal string autobridged_currency { get; set; }
        [DataMember] internal string autobridged_issuer { get; set; }

        /// <summary>Offer execution time in current time zone</summary>
        internal DateTime Time
        {
            get { return DateTime.Parse(executed_time); }
        }
    }
}
