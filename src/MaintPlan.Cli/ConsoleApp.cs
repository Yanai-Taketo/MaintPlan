using System.Globalization;
using MaintPlan.Core.Calculation;
using MaintPlan.Core.Model;
using MaintPlan.IO.Csv;
using MaintPlan.IO.Excel;
using MaintPlan.IO.Input;
using MaintPlan.IO.Results;

namespace MaintPlan.Cli;

/// <summary>
/// 確認用コンソール。入力の CSV のフォルダかブック(.xlsx)を読んで確かめ、計算し、結果の表を出力先のフォルダに CSV で、
/// 出力先の末尾が .xlsx ならブック1冊に書き出す。
/// 読み込み時には、形の違う値をすべて示す。形の違う値がなければ、入力の決まりに合わない行と保存できない条件に当たる行をすべて示す。
/// 終了コードは、書き出せたら0、読み込み・入力の確認・計算・書き出しの誤りなら1、引数の誤りなら2。
/// 誤りがあったときと、上書きするファイルがほかで開かれているときは、表を1つも書き出さない。
/// </summary>
public static class ConsoleApp
{
    public const int Succeeded = 0;
    public const int Failed = 1;
    public const int UsageError = 2;

    /// <summary>ブックの拡張子。大文字と小文字は区別しない。</summary>
    private const string WorkbookExtension = ".xlsx";

    /// <summary>today は、集計基準日を省いたときに使う、日本時間の当日の日付。</summary>
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

        if (InputProblem(arguments.InputPath) is { } inputProblem)
        {
            error.WriteLine(inputProblem);
            return UsageError;
        }

        var readsWorkbook = !Directory.Exists(arguments.InputPath);
        var writesWorkbook = IsWorkbook(arguments.OutputPath);
        if (readsWorkbook && writesWorkbook && IsSamePath(arguments.InputPath, arguments.OutputPath))
        {
            error.WriteLine($"入力と出力先に同じブックは指定できません: {arguments.OutputPath}");
            return UsageError;
        }

        if (!writesWorkbook && File.Exists(Path.TrimEndingDirectorySeparator(arguments.OutputPath)))
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

        PlanReadResult read;
        try
        {
            read = readsWorkbook ? PlanWorkbookReader.Read(arguments.InputPath) : PlanCsvReader.Read(arguments.InputPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"読み込めません: {exception.Message}");
            return Failed;
        }

        if (read.Plan is not { } plan || read.Source is not { } source)
        {
            foreach (var readError in read.Errors)
            {
                error.WriteLine($"読み込めません: {readError.Message}");
            }

            WriteNotWritten(error, read.Errors.Count);
            return Failed;
        }

        OutputResult result;
        try
        {
            var validation = InputValidation.Validate(plan);
            if (!validation.IsValid)
            {
                foreach (var violation in validation.Violations)
                {
                    var heading = violation.Kind == InputViolationKind.CannotSave ? "保存できません" : "決まりに合いません";
                    error.WriteLine($"{heading}: {InputViolationText.Describe(violation, source)}");
                }

                WriteNotWritten(error, validation.Violations.Count);
                return Failed;
            }

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

        // ブックに書いたときはシート名を、フォルダに書いたときはファイル名を、画面に示す
        var writtenNames = writesWorkbook
            ? result.Tables.Select(table => table.Name).ToList()
            : result.Tables.Select(table => table.Name + ".csv").ToList();
        try
        {
            if (writesWorkbook)
            {
                EnsureNotInUse([arguments.OutputPath]);
                ResultWorkbookWriter.Write(arguments.OutputPath, result.Tables);
            }
            else
            {
                var filePaths = writtenNames.Select(fileName => Path.Combine(arguments.OutputPath, fileName)).ToList();
                Directory.CreateDirectory(arguments.OutputPath);
                EnsureNotInUse(filePaths);
                foreach (var (table, filePath) in result.Tables.Zip(filePaths))
                {
                    ResultCsvWriter.Write(filePath, table.Table);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"書き出せません: {exception.Message}");
            return Failed;
        }

        WriteMissingAmounts(output, result.MissingAmounts);
        output.WriteLine($"書き出し: {arguments.OutputPath}");
        foreach (var name in writtenNames)
        {
            output.WriteLine($"  {name}");
        }

        return Succeeded;
    }

    /// <summary>
    /// 入力の誤りの文。フォルダは CSV のフォルダとして、拡張子が .xlsx のファイルはブックとして読むので、誤りはない(null)。
    /// </summary>
    private static string? InputProblem(string inputPath)
    {
        if (Directory.Exists(inputPath))
        {
            return null;
        }

        if (File.Exists(inputPath))
        {
            return IsWorkbook(inputPath) ? null : $"入力には、CSV のフォルダか .xlsx のブックを指定します: {inputPath}";
        }

        return IsWorkbook(inputPath) ? $"入力のブックがありません: {inputPath}" : $"入力のフォルダがありません: {inputPath}";
    }

    /// <summary>パスの末尾が .xlsx か。</summary>
    private static bool IsWorkbook(string path) => path.EndsWith(WorkbookExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>2つのパスが同じか。フルパスにして比べ、Windows と macOS では大文字と小文字を区別しない。</summary>
    private static bool IsSamePath(string path, string other) =>
        string.Equals(
            Path.GetFullPath(path),
            Path.GetFullPath(other),
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>
    /// 上書きするファイル(CSV かブック)が、ほかで(Excel などで)開かれていないことと、書き出すファイルと同じ名前のフォルダがないことを確かめる。
    /// 当たるものがあれば IOException にする。一部のファイルだけが新しい結果になるのを防ぐため、書き始める前にすべてを確かめる。
    /// </summary>
    private static void EnsureNotInUse(IEnumerable<string> filePaths)
    {
        foreach (var filePath in filePaths.Where(Directory.Exists))
        {
            throw new IOException($"{Path.GetFileName(filePath)} と同じ名前のフォルダがあります。");
        }

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

    private static void WriteNotWritten(TextWriter error, int count) =>
        error.WriteLine($"{count}件の誤りがあるため、表を書き出していません。");

    private static void WriteCondition(TextWriter output, ConsoleArguments arguments, OutputCondition condition)
    {
        output.WriteLine($"入力: {arguments.InputPath}");
        output.WriteLine("集計の条件:");
        output.WriteLine($"  金額の種類: {LabelList(condition.AmountKind is { } kind ? [kind] : Enum.GetValues<AmountKind>())}");
        output.WriteLine($"  含める状態: {LabelList(condition.Statuses)}");
        output.WriteLine($"  費用区分: {LabelList(condition.Categories)}");
        output.WriteLine($"  集計基準日: {condition.BaseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}{(arguments.BaseDate is null ? "(日本時間の当日)" : string.Empty)}");
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
