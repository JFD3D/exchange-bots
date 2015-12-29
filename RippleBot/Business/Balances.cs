using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;


namespace RippleBot.Business
{
    [DataContract]
    internal class BalancesResponse
    {
        [DataMember] internal string result { get; set; }
        [DataMember] internal int ledger_index { get; set; }
        [DataMember] internal string close_time { get; set; }
        [DataMember] internal int limit { get; set; }
        [DataMember] internal List<Balance> balances { get; set; }

        internal bool IsError
        {
            get { return "success" != result; }
        }

        /// <summary>Get asset balance by its code</summary>
        /// <param name="assetCode">XRP, USD, CNY...</param>
        internal double Asset(string assetCode)
        {
            return balances.First(bal => assetCode == bal.currency).Available;
        }
    }

    [DataContract]
    internal class Balance
    {
        [DataMember] internal string value { get; set; }
        [DataMember] internal string currency { get; set; }
        [DataMember] internal string counterparty { get; set; }


        internal double Available
        {
            get
            {
                if (String.IsNullOrEmpty(value))
                    return 0.0;
                return double.Parse(value);
            }
        }
    }
}
