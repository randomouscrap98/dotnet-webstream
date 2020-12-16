using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace stream
{
    //A single stream, usually associated with a room.
    public class StreamData
    {
        public DateTime CreateDate = DateTime.Now;
        public DateTime UpdateDate = DateTime.Now;
        public DateTime SaveDate = new DateTime(0);

        public StringBuilder Data = new StringBuilder();
        public readonly object Lock = new object();

        //The signaler is used to wake up waiting listeners
        public ManualResetEvent Signal = new ManualResetEvent(false);

        //The list of listeners should all get flushed every time a signal comes
        public List<StreamListener> Listeners = new List<StreamListener>();
    }

    //Data sent from a signal to waiters through the listener object. Should
    //be the same data for every listener!
    public class SignalData
    {
        //The amount of listeners at signal time.
        public int ListenersBeforeSignal = 0;
    }

    //The stream and signal data retrieved from waiting. Signal data will be null
    //if no waiting was involved.
    public class ReadyData
    {
        public string Data = "";
        public SignalData SignalData = null;
    }

    //Someone waiting for a stream. Signalled by data additions. The signal data is added
    //through this class.
    public class StreamListener
    {
        public Task Waiter = null;
        public SignalData SignalData = new SignalData();
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
        public TimeSpan SystemCheckInterval = TimeSpan.FromMinutes(1);
        public TimeSpan DeadRoomLimit = TimeSpan.FromHours(1); //a VERY aggressive saving system
        public int SavePerMinute = 10;
    }

    //A group of streams, categorized by key.
    public class StreamSystem : BackgroundService //IHostedService
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

        protected void SaveStream(string name, StreamData s, bool forceEmpty = false)
        {
            if(!Directory.Exists(Config.StoreLocation))
                Directory.CreateDirectory(Config.StoreLocation);

            //Don't save empty rooms (unless we're forced to!)
            if(s.Data.Length > 0 || forceEmpty)
                File.WriteAllText(Path.Combine(Config.StoreLocation, name), s.Data.ToString());

            s.SaveDate = DateTime.Now;
        }

        protected StreamData LoadStream(string name)
        {
            var filename = Path.Combine(Config.StoreLocation, name);

            if(File.Exists(filename))
                return new StreamData() { Data = new StringBuilder(File.ReadAllText(filename)) };

            return null;
        }

        protected override async Task ExecuteAsync(CancellationToken token)
        {
            while(!token.IsCancellationRequested)
            {
                try
                {
                    //It's ok to do this outside the lock because checking for dead streams isn't super 
                    //important. REMOVING is, so do that later in a lock.

                    var removals = new List<string>();

                    foreach (var s in Streams)
                    {
                        //Don't bother with things that aren't completely open right now
                        if (Monitor.TryEnter(s.Value.Lock))
                        {
                            try
                            {
                                //If nobody is listening to us and we're old, get rid of it
                                if (s.Value.Listeners.Count == 0 && DateTime.Now - s.Value.UpdateDate > Config.DeadRoomLimit)
                                {
                                    SaveStream(s.Key, s.Value);
                                    removals.Add(s.Key);
                                }
                            }
                            finally { Monitor.Exit(s.Value.Lock); }
                        }
                    }

                    if (removals.Count > 0)
                        logger.LogInformation($"Removing {removals.Count} dead rooms: {string.Join(", ", removals)}");

                    //Do NOT process streams unless we're holding the LOCK
                    lock (Lock)
                    {
                        removals.ForEach(x => Streams.Remove(x));
                    }

                    if (removals.Count > 0)
                        logger.LogInformation($"There are still {Streams.Count} open rooms");

                    //Find the streams that haven't been saved in the longest and save the first N of them.
                    var saveStreams = Streams.OrderBy(x => x.Value.SaveDate).Take((int)Math.Ceiling(Config.SavePerMinute * Config.SystemCheckInterval.TotalMinutes)).ToList();

                    //Don't need to lock on them: whatever data is in there is... probably fine? what if we're
                    //in the middle of writing chunk data though? the stream will be invalid! eh... that's the price
                    //for in-flight saving.
                    if (saveStreams.Count > 0)
                    {
                        saveStreams.ForEach(x => SaveStream(x.Key, x.Value));
                        logger.LogDebug($"Auto-Saved {saveStreams.Count} streams.");
                    }
                }
                catch(Exception ex)
                {
                    logger.LogError($"Failed during scheduled background work: {ex}");
                }

                await Task.Delay(Config.SystemCheckInterval);
            }

            //When you're ALL done, try to save them ALLLL
            await ForceSaveAll();
        }

        public async Task ForceSaveAll()
        {
            logger.LogInformation($"Force saving all {Streams.Count} streams (regardless of state)");
            await Task.Run(() => Streams.ToList().ForEach(x => SaveStream(x.Key, x.Value)));
        }

        public StreamData GetStream(string name)
        {
            //Don't let ANYBODY else mess with the dictionary while we're doing it!
            lock(Lock)
            {
                if(!Streams.ContainsKey(name))
                {
                    //Look for the stream in permament storage.
                    var existing = LoadStream(name);

                    //If it's there, add it! otherwise just add a new one
                    if(existing != null)
                    {
                        logger.LogInformation($"Reviving dead room {name}");
                        Streams.Add(name, existing);
                    }
                    else
                    {
                        Streams.Add(name, new StreamData());
                    }
                }

                return Streams[name];
            }
        }

        public string GetData(StreamData stream, int start, int count = -1)
        {
            lock(stream.Lock)
            {
                if(start < 0)
                    throw new InvalidOperationException("Start less than zero!");

                //I don't care what the inputs are, if the stream is empty, you can have it
                if(stream.Data.Length == 0)
                    return "";

                if(start >= stream.Data.Length)
                    throw new InvalidOperationException($"Start beyond end of data: {stream.Data.Length}!");

                if (count < 0 || count > stream.Data.Length - start)
                    count = stream.Data.Length - start;

                return stream.Data.ToString(start, count);
            }
        }

        public async Task<ReadyData> GetDataWhenReady(StreamData stream, int start, int count = -1)
        {
            //JUST IN CASE we need it later (can't make it in the lock section, needed outside!)
            bool completed = false;
            StreamListener listener = null; 
            ReadyData result = new ReadyData();

            lock(stream.Lock)
            {
                if(start < stream.Data.Length)
                {
                    //No waiting! We're already done! The stream can never get smaller!
                    completed = true;
                }
                else
                {
                    //Oh, waiting... we're a new listener so add it!
                    listener = new StreamListener();
                    listener.Waiter = Task.Run(() => completed = stream.Signal.WaitOne(Config.ListenTimeout));
                    stream.Listeners.Add(listener);
                }
            }

            //Don't need to listen if there's no listener!
            if(listener != null)
            {
                //CANNOT wait in the lock! We're just waiting to see if we get data. If we DOOOO, "completed" will be true!
                try
                {
                    await listener.Waiter;
                }
                finally
                {
                    //We're done. Doesn't matter what happened, whether it finished or we threw an exception,
                    //we are NO LONGER listening!
                    result.SignalData = listener.SignalData; //this might be nothing
                    stream.Listeners.Remove(listener);
                }
            }

            if(completed)
                result.Data = GetData(stream, start, count);

            return result;
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

                var signalData = new SignalData()
                {
                    ListenersBeforeSignal = stream.Listeners.Count
                };

                //Set the signal data before signalling so they have "communication" from us. NOT SAFE
                //since someone could... you know, add a listener... or could they? We hold the lock for
                //the stream and you can't add a listener without the lock! So probably safe...
                stream.Listeners.ForEach(x => x.SignalData = signalData);

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