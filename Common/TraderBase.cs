﻿using System;
using System.Threading;


namespace Common
{
    public abstract class TraderBase : ITrader
    {
        private int ERROR_RECOVERY_BREAK = 10 * 60 * 1000;        //10min after fatal exception to revive
        private bool _killSignal;
        private readonly bool _verbose = true;
        private readonly Logger _logger;
        protected int _intervalMs;
        protected bool _cleanup;


        protected TraderBase(Logger logger)
        {
            _logger = logger;
        }


        public void StartTrading()
        {
            do
            {
                try
                {
                    Check();
                    Thread.Sleep(_intervalMs);
                }
                catch (Exception ex)
                {
                    log("ERROR: " + ex.Message + Environment.NewLine + ex.StackTrace);
                    _logger.AppendMessage(Environment.NewLine + "Last response:" + Environment.NewLine + _logger.LastResponse + Environment.NewLine,
                                          true, ConsoleColor.Cyan);
//                    throw;
                    Thread.Sleep(ERROR_RECOVERY_BREAK);
                    log("Resurrected after " + ERROR_RECOVERY_BREAK + "ms, let's try again.", ConsoleColor.Magenta);
                }
            } while (!_killSignal);
        }

        public void Kill()
        {
            _killSignal = true;
            log("Trader received kill signal. Good bye.");
        }


        /// <summary>Single round in trading. Check status of active </summary>
        protected abstract void Check();


        protected void log(string message, ConsoleColor color, params object[] args)
        {
            if (_verbose) //TODO: select verbose and non-verbose messages
            {
                try
                {
                    _logger.AppendMessage(String.Format(message, args), true, color);
                }
                catch (FormatException)
                {
                    var argz = null == args || 0 == args.Length
                        ? "NULL"
                        : String.Join(",", args);
                    _logger.AppendMessage("Couldn't log message '" + message + "',  args=" + argz, true, ConsoleColor.Red);
                }
            }
        }

        protected void log(string message, params object[] args)
        {
            if (_verbose) //TODO: select verbose and non-verbose messages
            {
                try
                {
                    _logger.AppendMessage(String.Format(message, args));
                }
                catch (FormatException)
                {
                    var argz = null == args || 0 == args.Length
                        ? "NULL"
                        : String.Join(",", args);
                    _logger.AppendMessage("Couldn't log message '" + message + "',  args=" + argz, true, ConsoleColor.Red);
                }
            }
        }
    }
}
