using Microsoft.AspNetCore.Mvc;  
using System.Linq;  
using System.Xml.Linq;  
  
namespace App.Controllers  
{  
    [ApiController]  
    [Route("realmdex")]  
    public class RealmDexController : ControllerBase  
    {  
        private readonly CoreService _core;  
  
        public RealmDexController(CoreService core)  
        {
            _core = core;  
        }  
        [HttpGet("stats")]  
        public void Stats() {  
            var servers = _core.GetServerList().Select(_ => _.ToXml());  
            Response.CreateText(servers.Select(i => int.Parse(i.Element("Players")!.Value)).Sum().ToString());  
        }
    }
}