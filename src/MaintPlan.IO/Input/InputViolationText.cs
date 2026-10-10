using MaintPlan.Core.Calculation;

namespace MaintPlan.IO.Input;

/// <summary>入力の決まりに合わない行を、ファイル(ブックから読んだときはシート)・行・列を付けた文にする。</summary>
public static class InputViolationText
{
    /// <summary>
    /// 「工事.csv 5行目 列「管理番号」: 管理番号「例1」が重複しています。最初の行は2行目です。」の形にする。
    /// ブックから読んだときは「シート「工事」 5行目 列「管理番号」: …」の形で、行は Excel の行番号。
    /// 列が2つ以上あれば「列「開始日」「終了日」」と並べる。
    /// </summary>
    public static string Describe(InputViolation violation, PlanSource source)
    {
        var table = source.Tables[violation.Table];
        var columns = string.Concat(violation.Columns.Select(column => $"「{column}」"));
        var first = violation.FirstRowIndex is { } index ? $"最初の行は{table.LineNumbers[index]}行目です。" : string.Empty;
        return $"{table.Place} {table.LineNumbers[violation.RowIndex]}行目 列{columns}: {violation.Message}{first}";
    }
}
