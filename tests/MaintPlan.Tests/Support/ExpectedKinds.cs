using System.Globalization;
using System.Text.RegularExpressions;
using MaintPlan.Core.Calculation;
using MaintPlan.Core.Model;

namespace MaintPlan.Tests.Support;

/// <summary>期待値の列の値の形。比べるときは Normalize した値で比べる。</summary>
public sealed record ColumnType(string Description, Func<string, bool> IsValid, Func<string, string> Normalize, Func<string, bool> IsZero)
{
    private static readonly Regex IntegerPattern = new(@"^-?[0-9]+$");

    private static readonly Regex TenthsPattern = new(@"^[0-9]+\.[0-9]$");

    /// <summary>決まった形の文字列。空欄を0とみなす。</summary>
    public static ColumnType Text(string description, string pattern)
    {
        var regex = new Regex(pattern);
        return new(description, regex.IsMatch, text => text, text => text.Length == 0);
    }

    public static ColumnType OneOf(IEnumerable<string> labels)
    {
        var set = labels.ToHashSet();
        return new(string.Join("・", set) + "のどれか", set.Contains, text => text, text => text.Length == 0);
    }

    /// <summary>桁区切りのない整数。tokens に挙げた文字(「未入力」など)も書ける。</summary>
    public static ColumnType Integer(params string[] tokens)
    {
        var description = tokens.Length == 0 ? "桁区切りのない整数" : $"桁区切りのない整数か{string.Join("・", tokens.Select(token => $"「{token}」"))}";
        return new(
            description,
            text => tokens.Contains(text) || IntegerPattern.IsMatch(text),
            text => IntegerPattern.IsMatch(text) ? long.Parse(text, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) : text,
            text => text.Length == 0 || (IntegerPattern.IsMatch(text) && long.Parse(text, CultureInfo.InvariantCulture) == 0));
    }

    /// <summary>小数点以下1桁の数(人工)。</summary>
    public static ColumnType Tenths { get; } = new(
        "小数点以下1桁の数",
        TenthsPattern.IsMatch,
        text => TenthsPattern.IsMatch(text) ? decimal.Parse(text, CultureInfo.InvariantCulture).ToString("0.0", CultureInfo.InvariantCulture) : text,
        text => text.Length == 0 || (TenthsPattern.IsMatch(text) && decimal.Parse(text, CultureInfo.InvariantCulture) == 0));

    /// <summary>どんな文字でもよい列。</summary>
    public static ColumnType AnyText { get; } = new("文字", text => text.Length > 0, text => text, text => text.Length == 0);
}

/// <summary>キーの列。Required の列は必ず置く。</summary>
public sealed record KeyColumn(string Name, ColumnType Type, bool Required = false);

/// <summary>期待値ファイルの種類。ファイル名(「_一部」を除く)が種類の名前になる。</summary>
public sealed record ExpectedKind(
    string Name,
    IReadOnlyList<KeyColumn> Keys,
    IReadOnlyDictionary<string, ColumnType> Values,
    IReadOnlyList<string>? ExclusiveKeys = null,
    IReadOnlyList<string>? IgnoredColumns = null)
{
    public bool IsKnownColumn(string column) =>
        Keys.Any(key => key.Name == column) || Values.ContainsKey(column) || (IgnoredColumns?.Contains(column) ?? false);

    public ColumnType TypeOf(string column) =>
        Keys.FirstOrDefault(key => key.Name == column)?.Type ?? (Values.TryGetValue(column, out var type) ? type : ColumnType.AnyText);
}

public static class ExpectedKinds
{
    private const string FiscalYearLabel = @"[0-9]{4}年度";

    private static readonly ColumnType Example = ColumnType.Text("「例1」「例12A」などの例の名前", @"^例[0-9]+[A-Z]?$");
    private static readonly ColumnType CostItem = ColumnType.OneOf(Labels.All<CostCategory>());
    private static readonly ColumnType Slot = ColumnType.Text("YYYY-MM、「YYYY年度 時期未定」、「年度のない時期未定」のどれか", $@"^([0-9]{{4}}-(0[1-9]|1[0-2])|{FiscalYearLabel} 時期未定|年度のない時期未定)$");
    private static readonly ColumnType Month = ColumnType.Text("YYYY-MM", @"^[0-9]{4}-(0[1-9]|1[0-2])$");
    private static readonly ColumnType FiscalYearOrTotal = ColumnType.Text("「YYYY年度」か「合計」", $@"^({FiscalYearLabel}|合計)$");
    private static readonly ColumnType FiscalYear = ColumnType.Text("「YYYY年度」", $@"^{FiscalYearLabel}$");
    private static readonly ColumnType Staff = ColumnType.Text("「直営」「協力会社」か「直営・機械」などの種別・区分名", @"^(直営|協力会社)(・.+)?$");
    private static readonly ColumnType LaborLine = ColumnType.Text("作業明細のID", @"^[0-9]+$");
    private static readonly ColumnType AggregationColumn = ColumnType.Text("「YYYY年度 1Q」〜「YYYY年度 4Q」、「YYYY年度 時期未定」、「YYYY年度 合計」、「年度のない時期未定」のどれか", $@"^({FiscalYearLabel} ([1-4]Q|時期未定|合計)|年度のない時期未定)$");
    private static readonly ColumnType FiscalYearCategory = ColumnType.Text("「YYYY年度 修繕費」などの年度と費用区分", $@"^{FiscalYearLabel} (修繕費|設備投資)$");
    private static readonly ColumnType AmountKindType = ColumnType.OneOf(Labels.All<AmountKind>());
    private static readonly ColumnType Amount = ColumnType.Integer();
    private static readonly ColumnType AmountOrMissing = ColumnType.Integer("未入力");
    private static readonly ColumnType Count = ColumnType.Text("0以上の整数", @"^[0-9]+$");

