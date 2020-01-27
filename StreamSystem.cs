using System;
using System.Collections.Generic;
using System.Text;

namespace stream
{
    //A single stream, usually associated with a room.
    public class StreamData
    {
        public DateTime CreateDate = DateTime.Now;
        public DateTime UpdateDate = DateTime.Now;

        public StringBuilder Data = new StringBuilder();
        public readonly object Lock = new object();
    }

    //Configuration for the stream system
    public class StreamConfig
    {
        public int SingleDataLimit = -1;
        public int StreamDataLimit = -1;
        public string StoreLocation {get;set;} = null;
    }

    //A group of streams, categorized by key.
    public class StreamSystem
    {
        protected readonly object Lock = new object();

        public StreamConfig Config;
        public Dictionary<string, StreamData> Streams = new Dictionary<string, StreamData>();

        public StreamSystem(StreamConfig config)
        {
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

        public void AddData(StreamData stream, string data)
        {
            lock(stream.Lock)
            {
                if(data.Length > Config.SingleDataLimit)
                    throw new InvalidOperationException($"Too much data at once!: {Config.SingleDataLimit}");

                //Don't allow data additions that would allow the limit to go beyond thingy!
                if(stream.Data.Length >= Config.StreamDataLimit)
                    throw new InvalidOperationException($"Stream at data limit: {Config.StreamDataLimit}");

                stream.Data.Append(data);
                stream.UpdateDate = DateTime.Now;
            }
        }
    }

}