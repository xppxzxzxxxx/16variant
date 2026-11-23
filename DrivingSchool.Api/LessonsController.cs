using DrivingSchool.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DrivingSchool.Api;

[ApiController]
[Authorize]
[Route("api/lessons")]
[Tags("Занятия")]
public sealed class LessonsController(ILessonRepository lessons, ILogger<LessonsController> logger) : ControllerBase
{
    [HttpGet]
    public Task<List<LessonInfo>> List([FromQuery] LessonStatus? status, [FromQuery] string? q, CancellationToken ct) =>
        lessons.FindLessons(SessionTokens.User(User), status, q, ct);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<LessonInfo>> Get(int id, CancellationToken ct) =>
        await lessons.FindLesson(id, SessionTokens.User(User), ct) is { } lesson ? Ok(lesson) : NotFound();

    [HttpPost]
    [Authorize(Policy = "Staff")]
    public async Task<IActionResult> Create(LessonInput input, CancellationToken ct)
    {
        int id = await lessons.CreateLesson(input, ct);
        logger.LogInformation("Создано занятие {LessonId} пользователем {UserId}", id, SessionTokens.User(User).Id);
        return CreatedAtAction(nameof(Get), new { id }, new { id });
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "Staff")]
    public async Task<IActionResult> Update(int id, LessonInput input, CancellationToken ct) =>
        await lessons.UpdateLesson(id, input, ct) ? NoContent() : NotFound();

    [HttpPost("{id:int}/transition")]
    public async Task<IActionResult> Transition(int id, TransitionInput input, CancellationToken ct)
    {
        var actor = SessionTokens.User(User);
        if (!await lessons.Transition(id, actor, input, ct))
            return NotFound();
        logger.LogInformation("Занятие {LessonId} переведено в {LessonStatus} пользователем {UserId}",
            id, input.Status, actor.Id);
        return NoContent();
    }

    [HttpGet("statistics")]
    public async Task<SchoolStatistics> Statistics(CancellationToken ct) =>
        LessonRules.StatisticsFromMetrics(await lessons.FindMetrics(SessionTokens.User(User), ct));
}
