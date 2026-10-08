using System.Globalization;
using MaintPlan.Core.Model;

namespace MaintPlan.Cli;

/// <summary>引数の誤り。メッセージは利用者に見せる文。</summary>
public sealed class ConsoleUsageException(string message) : Exception(message);

/// <summary>
/// 確認用コンソールの引数。集計の条件は、省いたものを null とする。
/// ShowHelp が true のときは、ほかの項目を読まない。
/// </summary>
public sealed record ConsoleArguments(
    string InputPath,
    string OutputPath,
    IReadOnlySet<WorkStatus>? Statuses,
    IReadOnlySet<CostCategory>? Categories,
    DateOnly? BaseDate,
    AmountKind? AmountKind,
    bool ShowHelp = false)
{
    public const string Input = "--input";
    public const string Output = "--output";
    public const string AmountKindOption = "--amount-kind";
    public const string StatusesOption = "--statuses";
    public const string CategoriesOption = "--categories";
    public const string BaseDateOption = "--base-date";

    private static readonly string[] Options = [Input, Output, AmountKindOption, StatusesOption, CategoriesOption, BaseDateOption];
    private static readonly string[] HelpOptions = ["--help", "-h"];
    private static readonly char[] ListSeparators = ['、', ','];

    public static string Usage { get; } = string.Join(
        Environment.NewLine,
        "使い方: MaintPlan.Cli --input <入力のフォルダ> --output <出力先のフォルダ> [集計の条件]",
        "",
        "  --input        9つのテーブルの CSV を置いたフォルダ。決まりに合わない行があれば、行を示して書き出さない",
        "  --output       結果の表の CSV を書き出すフォルダ。なければ作り、同じ名前のファイルは上書きする",
        "",
        "集計の条件(省いたときの値)",
        "  --amount-kind  山積みの金額の種類。予算額・見積額・実績額のどれか1つ(3つとも)",
        "  --statuses     山積みに含める状態。「、」か「,」で区切る(すべて)",
        "  --categories   山積みに含める費用区分。「、」か「,」で区切る(修繕費と設備投資の両方)",
        "  --base-date    残予算と見込み残の集計基準日。YYYY-MM-DD(当日)",
        "",
        "  --help         この使い方を出す",
        "");

    /// <summary>引数を読む。誤りがあれば ConsoleUsageException にする。</summary>
    public static ConsoleArguments Parse(IReadOnlyList<string> args)
    {
        if (args.Any(HelpOptions.Contains))
        {
            return new ConsoleArguments(string.Empty, string.Empty, null, null, null, null, ShowHelp: true);
        }

        var values = new Dictionary<string, string>();
        for (var index = 0; index < args.Count; index++)
        {
            var option = args[index];
            if (!Options.Contains(option))
            {
                throw new ConsoleUsageException($"「{option}」は知らない引数です。");
            }

            if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]) || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ConsoleUsageException($"「{option}」の値がありません。");
            }

            if (!values.TryAdd(option, args[++index]))
            {
                throw new ConsoleUsageException($"「{option}」が2回あります。");
            }
        }

        return new ConsoleArguments(
            Require(values, Input),
            Require(values, Output),
            values.TryGetValue(StatusesOption, out var statuses) ? ParseList<WorkStatus>(StatusesOption, statuses) : null,
            values.TryGetValue(CategoriesOption, out var categories) ? ParseList<CostCategory>(CategoriesOption, categories) : null,
            values.TryGetValue(BaseDateOption, out var baseDate) ? ParseDate(baseDate) : null,
            values.TryGetValue(AmountKindOption, out var amountKind) ? ParseLabel<AmountKind>(AmountKindOption, amountKind) : null);
    }

    private static string Require(Dictionary<string, string> values, string option) =>
        values.TryGetValue(option, out var value) ? value : throw new ConsoleUsageException($"「{option}」がありません。");

    private static HashSet<T> ParseList<T>(string option, string text)
        where T : struct, Enum
    {
        var items = text.Split(ListSeparators).Select(item => item.Trim()).ToList();
        if (items.Any(item => item.Length == 0))
        {
            throw new ConsoleUsageException($"「{option}」の「{text}」に空の項目があります。");
        }

        return [.. items.Select(item => ParseLabel<T>(option, item))];
    }

    private static T ParseLabel<T>(string option, string label)
        where T : struct, Enum =>
        Labels.TryParse<T>(label, out var value)
            ? value
            : throw new ConsoleUsageException($"「{option}」の「{label}」は{string.Join("・", Labels.All<T>())}のどれでもありません。");

    private static DateOnly ParseDate(string text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new ConsoleUsageException($"「{BaseDateOption}」の「{text}」は YYYY-MM-DD の日付ではありません。");
}
