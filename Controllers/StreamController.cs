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
        private readonly ILogger<StreamController> _logger;

        public StreamControllerConfig Config;
        protected StreamSystem rooms;

        public StreamController(ILogger<StreamController> logger, StreamControllerConfig config, StreamSystem rooms)
        {
            _logger = logger;
            this.Config = config;
            this.rooms = rooms;
        }

        protected bool IsRoomAcceptable(string room)
        {
            return Regex.IsMatch(room, Config.AcceptableRoom);
        }

        [HttpGet("{room}")]
        public async Task<ActionResult<string>> Get(string room, [FromQuery]int start = 0, [FromQuery]int count = -1)
        {
            if(!IsRoomAcceptable(room))
                return BadRequest("Room name has invalid characters! Try something simpler!");

            try
            {
                var s = rooms.GetStream(room);
                return await rooms.GetDataWhenReady(s, start, count);
            }
            catch(InvalidOperationException ex)
            {
                _logger.LogWarning($"System threw a 'handled' exception is Get: {ex}");
                return BadRequest(ex.Message);
            }
        }

        public class Constants
        {
            public int MaxStreamSize {get;set;}
            public int MaxSingleChunk {get;set;}
        }

        [HttpGet("constants")]
        public ActionResult<Constants> Get()
        {
            return new Constants()
            {
                MaxStreamSize = rooms.Config.StreamDataLimit,
                MaxSingleChunk = rooms.Config.SingleDataLimit
            };
        }

        [HttpPost("{room}")]
        public async Task<ActionResult> Post(string room) //, [FromBody]string data)
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
