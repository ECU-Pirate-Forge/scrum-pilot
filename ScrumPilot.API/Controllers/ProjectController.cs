using Microsoft.AspNetCore.Mvc;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers;

/// <summary>
/// Manages Project resources.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ProjectController : ControllerBase
{
    private readonly IProjectService _svc;

    /// <summary>Initialises a new instance of <see cref="ProjectController"/>.</summary>
    public ProjectController(IProjectService svc) => _svc = svc;

    /// <summary>Returns all projects.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Project>>> GetAll()
        => Ok(await _svc.GetAllProjectsAsync());

    /// <summary>Returns the project with the given ID, or 404 if not found.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<Project>> GetById(int id)
    {
        var project = await _svc.GetByIdAsync(id);
        return project is null ? NotFound() : Ok(project);
    }

    /// <summary>Creates a new project and returns it at its canonical URL.</summary>
    [HttpPost]
    public async Task<ActionResult<Project>> Create([FromBody] Project project)
    {
        var created = await _svc.CreateAsync(project);
        return CreatedAtAction(nameof(GetById), new { id = created.ProjectId }, created);
    }

    /// <summary>Updates an existing project. Returns 400 if the route ID does not match the body.</summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<Project>> Update(int id, [FromBody] Project project)
    {
        if (id != project.ProjectId) return BadRequest();
        var updated = await _svc.UpdateAsync(project);
        return Ok(updated);
    }

    /// <summary>Deletes the project with the given ID.</summary>
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id)
    {
        await _svc.DeleteAsync(id);
        return NoContent();
    }
}
