using System;
using System.Collections.Generic;
using System.Text;

namespace gym_system.Application.InstructorUseCase.Queries
{
    public class InstrucotrResult
    {
        public required string usr_id { get; set; }
        public required string usr_name { get; set; }
        public required string usr_phone { get; set; }
        public bool user_role_is_active { get; set; }
    }
}