    public static IReadOnlyList<ExpectedKind> All { get; } =
    [
        new("月ごとの日数",
            [new("年月", ColumnType.Text("YYYY-MM か「合計」", @"^([0-9]{4}-(0[1-9]|1[0-2])|合計)$"), Required: true)],
            new Dictionary<string, ColumnType> { ["日数"] = Count }),
        new("月ごとの値",
            [new("例", Example), new("費用内訳", CostItem), new("年月", Slot, Required: true)],
            new Dictionary<string, ColumnType> { ["予算額"] = AmountOrMissing, ["見積額"] = AmountOrMissing, ["実績額"] = Amount, ["人工"] = ColumnType.Tenths }),
        new("人工の月ごとの内訳",
            [new("例", Example), new("費用内訳", CostItem), new("人員区分", Staff), new("行", LaborLine), new("年月", Slot, Required: true)],
            new Dictionary<string, ColumnType> { ["人工"] = ColumnType.Tenths },
            ExclusiveKeys: ["人員区分", "行"]),
        new("計算に使わない修正",
            [new("例", Example), new("費用内訳", CostItem, Required: true), new("金額の種類", ColumnType.OneOf(["予算額", "見積額"]), Required: true), new("年月", Month, Required: true)],
            new Dictionary<string, ColumnType> { ["金額"] = Amount, ["理由"] = ColumnType.OneOf(Labels.All<UnusedOverrideReason>()) }),
        new("予算額の初期値",
            [new("例", Example), new("費用内訳", CostItem)],
            new Dictionary<string, ColumnType>
            {
                ["労務費"] = Amount,
                ["予算額の初期値"] = ColumnType.Integer("出さない"),
                ["年割の初期値"] = ColumnType.Text("「2026年度 1603076、2027年度 1731324」の形", $@"^{FiscalYearLabel} -?[0-9]+(、{FiscalYearLabel} -?[0-9]+)*$"),
                ["知らせる内容"] = ColumnType.AnyText,
            }),
        new("労務費の内訳",
            [new("例", Example), new("費用内訳", CostItem), new("人員区分", Staff), new("行", LaborLine), new("年度", FiscalYearOrTotal, Required: true)],
            new Dictionary<string, ColumnType> { ["人工"] = ColumnType.Tenths, ["単価"] = Amount, ["労務費"] = Amount },
            ExclusiveKeys: ["人員区分", "行"]),
        new("山積み",
            [new("欄", AggregationColumn, Required: true)],
            new Dictionary<string, ColumnType> { ["予算額"] = Amount, ["見積額"] = Amount, ["実績額"] = Amount, ["人工"] = ColumnType.Tenths }),
        new("人工の内訳",
            [new("欄", AggregationColumn, Required: true), new("人員区分", Staff, Required: true)],
            new Dictionary<string, ColumnType> { ["人工"] = ColumnType.Tenths }),
        new("未入力の件数",
            [new("金額の種類", ColumnType.OneOf(["予算額", "見積額"]), Required: true)],
            new Dictionary<string, ColumnType>
            {
                ["件数"] = Count,
                ["対象"] = ColumnType.Text("「例3」「例12D」などを「、」でつないだもの", @"^例[0-9]+[A-Z]?(、例[0-9]+[A-Z]?)*$"),
            }),
        new("残予算と見込み残",
            [new("年度・費用区分", FiscalYearCategory, Required: true)],
            new Dictionary<string, ColumnType>
            {
                ["予算枠"] = ColumnType.Integer("0(未登録)"),
                ["実績額"] = Amount,
                ["残予算"] = Amount,
                ["未実績見込み"] = Amount,
                ["見込み残"] = Amount,
            }),
        new("未実績見込みの内訳",
            [new("例", Example), new("費用内訳", CostItem), new("年度", FiscalYear, Required: true)],
            new Dictionary<string, ColumnType> { ["年度配分"] = Amount, ["未実績見込み"] = Amount }),
        new("保存の確認",
            [],
            new Dictionary<string, ColumnType>
            {
                ["保存できる"] = ColumnType.OneOf(["はい", "いいえ"]),
                ["理由の種類"] = ColumnType.OneOf(Labels.All<SaveViolationKind>()),
            },
            IgnoredColumns: ["結果"]),
    ];

    public static ExpectedKind? Find(string name) => All.FirstOrDefault(kind => kind.Name == name);
}
