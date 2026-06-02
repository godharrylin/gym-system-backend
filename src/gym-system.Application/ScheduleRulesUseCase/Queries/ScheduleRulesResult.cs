using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace gym_system.Application.ScheduleRulesUseCase.Queries
{
    public class ScheduleRulesResult
    {
        public string cls_scdle_rules_sn { get; set; } = string.Empty;
        public required string class_id { get; set; }
        public int cls_scdle_rules_day_wk { get; set; }
        public TimeSpan cls_scdle_rules_st { get; set; }
        public int cls_scdle_duration { get; set; }
        public TimeSpan cls_scdle_rules_et { get; set; }
        public int cls_scdle_rules_buffer_time { get; set; }
        public string cls_scdle_instructor_id { get; set; } = string.Empty;
        public bool cls_scdle_rules_is_active { get; set; }
    }
}
