using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace stream
{
    //A single stream, usually associated with a room.
    public class StreamData
    {
        public DateTime CreateDate = DateTime.Now;
        public DateTime UpdateDate = DateTime.Now;

        public StringBuilder Data = new StringBuilder();
        public readonly object Lock = new object();

        //The signaler is used to wake up waiting listeners
        public ManualResetEvent Signal = new ManualResetEvent(false);

        //The list of listeners should all get flushed every time a signal comes
        public List<StreamListener> Listeners = new List<StreamListener>();
    }

    public class StreamListener
    {
        public Task Waiter = null;
    }

    //Configuration for the stream system
    public class StreamConfig
    {
        public int SingleDataLimit {get;set;} = -1;
        public int StreamDataLimit {get;set;} = -1;
        public string StoreLocation {get;set;} = null;

        //Stuff I don't want to set in the json configs.
        public TimeSpan ListenTimeout = TimeSpan.FromSeconds(300); //This length PROBABLY doesn't matter....???
        public TimeSpan SignalTimeout = TimeSpan.FromSeconds(30);
        public TimeSpan SignalWaitInterval = TimeSpan.FromMilliseconds(20);
    }

    //A group of streams, categorized by key.
    public class StreamSystem
    {
        protected readonly ILogger<StreamSystem> logger;
        protected readonly object Lock = new object();

        public StreamConfig Config;
        public Dictionary<string, StreamData> Streams = new Dictionary<string, StreamData>();

        public StreamSystem(ILogger<StreamSystem> logger, StreamConfig config)
        {
            this.logger = logger;
            this.Config = config;
        }

        public StreamData GetStream(string name)
        {
            //Don't let ANYBODY else mess with the dictionary while we're doing it!
            lock(Lock)
            {
                if(!Streams.ContainsKey(name))
                    Streams.Add(name, new StreamData());

                return Streams[name];
            }
        }

        public string GetData(StreamData stream, int start)
        {
            lock(stream.Lock)
            {
                if(start < 0)
                    throw new InvalidOperationException("Start less than zero!");
                if(start >= stream.Data.Length)
                    throw new InvalidOperationException($"Start beyond end of data: {stream.Data.Length}!");

                return stream.Data.ToString(start, stream.Data.Length - start);
            }
        }

        public async Task<string> GetDataWhenReady(StreamData stream, int start)
        {
            //JUST IN CASE we need it later (can't make it in the lock section, needed outside!)
            bool completed = false;
            var listener = new StreamListener() ;

            lock(stream.Lock)
            {
                //No waiting!
                if(start < stream.Data.Length)
                    return GetData(stream, start);
                
                //Oh, waiting... we're a new listener so add it!
                listener.Waiter = Task.Run(() => completed = stream.Signal.WaitOne(Config.ListenTimeout));
                stream.Listeners.Add(listener);
            }

            //CANNOT wait in the lock! We're just waiting to see if we get data. If we DOOOO, "completed"
            //will be true!
            try
            {
                await listener.Waiter;
            }
            finally
            {
                //We're done. Doesn't matter what happened, whether it finished or we threw an exception,
                //we are NO LONGER listening!
                stream.Listeners.Remove(listener);
            }

            if(completed)
                return GetData(stream, start);
            else
                return ""; //No data to return!
        }

        public void AddData(StreamData stream, string data)
        {
            lock(stream.Lock)
            {
                if(data.Length == 0)
                    throw new InvalidOperationException("Can't add 0 length data!");

                if(data.Length > Config.SingleDataLimit)
                    throw new InvalidOperationException($"Too much data at once!: {Config.SingleDataLimit}");

                //Don't allow data additions that would allow the limit to go beyond thingy!
                if(stream.Data.Length >= Config.StreamDataLimit)
                    throw new InvalidOperationException($"Stream at data limit: {Config.StreamDataLimit}");

                stream.Data.Append(data);
                stream.UpdateDate = DateTime.Now;

                //Set the signal so all the listeners know they have data!
                stream.Signal.Set();

                try
                {
                    var signalStart = DateTime.Now;

                    //Wait for OUR listeners to clear out! Notice that the listener wait and removal is NOT 
                    //in a lock: this allows US to hold the lock (since it's probably safer...? we're doing the signalling).
                    while (stream.Listeners.Count > 0)
                    {
                        System.Threading.Thread.Sleep(Config.SignalWaitInterval);

                        if (DateTime.Now - signalStart > Config.SignalTimeout)
                        {
                            logger.LogWarning("Timed out while waiting for listeners to process signal!");
                            break;
                        }
                    }
                }
                finally
                {
                    //ALWAYS get rid of listeners and reset the signal! we don't want to be left in an unknown state!
                    stream.Listeners.Clear(); //This might be dangerous? IDK, we don't want to wait forever!
                    stream.Signal.Reset();
                }
            }
        }
    }

}