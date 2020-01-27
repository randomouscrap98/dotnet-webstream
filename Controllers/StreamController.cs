using System;
using System.Collections.Generic;
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
        public async Task<ActionResult<string>> Get(string room)
        {
            if(!IsRoomAcceptable(room))
                return BadRequest("Room name has invalid characters! Try something simpler!");

            return "wow, room is: " + room;
        }

        [HttpPost("{room}")]
        public ActionResult Post(string room, [FromBody]string data)
        {
            return Ok(); //NotFound();
        }
    }
}
