using AwtrixSharpWeb.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Test.Domain
{
    public class AwtrixAppMessageTest
    {
        [Fact]
        public void ToJsonTextSimple()
        {
            var message = new AwtrixAppMessage()
                .SetText("Hello World");

            var json = message.ToJson();

            Assert.Equal(
                "{\"text\":\"Hello World\"}",
                json);
        }

        [Fact]
        public void ToJsonTextJson()
        {
            var message = new AwtrixAppMessage()
                .SetText("[\r\n    {\r\n      \"t\": \"Hello, \",\r\n      \"c\": \"FF0000\"\r\n    },\r\n    {\r\n      \"t\": \"world!\",\r\n      \"c\": \"00FF00\"\r\n    }\r\n  ]");

            var json = message.ToJson();
            Console.WriteLine(json);
            Assert.Equal(
                "{\"text\":[{\"t\":\"Hello, \",\"c\":\"FF0000\"},{\"t\":\"world!\",\"c\":\"00FF00\"}]}",
                json);
        }
    }
}
