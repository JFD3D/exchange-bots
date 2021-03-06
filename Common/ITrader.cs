﻿
namespace Common
{
    public interface ITrader
    {
        /// <summary>Launch trading loop</summary>
        void StartTrading();

        /// <summary>Terminate trading loop</summary>
        void Kill();
    }
}