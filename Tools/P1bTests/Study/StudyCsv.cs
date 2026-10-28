using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

internal static partial class P1bTests
{
    private static string StudyCsvRow(string name, params object[] values)
    {
        var sb = new StringBuilder(name);
        foreach (object value in values)
        {
            sb.Append(',');
            if (value is double d)
            {
                sb.Append(d.ToString("E17", CultureInfo.InvariantCulture));
            }
            else if (value is long l)
            {
                sb.Append(l.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
            }
        }

        return sb.ToString();
    }

    private static void StudyWriteCsv(string fileName, string header, List<string> rows)
    {
        string dir = Path.Combine("Study", "taylor", "results");
        Directory.CreateDirectory(dir);
        var lines = new List<string>(rows.Count + 1) { header };
        lines.AddRange(rows);
        File.WriteAllLines(Path.Combine(dir, fileName), lines, new UTF8Encoding(false));
    }
}
