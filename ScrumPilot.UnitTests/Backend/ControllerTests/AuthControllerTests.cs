using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using ScrumPilot.API.Controllers;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.UnitTests.Backend.ControllerTests
{
    public class AuthControllerTests
    {
        private readonly UserManager<IdentityUser> _mockUserManager;
        private readonly IConfiguration _mockConfig;
        private readonly AuthController _controller;

        // Must be 32+ chars for HMAC-SHA256
        private const string TestJwtKey = "test-secret-key-for-unit-tests-must-be-at-least-32-chars!";

        public AuthControllerTests()
        {
            _mockUserManager = Substitute.For<UserManager<IdentityUser>>(
                Substitute.For<IUserStore<IdentityUser>>(),
                null, null, null, null, null, null, null, null);

            _mockConfig = Substitute.For<IConfiguration>();
            _mockConfig["Jwt:Key"].Returns(TestJwtKey);
            _mockConfig["Jwt:Issuer"].Returns("ScrumPilot.API");
            _mockConfig["Jwt:Audience"].Returns("ScrumPilot.Web");
            _mockConfig["Jwt:ExpiresInHours"].Returns("8");

            _controller = new AuthController(_mockUserManager, _mockConfig);
        }

        [Fact]
        public async Task Login_ValidCredentials_ReturnsOkResult()
        {
            // Arrange
            var user = new IdentityUser { UserName = "Tyler", Email = "Tyler@scrumpilot.xyz", Id = "user-1" };
            _mockUserManager.FindByNameAsync("Tyler").Returns(user);
            _mockUserManager.CheckPasswordAsync(user, "Password1234!").Returns(true);
            _mockUserManager.GetRolesAsync(user).Returns(new List<string> { "Developer" });

            var request = new LoginRequest { UserName = "Tyler", Password = "Password1234!" };

            // Act
            var result = await _controller.Login(request);

            // Assert
            Assert.IsType<OkObjectResult>(result.Result);
        }

        [Fact]
        public async Task Login_ValidCredentials_ReturnsCorrectUserName()
        {
            // Arrange
            var user = new IdentityUser { UserName = "Tyler", Email = "Tyler@scrumpilot.xyz", Id = "user-1" };
            _mockUserManager.FindByNameAsync("Tyler").Returns(user);
            _mockUserManager.CheckPasswordAsync(user, "Password1234!").Returns(true);
            _mockUserManager.GetRolesAsync(user).Returns(new List<string> { "Developer" });

            var request = new LoginRequest { UserName = "Tyler", Password = "Password1234!" };

            // Act
            var result = await _controller.Login(request);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var response = Assert.IsType<LoginResponse>(okResult.Value);
            Assert.Equal("Tyler", response.UserName);
        }

        [Fact]
        public async Task Login_ValidCredentials_ReturnsNonEmptyToken()
        {
            // Arrange
            var user = new IdentityUser { UserName = "Tyler", Email = "Tyler@scrumpilot.xyz", Id = "user-1" };
            _mockUserManager.FindByNameAsync("Tyler").Returns(user);
            _mockUserManager.CheckPasswordAsync(user, "Password1234!").Returns(true);
            _mockUserManager.GetRolesAsync(user).Returns(new List<string> { "Developer" });

            var request = new LoginRequest { UserName = "Tyler", Password = "Password1234!" };

            // Act
            var result = await _controller.Login(request);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var response = Assert.IsType<LoginResponse>(okResult.Value);
            Assert.False(string.IsNullOrWhiteSpace(response.Token));
        }

        [Fact]
        public async Task Login_UnknownUsername_ReturnsUnauthorized()
        {
            // Arrange
            _mockUserManager.FindByNameAsync("nobody").Returns((IdentityUser?)null);

            var request = new LoginRequest { UserName = "nobody", Password = "anypass" };

            // Act
            var result = await _controller.Login(request);

            // Assert
            Assert.IsType<UnauthorizedObjectResult>(result.Result);
        }

        [Fact]
        public async Task Login_WrongPassword_ReturnsUnauthorized()
        {
            // Arrange
            var user = new IdentityUser { UserName = "Tyler", Email = "Tyler@scrumpilot.xyz", Id = "user-1" };
            _mockUserManager.FindByNameAsync("Tyler").Returns(user);
            _mockUserManager.CheckPasswordAsync(user, "WrongPassword!").Returns(false);

            var request = new LoginRequest { UserName = "Tyler", Password = "WrongPassword!" };

            // Act
            var result = await _controller.Login(request);

            // Assert
            Assert.IsType<UnauthorizedObjectResult>(result.Result);
        }

        [Fact]
        public async Task Login_ValidCredentials_CallsUserManagerOnce()
        {
            // Arrange
            var user = new IdentityUser { UserName = "Tyler", Email = "Tyler@scrumpilot.xyz", Id = "user-1" };
            _mockUserManager.FindByNameAsync("Tyler").Returns(user);
            _mockUserManager.CheckPasswordAsync(user, "Password1234!").Returns(true);
            _mockUserManager.GetRolesAsync(user).Returns(new List<string> { "Developer" });

            var request = new LoginRequest { UserName = "Tyler", Password = "Password1234!" };

            // Act
            await _controller.Login(request);

            // Assert
            await _mockUserManager.Received(1).FindByNameAsync("Tyler");
            await _mockUserManager.Received(1).CheckPasswordAsync(user, "Password1234!");
        }
    }
}
