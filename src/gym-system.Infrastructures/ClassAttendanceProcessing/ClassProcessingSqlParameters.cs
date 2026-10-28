using System.Data;
using Dapper;

namespace gym_system.Infrastructures.ClassAttendanceProcessing;

// Local to class processing: do not change Dapper's global mappings or JSON handlers.
internal static class ClassProcessingSqlParameters
{
    public static DynamicParameters Create(object? values)
    {
        var parameters = new DynamicParameters(values);
        if (values is null) return parameters;
        foreach (var property in values.GetType().GetProperties())
            if (property.GetValue(values) is DateTime value)
                parameters.Add(property.Name, value, DbType.DateTime2);
        return parameters;
    }
}
