using Microsoft.AspNetCore.Mvc;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProjectController : ControllerBase
{
    private readonly IProjectService _svc;

    public ProjectController(IProjectService svc) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Project>>> GetAll()
        => Ok(await _svc.GetAllProjectsAsync());

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Project>> GetById(int id)
    {
        var project = await _svc.GetByIdAsync(id);
        return project is null ? NotFound() : Ok(project);
    }

    [HttpPost]
    public async Task<ActionResult<Project>> Create([FromBody] Project project)
    {
        var created = await _svc.CreateAsync(project);
        return CreatedAtAction(nameof(GetById), new { id = created.ProjectId }, created);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<Project>> Update(int id, [FromBody] Project project)
    {
        if (id != project.ProjectId) return BadRequest();
        var updated = await _svc.UpdateAsync(project);
        return Ok(updated);
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id)
    {
        await _svc.DeleteAsync(id);
        return NoContent();
    }
}
