using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using MaintPlan.IO.Input;

namespace MaintPlan.IO.Csv;

/// <summary>列名と、文字列のままの値を持つ表。</summary>
public sealed record CsvTable(string FilePath, IReadOnlyList<string> Columns, IReadOnlyList<CsvTableRow> Rows)
{
    /// <summary>CSV ファイルを読む。文字コードは UTF-8(BOM の有無は問わない)。1行目を列名とし、列名が重複していれば、行を読まずに CsvFormatException を投げる。</summary>
    public static CsvTable Read(string filePath)
    {
        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            DetectColumnCountChanges = true,
            BadDataFound = args => throw new CsvFormatException(
                filePath, args.Context.Parser is { } parser ? StartLine(parser) : null, null, $"CSV の形が正しくありません: {args.RawRecord}"),
        };

        using var reader = new StreamReader(filePath, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(reader, configuration);

        if (!csv.Read() || !csv.ReadHeader() || csv.HeaderRecord is not { Length: > 0 } header)
        {
            throw new CsvFormatException(filePath, null, null, "列名の行がありません。");
        }

        if (PlanTableReader.DuplicateColumnsProblem(header) is { } duplicated)
        {
            throw new CsvFormatException(filePath, 1, null, duplicated);
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

                rows.Add(new CsvTableRow(StartLine(csv.Parser), cells));
            }
        }
        catch (BadDataException exception)
        {
            throw new CsvFormatException(filePath, exception.Context?.Parser is { } parser ? StartLine(parser) : null, null, "列の数が列名の行と合いません。");
        }

        return new CsvTable(filePath, header, rows);
    }

    /// <summary>レコードが始まる行の番号。セルの中に改行があるレコードは、複数の行にまたがる。</summary>
    private static int StartLine(IParser parser)
    {
        var record = parser.RawRecord.TrimEnd('\r', '\n');
        return parser.RawRow - record.Count(character => character == '\n');
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
