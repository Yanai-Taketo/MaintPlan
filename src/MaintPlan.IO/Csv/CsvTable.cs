using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace MaintPlan.IO.Csv;

/// <summary>列名と、文字列のままの値を持つ表。</summary>
public sealed record CsvTable(string FilePath, IReadOnlyList<string> Columns, IReadOnlyList<CsvTableRow> Rows)
{
    /// <summary>CSV ファイルを読む。文字コードは UTF-8(BOM の有無は問わない)。1行目を列名とする。</summary>
    public static CsvTable Read(string filePath)
    {
        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            DetectColumnCountChanges = true,
            BadDataFound = args => throw new CsvFormatException(
                filePath, args.Context.Parser?.RawRow, null, $"CSV の形が正しくありません: {args.RawRecord}"),
        };

        using var reader = new StreamReader(filePath, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(reader, configuration);

        if (!csv.Read() || !csv.ReadHeader() || csv.HeaderRecord is not { Length: > 0 } header)
        {
            throw new CsvFormatException(filePath, null, null, "列名の行がありません。");
        }

        var duplicated = header.GroupBy(name => name).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
        if (duplicated.Count > 0)
        {
            throw new CsvFormatException(filePath, 1, null, $"列名が重複しています: {string.Join("、", duplicated)}");
        }

        var rows = new List<CsvTableRow>();
        try
        {
            while (csv.Read())
            {
                var cells = new string[header.Length];
                for (var index = 0; index < header.Length; index++)
                {
                    cells[index] = csv.GetField(index) ?? string.Empty;
                }

                rows.Add(new CsvTableRow(csv.Parser.RawRow, cells));
            }
        }
        catch (BadDataException exception)
        {
            throw new CsvFormatException(filePath, exception.Context?.Parser?.RawRow, null, "列の数が列名の行と合いません。");
        }

        return new CsvTable(filePath, header, rows);
    }

    /// <summary>列の位置。なければ -1。</summary>
    public int IndexOf(string column)
    {
        for (var index = 0; index < Columns.Count; index++)
        {
            if (Columns[index] == column)
            {
                return index;
            }
        }

        return -1;
    }
}

/// <summary>表の1行。LineNumber はファイルの行番号(1行目が列名)。</summary>
public sealed record CsvTableRow(int LineNumber, IReadOnlyList<string> Cells);
