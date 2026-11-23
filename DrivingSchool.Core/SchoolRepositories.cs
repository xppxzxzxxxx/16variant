namespace DrivingSchool.Core;

public interface ILessonRepository
{
    Task<List<LessonInfo>> FindLessons(UserInfo user, LessonStatus? status, string? search, CancellationToken ct);
    Task<LessonInfo?> FindLesson(int id, UserInfo user, CancellationToken ct);
    Task<List<LessonMetrics>> FindMetrics(UserInfo user, CancellationToken ct);
    Task<int> CreateLesson(LessonInput input, CancellationToken ct);
    Task<bool> UpdateLesson(int id, LessonInput input, CancellationToken ct);
    Task<bool> Transition(int id, UserInfo user, TransitionInput input, CancellationToken ct);
}

public interface IStudentRepository
{
    Task<List<StudentInfo>> GetStudents(CancellationToken ct);
    Task<int> CreateStudent(StudentInput input, CancellationToken ct);
    Task<bool> UpdateStudent(int id, StudentInput input, CancellationToken ct);
    Task<int> ImportStudents(string csv, CancellationToken ct);
}

public interface IUserRepository
{
    Task<List<UserInfo>> GetUsers(bool instructorsOnly, CancellationToken ct);
    Task<SchoolUser?> FindUser(string login, CancellationToken ct);
}
