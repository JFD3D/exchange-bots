using System;
using System.Collections.Generic;
using System.Runtime.Serialization;


namespace RippleBot.Business
{
    [DataContract]
    internal class AccountLinesResponse
    {
        [DataMember] internal int id { get; set; }
        [DataMember] internal string status { get; set; }
        [DataMember] internal string type { get; set; }
        [DataMember] internal AccountLineResult result { get; set; }
    }

    [DataContract]
    internal class Line
    {
        [DataMember] internal string account { get; set; }
        [DataMember] internal string balance { get; set; }
        [DataMember] internal string currency { get; set; }
        [DataMember] internal string limit { get; set; }
        [DataMember] internal string limit_peer { get; set; }
        [DataMember] internal int quality_in { get; set; }
        [DataMember] internal int quality_out { get; set; }
        [DataMember] internal bool? no_ripple { get; set; }


        internal double Balance
        {
            get { return double.Parse(balance); }
        }
    }

    [DataContract]
    internal class AccountLineResult
    {
        [DataMember] internal string account { get; set; }
        [DataMember] internal int ledger_current_index { get; set; }
        [DataMember] internal List<Line> lines { get; set; }
        [DataMember] internal bool validated { get; set; }
    }    
}
