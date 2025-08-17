using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Attributes;
using System.ComponentModel;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace AwtrixSharpWeb.Controllers
{
    [DisplayName("Apps")]
    [Route("api/app/[controller]")]
    [ApiController]
    public class TripTimerController : ControllerBase
    {

        /// <summary>
        /// Start now
        /// </summary>
        [HttpPost]
        public void Post()
        {
        }
    }
}
