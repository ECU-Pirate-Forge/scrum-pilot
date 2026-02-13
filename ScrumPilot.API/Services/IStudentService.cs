using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services
{
    public interface IStudentService
    {
        List<Student> GetStudents();
    }
}