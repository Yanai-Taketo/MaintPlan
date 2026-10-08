using System.Globalization;
using System.Text;
using CsvHelper;
using MaintPlan.IO.Results;

namespace MaintPlan.IO.Csv;

/// <summary>結果の表を CSV に書き出す。</summary>
public static class ResultCsvWriter
{
    /// <summary>表を1つの CSV ファイルに書く。文字コードは UTF-8(BOM付き)、1行目を列名とする。同じ名前のファイルは上書きする。</summary>
    public static void Write(string filePath, TextTable table)
    {
        using var writer = new StreamWriter(filePath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
        foreach (var column in table.Columns)
        {
            csv.WriteField(column);
        }

        csv.NextRecord();
        foreach (var row in table.Rows)
        {
            foreach (var cell in row)
            {
                csv.WriteField(cell);
            }

            csv.NextRecord();
        }
    }
}
