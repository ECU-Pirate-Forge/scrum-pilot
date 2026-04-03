using Bunit;
using Xunit;
using Microsoft.AspNetCore.Components.Web;

namespace ScrumPilot.UnitTests.Frontend
{
    public class SimpleBunitTest : FrontendTestBase
    {
        [Fact]
        public void SimpleBunitTest_CanCreateTestContext()
        {
            // Act & Assert
            // If we can create the test context without errors, bUnit is working
            Assert.NotNull(Services);
        }
    }
}

