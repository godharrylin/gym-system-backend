using gym_system.Domain.Entities.ScheduleSessions;
using System.Globalization;
using System.Text.Json;

namespace gym_system.Application.ScheduleSessionsUseCase.Command
{
    internal static class ScheduleSessionChangeLogBuilder
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        internal static string? Build(ScheduleSessionSnapshot before, ScheduleSession after)
        {
            var changes = new Dictionary<string, ChangedValue>();

            AddIfChanged(
                changes,
                "cls_scdle_date",
                before.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                after.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            AddIfChanged(changes, "class_id", before.ClassId, after.ClassId);
            AddIfChanged(changes, "class_name", before.ClassName, after.ClassName);
            AddIfChanged(changes, "class_label_color", before.ClassLabelColor, after.ClassLabelColor);
            AddIfChanged(changes, "cls_scdle_arnge_instructor_id", before.InstructorId, after.InstructorId);
            AddIfChanged(changes, "instructor_name", before.InstructorName, after.InstructorName);

            AddIfChanged(
                changes,
                "cls_scdle_arnge_st",
                FormatDateTime(before.StartAt),
                FormatDateTime(after.StartAt));

            AddIfChanged(
                changes,
                "cls_scdle_arnge_et",
                FormatDateTime(before.EndAt),
                FormatDateTime(after.EndAt));

            AddIfChanged(changes, "cls_scdle_status", before.Status.ToString(), after.Status.ToString());
            AddIfChanged(changes, "cls_scdle_arnge_is_free", FormatBool(before.IsFree), FormatBool(after.IsFree));

            return changes.Count == 0
                ? null
                : JsonSerializer.Serialize(changes, JsonOptions);
        }

        private static void AddIfChanged(
            Dictionary<string, ChangedValue> changes,
            string columnName,
            string oldValue,
            string newValue)
        {
            if (oldValue == newValue) return;

            changes[columnName] = new ChangedValue(oldValue, newValue);
        }

        private static string FormatDateTime(DateTime value)
        {
            return value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static string FormatBool(bool value)
        {
            return value ? "true" : "false";
        }

        private sealed record ChangedValue(string Old, string New);
    }
}
