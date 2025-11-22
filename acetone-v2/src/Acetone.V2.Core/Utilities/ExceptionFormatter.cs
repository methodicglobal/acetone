using System.Collections;
using System.Text;

namespace Acetone.V2.Core.Utilities;

public static class ExceptionFormatter
{
    public static string FormatException(Exception ex)
    {
        StringBuilder error = new();
        WriteExceptionDetails(ex, error);
        return error.ToString();
    }

    private static void WriteExceptionDetails(Exception? exception, StringBuilder builderToFill, int level = 0)
    {
        if (exception == null || builderToFill == null)
        {
            return;
        }
        var indent = new string(' ', level);

        if (level > 0)
        {
            builderToFill.AppendLine(indent + "=== INNER EXCEPTION ===");
        }

        void Append(string prop)
        {
            var propInfo = exception.GetType().GetProperty(prop);
            var val = propInfo?.GetValue(exception);

            if (val != null)
            {
                builderToFill.AppendFormat("{0}{1}: {2}{3}", indent, prop, val, Environment.NewLine);
            }
        }

        Append("Message");
        Append("HResult");
        Append("HelpLink");
        Append("Source");
        Append("StackTrace");
        Append("TargetSite");

        foreach (DictionaryEntry de in exception.Data)
        {
            builderToFill.AppendFormat("{0} {1} = {2}{3}", indent, de.Key, de.Value, Environment.NewLine);
        }

        if (exception.InnerException != null)
        {
            WriteExceptionDetails(exception.InnerException, builderToFill, ++level);
        }
    }
}
