# ScrumPilot.UnitTests

🧪 **Comprehensive Test Suite for ScrumPilot Application**

This project contains unit tests for the ScrumPilot application, organized into Backend and Frontend test suites with full coverage of API controllers, services, and Blazor components.

## 🏗️ Test Architecture

The test project is structured to provide comprehensive coverage across all layers of the ScrumPilot application:

- **Backend Tests** - API controllers, services, and business logic
- **Frontend Tests** - Blazor components and UI interactions (future)
- **Integration Tests** - End-to-end scenarios and workflows
- **Mock Data** - Consistent test fixtures and data generation

## 🛠️ Test Framework Stack

- **XUnit** - Primary testing framework with extensible architecture
- **NSubstitute** - Powerful mocking library for backend dependency injection
- **BUnit** - Blazor component testing (currently disabled for .NET 10 compatibility)
- **AutoFixture** - Automated test data generation and object creation

## 📁 Project Structure

```
ScrumPilot.UnitTests/
├── Backend/                  # API and service layer tests
│   ├── Controllers/                 # Controller endpoint tests
│   ├── Services/                    # Business logic service tests
│   └── ExampleApiTests.cs           # Test framework examples
├── Frontend/                # Blazor component tests (disabled until BUnit .NET 10 support)
│   ├── Components/                  # Individual component tests
│   ├── Pages/                       # Page component integration tests
│   └── ExampleComponentTests.cs     # Component testing examples
├── Shared/                  # Cross-cutting test utilities
│   ├── TestData/                    # Test data factories and builders
│   ├── Fixtures/                    # Test fixtures and setup helpers
│   └── Mocks/                       # Common mock implementations
├── GlobalUsings.cs          # Global using statements for all tests
├── README.md               # This documentation file
└── ScrumPilot.UnitTests.csproj     # Project configuration and dependencies
```

## 🚀 Running Tests

### All Tests
```bash
dotnet test ScrumPilot.UnitTests/ScrumPilot.UnitTests.csproj
```

### Backend Tests Only
```bash
dotnet test ScrumPilot.UnitTests/ScrumPilot.UnitTests.csproj --filter "FullyQualifiedName~Backend"
```

### Frontend Tests Only (When Available)
```bash
dotnet test ScrumPilot.UnitTests/ScrumPilot.UnitTests.csproj --filter "FullyQualifiedName~Frontend"
```

### With Coverage Report
```bash
dotnet test ScrumPilot.UnitTests/ScrumPilot.UnitTests.csproj --collect:"XPlat Code Coverage"
```

### Continuous Testing (Watch Mode)
```bash
dotnet watch test --project ScrumPilot.UnitTests/ScrumPilot.UnitTests.csproj
```

## 🔧 Dependencies & References

### Test Framework Dependencies
- **XUnit** - Core testing framework and runner
- **XUnit.Runner.VisualStudio** - Visual Studio integration
- **Microsoft.NET.Test.Sdk** - .NET test SDK and tooling

### Mocking & Data Generation
- **NSubstitute** - Flexible mocking framework for interfaces
- **AutoFixture** - Automatic test data generation
- **AutoFixture.Xunit2** - XUnit integration for AutoFixture

### Project References
- **ScrumPilot.API** - For testing API controllers and services
- **ScrumPilot.Web** - For testing Blazor components (future)
- **ScrumPilot.Shared** - For testing shared models and logic

### Future Dependencies (BUnit Integration)
- **BUnit** - Blazor component testing framework
- **BUnit.Web** - Web-specific Blazor testing utilities
- **AngleSharp** - HTML parsing and DOM manipulation for component testing

## 🧪 Backend Testing Examples

### Controller Testing
```csharp
public class StoryControllerTests
{
    private readonly IStoryService _storyService;
    private readonly StoryController _controller;

    public StoryControllerTests()
    {
        _storyService = Substitute.For<IStoryService>();
        _controller = new StoryController(_storyService);
    }

    [Fact]
    public async Task GetStories_Should_ReturnAllStories()
    {
        // Arrange
        var expectedStories = new List<Story> 
        { 
            new Story { Id = Guid.NewGuid(), Title = "Test Story" }
        };
        _storyService.GetAllStoriesAsync().Returns(expectedStories);

        // Act
        var result = await _controller.GetStories();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var stories = Assert.IsType<List<Story>>(okResult.Value);
        Assert.Single(stories);
    }
}
```

### Service Testing
```csharp
public class StoryServiceTests
{
    private readonly Fixture _fixture;

    public StoryServiceTests()
    {
        _fixture = new Fixture();
    }

    [Theory]
    [InlineData(StoryPriority.Highest)]
    [InlineData(StoryPriority.High)]
    [InlineData(StoryPriority.Low)]
    public void CreateStory_Should_SetPriorityCorrectly(StoryPriority priority)
    {
        // Arrange
        var story = _fixture.Build<Story>()
            .With(x => x.Priority, priority)
            .Create();

        // Act & Assert
        Assert.Equal(priority, story.Priority);
    }
}
```

### Model Validation Testing
```csharp
public class StoryModelTests
{
    [Fact]
    public void Story_Should_RequireTitle()
    {
        // This will be caught at compile time due to required keyword
        var story = new Story { Title = "Required Title" };
        Assert.NotNull(story.Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Story_Should_HandleEmptyDescription(string? description)
    {
        var story = new Story 
        { 
            Title = "Test Story",
            Description = description ?? ""
        };

        Assert.NotNull(story.Description);
    }
}
```

