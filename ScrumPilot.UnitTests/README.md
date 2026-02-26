# ScrumPilot Unit Tests

This project contains unit tests for the ScrumPilot application, organized into Backend and Frontend test suites.

## ?? Test Framework Stack

- **XUnit** - Primary testing framework
- **NSubstitute** - Mocking library for backend tests  
- **BUnit** - Blazor component testing (currently disabled for .NET 10 compatibility)

## ?? Project Structure

```
ScrumPilot.UnitTests/
??? Backend/          # API and service layer tests
??? Frontend/         # Blazor component tests (disabled until BUnit .NET 10 support)
??? GlobalUsings.cs   # Common using statements
??? README.md         # This file
```

## ?? Running Tests

### All Tests
```bash
dotnet test ScrumPilot.UnitTests/ScrumPilot.UnitTests.csproj
```

### Backend Tests Only
```bash
dotnet test ScrumPilot.UnitTests/ScrumPilot.UnitTests.csproj --filter "FullyQualifiedName~Backend"
```

### Frontend Tests Only
```bash
dotnet test ScrumPilot.UnitTests/ScrumPilot.UnitTests.csproj --filter "FullyQualifiedName~Frontend"
```

### With Coverage
```bash
dotnet test ScrumPilot.UnitTests/ScrumPilot.UnitTests.csproj --collect:"XPlat Code Coverage"
```

## ?? Dependencies

- **ScrumPilot.API** - For testing API controllers and services
- **ScrumPilot.Web** - For testing Blazor components
- **ScrumPilot.Shared** - For testing shared models and logic

## ?? Backend Testing Examples

The `Backend/ExampleApiTests.cs` demonstrates:
- Unit testing with XUnit
- Mocking dependencies with NSubstitute
- Testing shared models
- Theory-based parameterized tests

## ?? Frontend Testing (Coming Soon)

Frontend tests are prepared but currently disabled due to BUnit's .NET 10 compatibility. 

**When BUnit supports .NET 10:**
1. Uncomment tests in `Frontend/ExampleComponentTests.cs`
2. Tests will cover:
   - Component rendering
   - User interactions
   - Service integrations
   - MudBlazor component testing

## ?? Writing New Tests

### Backend Tests
Create new test classes in the `Backend/` folder:
```csharp
public class YourServiceTests
{
    [Fact]
    public void YourMethod_Should_ReturnExpectedResult()
    {
        // Arrange
        var mockDependency = Substitute.For<IDependency>();
        var service = new YourService(mockDependency);
        
        // Act
        var result = service.YourMethod();
        
        // Assert
        Assert.NotNull(result);
    }
}
```

### Frontend Tests (Future)
When BUnit is compatible, create tests in the `Frontend/` folder:
```csharp
public class YourComponentTests : TestContext
{
    [Fact]
    public void YourComponent_Should_Render()
    {
        var component = RenderComponent<YourComponent>();
        Assert.NotNull(component);
    }
}
```

## ?? Known Issues

- **BUnit .NET 10 Compatibility**: Frontend component tests are disabled until BUnit releases .NET 10 compatible versions
- **Security Warning**: Package 'Microsoft.Extensions.Caching.Memory' has a known vulnerability - this will be resolved when updated packages are available

## ?? Updates

Check for BUnit .NET 10 compatibility updates at: https://github.com/bUnit-dev/bUnit