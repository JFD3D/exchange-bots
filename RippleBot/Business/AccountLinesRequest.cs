using System;
using System.Runtime.Serialization;


namespace RippleBot.Business
{
    [DataContract]
    internal class AccountLinesRequest
    {
        [DataMember] internal readonly int id = 81;
        [DataMember] internal readonly string command = "account_lines";
        [DataMember] internal string account { get; set; }
        [DataMember] internal string ledger = "current";
    }
}
