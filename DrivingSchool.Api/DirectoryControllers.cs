using DrivingSchool.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DrivingSchool.Api;

[ApiController]
[Authorize(Policy = "Staff")]
[Route("api/students")]
[Tags("Ученики")]
public sealed class StudentsController(IStudentRepository students) : ControllerBase
{
    [HttpGet]
    public Task<List<StudentInfo>> List(CancellationToken ct) => students.GetStudents(ct);

    [HttpPost]
    public async Task<IActionResult> Create(StudentInput input, CancellationToken ct)
    {
        int id = await students.CreateStudent(input, ct);
        return Created("/api/students", new { id });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, StudentInput input, CancellationToken ct) =>
        await students.UpdateStudent(id, input, ct) ? NoContent() : NotFound();
}

[ApiController]
[Route("api/users")]
[Tags("Пользователи")]
public sealed class UsersController(IUserRepository users) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = Roles.Admin)]
    public Task<List<UserInfo>> List(CancellationToken ct) => users.GetUsers(false, ct);

    [HttpGet("instructors")]
    [Authorize(Policy = "Staff")]
    public Task<List<UserInfo>> Instructors(CancellationToken ct) => users.GetUsers(true, ct);
}
