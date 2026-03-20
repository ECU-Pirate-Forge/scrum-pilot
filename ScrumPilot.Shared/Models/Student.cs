using System;
using System.Collections.Generic;
using System.Text;

namespace ScrumPilot.Shared.Models
{
    public class Student
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public int GradeLevel { get; set; }
        public bool IsFullTime { get; set; }
    }
}
