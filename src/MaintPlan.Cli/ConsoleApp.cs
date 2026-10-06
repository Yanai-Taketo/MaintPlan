using System.Globalization;
using MaintPlan.Core.Model;
using MaintPlan.IO.Csv;
using MaintPlan.IO.Results;

namespace MaintPlan.Cli;

/// <summary>
/// 確認用コンソール。入力の CSV のフォルダを読んで計算し、結果の表を出力先のフォルダに CSV で書き出す。
/// 終了コードは、書き出せたら0、読み込み・計算・書き出しの誤りなら1、引数の誤りなら2。
/// 読み込みか計算に誤りがあったときと、上書きするファイルがほかで開かれているときは、表を1つも書き出さない。
/// </summary>
public static class ConsoleApp
{
    public const int Succeeded = 0;
    public const int Failed = 1;
    public const int UsageError = 2;

    /// <summary>today は、集計基準日を省いたときに使う当日の日付。</summary>
    public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error, DateOnly today)
    {
        ConsoleArguments arguments;
        try
        {
            arguments = ConsoleArguments.Parse(args);
        }
        catch (ConsoleUsageException exception)
        {
            error.WriteLine(exception.Message);
            error.WriteLine();
            error.Write(ConsoleArguments.Usage);
            return UsageError;
        }

        if (arguments.ShowHelp)
        {
            output.Write(ConsoleArguments.Usage);
            return Succeeded;
        }

        if (!Directory.Exists(arguments.InputPath))
        {
            error.WriteLine($"入力のフォルダがありません: {arguments.InputPath}");
            return UsageError;
        }

        if (File.Exists(arguments.OutputPath))
        {
            error.WriteLine($"出力先にはフォルダを指定します。同じ名前のファイルがあります: {arguments.OutputPath}");
            return UsageError;
        }

        var condition = new OutputCondition(
            arguments.Statuses ?? Enum.GetValues<WorkStatus>().ToHashSet(),
            arguments.Categories ?? Enum.GetValues<CostCategory>().ToHashSet(),
            arguments.BaseDate ?? today,
            arguments.AmountKind);
        WriteCondition(output, arguments, condition);

        PlanData plan;
        try
        {
            plan = PlanCsvReader.ReadFolder(arguments.InputPath);
        }
        catch (Exception exception) when (exception is CsvFormatException or IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"読み込めません: {exception.Message}");
            return Failed;
        }

        OutputResult result;
        try
        {
            result = OutputTables.Build(plan, condition);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            error.WriteLine($"計算できません: {exception.Message}");
            return Failed;
        }
        catch (OverflowException)
        {
            error.WriteLine("計算できません: 金額か人工が大きすぎます。");
            return Failed;
        }

        var fileNames = result.Tables.Select(table => table.Name + ".csv").ToList();
        var filePaths = fileNames.Select(fileName => Path.Combine(arguments.OutputPath, fileName)).ToList();
        try
        {
            Directory.CreateDirectory(arguments.OutputPath);
            EnsureNotInUse(filePaths);
            foreach (var (table, filePath) in result.Tables.Zip(filePaths))
            {
                ResultCsvWriter.Write(filePath, table.Table);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"書き出せません: {exception.Message}");
            return Failed;
        }

        WriteMissingAmounts(output, result.MissingAmounts);
        output.WriteLine("読み込み時に、重複と参照先は確かめていません。参照先のない行は、知らせずに計算から外れることがあります。");
        output.WriteLine($"書き出し: {arguments.OutputPath}");
        foreach (var fileName in fileNames)
        {
            output.WriteLine($"  {fileName}");
        }

        return Succeeded;
    }

    /// <summary>
    /// 上書きするファイルが、ほかで(Excel などで)開かれていないことを確かめる。開けないファイルがあれば IOException にする。
    /// 一部のファイルだけが新しい結果になるのを防ぐため、書き始める前にすべてを確かめる。
    /// </summary>
    private static void EnsureNotInUse(IEnumerable<string> filePaths)
    {
        foreach (var filePath in filePaths.Where(File.Exists))
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException exception)
            {
                throw new IOException($"{Path.GetFileName(filePath)} を開けません。ほかで開かれていれば、閉じてからやり直してください。", exception);
            }
        }
    }

    private static void WriteCondition(TextWriter output, ConsoleArguments arguments, OutputCondition condition)
    {
        output.WriteLine($"入力: {arguments.InputPath}");
        output.WriteLine("集計の条件:");
        output.WriteLine($"  金額の種類: {LabelList(condition.AmountKind is { } kind ? [kind] : Enum.GetValues<AmountKind>())}");
        output.WriteLine($"  含める状態: {LabelList(condition.Statuses)}");
        output.WriteLine($"  費用区分: {LabelList(condition.Categories)}");
        output.WriteLine($"  集計基準日: {condition.BaseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}{(arguments.BaseDate is null ? "(当日)" : string.Empty)}");
    }

    /// <summary>未入力の件数を「予算額 1件(例12D)」の形で、金額の種類ごとに1行ずつ書く。予算額・見積額を集計しないときは書かない。</summary>
    private static void WriteMissingAmounts(TextWriter output, TextTable missingAmounts)
    {
        if (missingAmounts.Rows.Count == 0)
        {
            return;
        }

        output.WriteLine("未入力の件数:");
        foreach (var row in missingAmounts.Rows)
        {
            var targets = row[2].Length == 0 ? string.Empty : $"({row[2]})";
            output.WriteLine($"  {row[0]} {row[1]}件{targets}");
        }
    }

    /// <summary>選択項目を、呼び名の決まった順に「、」でつなぐ。</summary>
    private static string LabelList<T>(IEnumerable<T> values)
        where T : struct, Enum =>
        string.Join("、", values.Order().Select(Labels.Of));
}
