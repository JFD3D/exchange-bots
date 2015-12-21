using System.Collections.Generic;
using System.Runtime.Serialization;
using Common.Business;


namespace RippleBot.Business
{
    /// <summary>Order book, side of asks of trading one non-XRP asset for another</summary>
    [DataContract]
    internal class MarketDepthFiatAsksResponse
    {
        [DataMember] internal int id { get; set; }
        [DataMember] internal FiatAsks result { get; set; }
        [DataMember] internal string status { get; set; }
        [DataMember] internal string type { get; set; }
    }

    [DataContract]
    internal class FiatAsks
    {
        [DataMember] internal int ledger_current_index { get; set; }
        [DataMember] internal List<FiatAsk> offers { get; set; }
        [DataMember] internal bool validated { get; set; }
    }

    [DataContract]
    public class FiatAsk : IMarketOrder
    {
        [DataMember] internal string Account { get; set; }
        [DataMember] internal string BookDirectory { get; set; }
        [DataMember] internal string BookNode { get; set; }
        [DataMember] internal int Flags { get; set; }
        [DataMember] internal string LedgerEntryType { get; set; }
        [DataMember] internal string OwnerNode { get; set; }
        [DataMember] internal string PreviousTxnID { get; set; }
        [DataMember] internal int PreviousTxnLgrSeq { get; set; }
        [DataMember] internal int Sequence { get; set; }
        [DataMember] internal Take TakerGets { get; set; }
        [DataMember] internal Take TakerPays { get; set; }
        [DataMember] internal string index { get; set; }
        [DataMember] internal string quality { get; set; }
        [DataMember] internal int? Expiration { get; set; }
        [DataMember] internal Take taker_gets_funded { get; set; }
        [DataMember] internal Take taker_pays_funded { get; set; }

        /// <summary>Amount of fiat to sell</summary>
        public double Amount
        {
            get
            {
                return double.Parse(TakerGets.value);
            }
        }

        public double Price
        {
            get
            {
                var fiat = double.Parse(TakerPays.value);
                return fiat / Amount;
            }
        }
    }
}
