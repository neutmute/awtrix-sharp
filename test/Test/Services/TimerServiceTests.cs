using AwtrixSharpWeb.HostedServices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Test.Services
{
    public class TimerServiceTests
    {
        [Theory]
        [InlineData(true, "13:14")]
        [InlineData(false, "1:14")]
        public void FormatClockString(bool use24H, string expectedString)
        {
            
            var time = new DateTime(2025, 1, 1, 13, 14, 14); // 13:14:14
            
            var result = TimerService.FormatClockString(time, use24H);

            Assert.Equal(expectedString, result);
        }   
    }
}
