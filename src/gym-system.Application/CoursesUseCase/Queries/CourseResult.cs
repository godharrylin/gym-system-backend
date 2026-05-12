namespace gym_system.Application.CoursesUseCase.Queries
{
    public sealed class CourseResult
    {
        public int class_sn { get; set; }
        public string class_name { get; set; } = string.Empty;
        /// <summary>
        /// 老師的名字
        /// </summary>
        public string usr_name { get; set; } = string.Empty;
        public string class_label_color { get; set; } = string.Empty;
        public int class_duration { get; set; }
        public bool class_is_free { get; set; }
        public bool class_is_active { get; set; }
        public string class_type { get; set; } = string.Empty;
    }
}
