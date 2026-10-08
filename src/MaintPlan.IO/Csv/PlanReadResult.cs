using MaintPlan.Core.Model;
using MaintPlan.IO.Input;

namespace MaintPlan.IO.Csv;

/// <summary>
/// CSV のフォルダを読んだ結果。形の違う値(Errors)があれば、Plan と Source は null。
/// Errors は、テーブルの順、ファイルの行の順に並ぶ。
/// </summary>
public sealed record PlanReadResult(PlanData? Plan, PlanSource? Source, IReadOnlyList<CsvFormatException> Errors);
