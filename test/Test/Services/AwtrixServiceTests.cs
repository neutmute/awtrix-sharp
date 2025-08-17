using AwtrixSharpWeb.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Test.Services
{
    public class AwtrixServiceTests
    {
        [Theory]
        [InlineData(0, 0, 4)]
        [InlineData(3, 3, 4)]
        [InlineData(4, 4, 3)]
        [InlineData(12, 12, 10)]
        [InlineData(99, 99, 96)]
        [InlineData(100, 100, 99)]
        public void Quantise(int rawProgress, int expectedQuantize, int expectedQuantizeBlink)
        {
            // Arrange
            var (quantized, quantizedBlinkOff) = AwtrixService.Quantize(rawProgress);
            // Assert
            Assert.Equal(expectedQuantize, quantized);
            Assert.Equal(expectedQuantizeBlink, quantizedBlinkOff);
        }
    }
}
