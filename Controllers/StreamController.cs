using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace stream.Controllers
{
    public class StreamControllerConfig
    {
        public string AcceptableRoom {get;set;} = null;
    }


    [ApiController]
    [Route("[controller]")]
    public class StreamController : ControllerBase
    {
        public class StreamQuery
        {
            public int start {get;set;} = 0;
            public int count {get;set;} = -1;
            public bool nonblocking {get;set;} = false;
            public bool readonlyname {get;set;} = false;
        }

        public class StreamResult
        {
            public string data {get;set;}
            public string readonlykey {get;set;}
            public int signalled {get;set;}
            public int used {get;set;}
            public int limit {get;set;}
        }

        public class Constants
        {
            public int maxStreamSize {get;set;}
            public int maxSingleChunk {get;set;}
        }

        private readonly ILogger<StreamController> _logger;
        private static DateTime LastSaveAll = new DateTime(0);
        private static readonly object SaveAllLock = new object();

        public StreamControllerConfig Config;
        protected StreamSystem rooms;
        protected RandomNameAssociation<string> readonlyNames;

        public StreamController(ILogger<StreamController> logger, StreamControllerConfig config, 
            StreamSystem rooms, RandomNameAssociation<string> readonlyNames)
        {
            _logger = logger;
            this.Config = config;
            this.rooms = rooms;
            this.readonlyNames = readonlyNames;
        }

        protected bool IsRoomAcceptable(string room)
        {
            return Regex.IsMatch(room, Config.AcceptableRoom);
        }

        protected async Task<StreamResult> GetStreamResult(string room, StreamQuery query = null)
        {
            //This will throw an exception if there's no GetItem association, that should be good enough.
            if(query?.readonlyname == true)
                room = readonlyNames.GetItem(room);

            if(!IsRoomAcceptable(room))
                throw new InvalidOperationException("Room name has invalid characters! Try something simpler!");

            var s = rooms.GetStream(room);
            var r = readonlyNames.GetLink(room);

            var result = new StreamResult()
            {
                limit = rooms.Config.StreamDataLimit,
                used = s.Data.Length,
                readonlykey = r,
                signalled = 0
            };

            if(query != null)
            {
                if(query.nonblocking)
                {
                    result.data = rooms.GetData(s, query.start, query.count);
                }
                else 
                {
                    var data = await rooms.GetDataWhenReady(s, query.start, query.count);
                    result.data = data.Data;

                    if(data.SignalData != null)
                        result.signalled = data.SignalData.ListenersBeforeSignal;
                }
            }

            return result;
        }

        protected async Task<ActionResult<T>> HandleException<T>(Func<Task<T>> attempt)
        {
            try
            {
                return await attempt();
            }
            catch(InvalidOperationException ex)
            {
                _logger.LogWarning($"System threw a 'handled' exception: {ex.Message}");
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("{room}")]
        public Task<ActionResult<string>> Get(string room, [FromQuery]StreamQuery query = null)
        {
            if(query == null)
                query = new StreamQuery();

            return HandleException(async () => (await GetStreamResult(room, query)).data);
        }

        [HttpGet("{room}/json")]
        public async Task<ActionResult<StreamResult>> GetJson(string room, [FromQuery]StreamQuery query = null)
        {
            return await HandleException(async () => await GetStreamResult(room, query));
        }

        [HttpGet("constants")]
        public ActionResult<Constants> Get()
        {
            return new Constants()
            {
                maxStreamSize = rooms.Config.StreamDataLimit,
                maxSingleChunk = rooms.Config.SingleDataLimit
            };
        }

        [HttpGet("saveall")]
        public async Task<ActionResult<string>> SaveAll()
        {
            //This ensures only ONE person will get through the save-all time barrier
            lock(SaveAllLock)
            {
                if(DateTime.Now - LastSaveAll < TimeSpan.FromMinutes(1))
                    return BadRequest("Cannot save that frequently!");
                else
                    LastSaveAll = DateTime.Now;
            }

            await rooms.ForceSaveAll();
            return "Saved all streams";
        }

        [HttpPost("{room}")]
        public async Task<ActionResult> Post(string room)
        {
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
            {  
                string data = await reader.ReadToEndAsync();

                if(!IsRoomAcceptable(room))
                    return BadRequest("Room name has invalid characters! Try something simpler!");

                try
                {
                    var s = rooms.GetStream(room);
                    rooms.AddData(s, data);
                }
                catch(InvalidOperationException ex)
                {
                    _logger.LogWarning($"System threw a 'handled' exception is Post: {ex}");
                    return BadRequest(ex.Message);
                }

                return Ok();
            }
        }
    }
}
