using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;

namespace stream
{
    public class RandomNameAssociationConfig
    {
        public int InitialNameLength = 5;
        public int RetryIncreaseName = 20;
    }

    public class RandomNameAssociation<T> //where T : struct
    {
        protected ILogger logger;
        protected readonly object Lock = new object();
        protected RandomNameAssociationConfig config;

        public Dictionary<string, T> Keys = new Dictionary<string, T>();
        public Random rng = new Random();

        public RandomNameAssociation(ILogger<RandomNameAssociation<T>> logger, RandomNameAssociationConfig config)
        {
            this.logger = logger;
            this.config = config;
        }

        /// <summary>
        /// Generate a nice random name using ONLY lowercase letters up to the desired amount
        /// </summary>
        /// <param name="chars"></param>
        /// <returns></returns>
        public string GenerateRandomName(int chars)
        {
            var name = new StringBuilder();
            for(int i = 0; i < chars; i++)
                name.Append((char)('a' + (rng.Next() % 26)));
            return name.ToString();
        }
        
        /// <summary>
        /// Create a linked object with a random name. If already linked, do nothing.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public string GetLink(T item)
        {
            lock(Lock)
            {
                var key = Keys.FirstOrDefault(x => x.Value.Equals(item)).Key;
                int retries = 0;

                while (key == null)
                {
                    key = GenerateRandomName(config.InitialNameLength + (retries / config.RetryIncreaseName));

                    if(!Keys.ContainsKey(key))
                    {
                        Keys.Add(key, item);
                        logger.LogInformation($"Added key {key} linking to {item}, {Keys.Count} total keys");
                    }
                    else
                    {
                        key = null;
                    }

                    retries++;
                }

                return key;
            }
        }

        public T GetItem(string key)
        {
            lock(Lock)
            {
                if(Keys.ContainsKey(key))
                    return Keys[key];
            }

            throw new InvalidOperationException($"No key link found for {key}");
        }
    }
}