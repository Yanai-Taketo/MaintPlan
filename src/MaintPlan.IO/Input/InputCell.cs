using System.Globalization;

namespace MaintPlan.IO.Input;

/// <summary>セルの型。CSV のセルは、すべて文字のセル。</summary>
internal enum InputCellType
{
    /// <summary>文字のセル。空のセルも、空の文字のセルとして持つ。</summary>
    Text,

    /// <summary>数値のセル。</summary>
    Number,

    /// <summary>日付のセル(日付の表示の書式を付けた数値のセル)。</summary>
    Date,

    /// <summary>数式のセル。</summary>
    Formula,

    /// <summary>ほかのセル(真偽・エラー・時刻など)。</summary>
    Other,
}

/// <summary>列が、文字のセルのほかに受け付けるセル。</summary>
internal enum AcceptedCells
{
    /// <summary>文字のセルだけ(真偽・選択項目)。</summary>
    TextOnly,

    /// <summary>15桁に丸めた値が整数の数値のセル(整数・年度・金額・文字)。</summary>
    Integer,

    /// <summary>15桁に丸めた値が小数点以下1桁までの数値のセル(作業日数)。</summary>
    Tenths,

    /// <summary>時刻が0の日付のセル。</summary>
    Date,

    /// <summary>1日で、時刻が0の日付のセル。</summary>
    YearMonth,
}

/// <summary>
/// 表のセル。Number は数値のセルの値を有効数字15桁に丸めたもの、Date は日付のセルの値で、ほかの型のセルでは使わない。
/// 文字でないセルの表示される文字は、形の誤りの文を作るときにだけ作る。受け付けた値は、表示される文字によらない。
/// </summary>
internal sealed class InputCell
{
    /// <summary>文字のセルの文字。ほかのセルでは空。</summary>
    private readonly string text;

    /// <summary>文字でないセルの、Excel で表示される文字を作る。文字のセルでは null。</summary>
    private readonly Func<string>? shown;

    /// <summary>文字でないセル。shown は、Excel で表示される文字を作る。</summary>
    public InputCell(InputCellType type, Func<string> shown, RoundedNumber number = default, DateTime date = default)
        : this(type, string.Empty, shown, number, date)
    {
    }

    private InputCell(InputCellType type, string text, Func<string>? shown, RoundedNumber number, DateTime date)
    {
        Type = type;
        this.text = text;
        this.shown = shown;
        Number = number;
        Date = date;
    }

    /// <summary>空欄のセル。</summary>
    public static InputCell Blank { get; } = Text(string.Empty);

    public InputCellType Type { get; }

    public RoundedNumber Number { get; }

    public DateTime Date { get; }

    /// <summary>形の誤りの文に使う文字。文字のセルではその文字、ほかのセルでは Excel で表示される文字(数式のセルは空)。</summary>
    public string Shown => shown is null ? text : shown();

    /// <summary>空欄か。空のセルと、空の文字のセルは空欄。</summary>
    public bool IsBlank => Type == InputCellType.Text && text.Length == 0;

    public static InputCell Text(string text) => new(InputCellType.Text, text, null, default, default);

    /// <summary>
    /// 列が受け付けるセルを、CSV と同じ形の文字にする。文字のセルはその文字のまま、数値のセルは15桁に丸めた値の数字、
    /// 日付のセルは YYYY-MM-DD か YYYY-MM にする。受け付けないセルなら null。
    /// </summary>
    public string? TextFor(AcceptedCells accepted) => (Type, accepted) switch
    {
        (InputCellType.Text, _) => text,
        (InputCellType.Number, AcceptedCells.Integer) => Number.IntegerText,
        (InputCellType.Number, AcceptedCells.Tenths) => Number.TenthsText,
        (InputCellType.Date, AcceptedCells.Date) when Date.TimeOfDay == TimeSpan.Zero
            => Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        (InputCellType.Date, AcceptedCells.YearMonth) when Date.TimeOfDay == TimeSpan.Zero && Date.Day == 1
            => Date.ToString("yyyy-MM", CultureInfo.InvariantCulture),
        _ => null,
    };
}
