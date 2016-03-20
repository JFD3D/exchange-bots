using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;


namespace BitfinexBot.Business
{
    [DataContract]
    internal class Balance
    {
        [DataMember] internal string type { get; set; }
        [DataMember] internal string currency { get; set; }
        [DataMember] internal string amount { get; set; }
        [DataMember] internal string available { get; set; }

        /// <summary>Overall amount of the underlying account and respective currency</summary>
        internal double Amount
        {
            get { return double.Parse(amount); }
        }

        /// <summary>Amount available for exchange trading</summary>
        internal double Available
        {
            get { return double.Parse(available); }
        }
    }

    internal static class BalancesExtensions
    {
        public static Balance GetExchangeBalance(this List<Balance> balances, string currencyCode)
        {
            var nullBalance = new Balance
                {
                    available = "0",
                    amount = "0",
                    currency = currencyCode,
                    type = "DUMMY_NULL_REPLACEMENT"
                };

            if (null == balances)
            {
                return nullBalance;
            }

            return balances.FirstOrDefault(b => b.type == "exchange" && b.currency == currencyCode.ToLowerInvariant()) ?? nullBalance;
        }
    }
}
