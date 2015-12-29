using System;
using System.Collections.Generic;
using System.IO;


namespace Common
{
    public static class Configuration       //TODO: non-static, instantiate only in composition root and pass it through DI
    {
        private static Dictionary<string, string> _values;

        public static string Strategy { get { return GetValue("STRATEGY"); } }
        public static string AccessKey { get { return GetValue("ACCESS_KEY"); } }
        public static string SecretKey { get { return GetValue("SECRET_KEY"); } }


        /// <summary>Read configuration file in form "key=value" per line, case insensitive. Lines not having this pattern are ignored.</summary>
        public static void Load(string fullPath)        //TODO: this is constructor
        {
            _values = new Dictionary<string, string>();

            using (var reader = new StreamReader(fullPath))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (!String.IsNullOrEmpty(line) && line.Contains("="))
                    {
                        var index = line.IndexOf('=');
                        var key = line.Substring(0, index);
                        var value = line.Substring(index + 1);
                        _values.Add(key.ToUpper(), value);
                    }
                }
            }
        }

        /// <summary>Get configuration value by key</summary>
        /// <param name="key">Case insensitive key to get value for</param>
        /// <returns>String value or NULL if configuration file didn't contain the given key</returns>
        public static string GetValue(string key)
        {
            key = key.ToUpper();
            if (!_values.ContainsKey(key))
            {
                return null;
            }
            return _values[key];
        }
    }
}