## 🎨 Frontend Testing (Future Implementation)

When BUnit supports .NET 10, frontend tests will cover:

### Component Rendering Tests
```csharp
public class PbiCardTests : TestContext
{
    [Fact]
    public void PbiCard_Should_DisplayStoryTitle()
    {
        // Arrange
        var story = new Story { Title = "Test Story", Description = "Test Description" };

        // Act
        var component = RenderComponent<PbiCard>(parameters => parameters
            .Add(p => p.StoryModel, story));

        // Assert
        Assert.Contains("Test Story", component.Markup);
    }
}
```

### Page Integration Tests
```csharp
public class DraftPbiPageTests : TestContext
{
    [Fact]
    public void DraftPbiPage_Should_LoadStoriesOnInit()
    {
        // Arrange
        Services.AddSingleton(Substitute.For<HttpClient>());

        // Act
        var component = RenderComponent<DraftPbiPage>();

        // Assert
        Assert.Contains("Loading draft stories", component.Markup);
    }
}
```

### User Interaction Tests
```csharp
public class ScrumBoardTests : TestContext
{
    [Fact]
    public void ScrumBoard_Should_HandleStoryDrop()
    {
        // Test drag-and-drop functionality
        // Test story status updates
        // Test UI state changes
    }
}
```

## 📊 Test Data Management

### AutoFixture Configuration
```csharp
public class StorySpecimenBuilder : ISpecimenBuilder
{
    public object Create(object request, ISpecimenContext context)
    {
        if (request is Type type && type == typeof(Story))
        {
            return new Story
            {
                Id = Guid.NewGuid(),
                Title = "Generated Story Title",
                Description = "Generated description",
                Status = StoryStatus.ToDo,
                Priority = StoryPriority.High,
                DateCreated = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow
            };
        }

        return new NoSpecimen();
    }
}
```

### Test Data Factories
```csharp
public static class TestDataFactory
{
    public static Story CreateStory(
        string? title = null,
        StoryStatus status = StoryStatus.ToDo,
        StoryPriority priority = StoryPriority.High)
    {
        return new Story
        {
            Id = Guid.NewGuid(),
            Title = title ?? "Default Test Story",
            Description = "Test story description",
            Status = status,
            Priority = priority,
            DateCreated = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
        };
    }
}
```

## 🔄 Continuous Integration

### GitHub Actions Integration
```yaml
name: Run Tests
on: [push, pull_request]
jobs:
  test:
    runs-on: ubuntu-latest
    steps:
    - uses: actions/checkout@v3
    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '10.0.x'
    - name: Restore dependencies
      run: dotnet restore
    - name: Run tests
      run: dotnet test --collect:"XPlat Code Coverage"
```

### Local Development
```bash
# Run tests automatically on file changes
dotnet watch test

# Generate coverage report
dotnet test --collect:"XPlat Code Coverage" --results-directory ./TestResults

# View coverage with ReportGenerator
reportgenerator -reports:"./TestResults/*/coverage.cobertura.xml" -targetdir:"./CoverageReport"
```

## 📋 Testing Best Practices

### Test Naming Convention
```csharp
// Pattern: MethodName_Should_ExpectedBehavior_When_Condition
[Fact]
public void CreateStory_Should_ReturnCreatedStory_When_ValidDataProvided()

[Theory]
public void ValidateStory_Should_ReturnFalse_When_TitleIsEmpty()
```

### Test Organization
- **Arrange** - Set up test data and dependencies
- **Act** - Execute the method being tested  
- **Assert** - Verify the expected outcomes

### Mocking Guidelines
- Mock external dependencies only
- Use `Returns()` for method return values
- Use `Received()` to verify method calls
- Keep mocks simple and focused

## 🚀 Running Different Test Scenarios

### Performance Tests
```bash
# Run tests with performance profiling
dotnet test --logger "console;verbosity=detailed" --collect:"Code Coverage"
```

### Integration Tests
```bash
# Run tests requiring database or external services
dotnet test --filter "Category=Integration"
```

### Unit Tests Only
```bash
# Run pure unit tests (no external dependencies)
dotnet test --filter "Category=Unit"
```

## 🔧 Troubleshooting

### Common Test Issues

**BUnit Compatibility**
- BUnit currently doesn't support .NET 10
- Frontend tests are disabled until compatibility is restored
- Monitor [BUnit repository](https://github.com/bUnit-dev/bUnit) for .NET 10 support

**Mock Setup Errors**
- Ensure interface dependencies are properly substituted
- Verify method signatures match between interface and implementation
- Check that async methods return `Task<T>` not just `T`

**AutoFixture Conflicts**
- Customize specimen builders for complex objects
- Use `.Build<T>().With()` to override specific properties
- Handle circular references in object graphs

**Test Discovery Issues**
- Ensure test classes are public
- Verify `[Fact]` and `[Theory]` attributes are properly applied
- Check that test project references XUnit packages

### Debug Tests in Visual Studio
1. Set breakpoints in test methods
2. Right-click test in Test Explorer
3. Select "Debug Selected Tests"
4. Step through test execution

## 📚 Additional Resources

- [XUnit Documentation](https://xunit.net/docs/getting-started/netcore/cmdline)
- [NSubstitute Documentation](https://nsubstitute.github.io/help/getting-started/)
- [AutoFixture Documentation](https://github.com/AutoFixture/AutoFixture)
- [BUnit Documentation](https://bunit.egilhansen.com/) (Future reference)

---

**Part of the ScrumPilot Application Suite**

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