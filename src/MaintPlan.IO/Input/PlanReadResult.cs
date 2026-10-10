using MaintPlan.Core.Model;

namespace MaintPlan.IO.Input;

/// <summary>
/// CSV のフォルダかブックを読んだ結果。形の違う値(Errors)があれば、Plan と Source は null。
/// Errors は、テーブルの順、行の順(行の中は列の順)に並ぶ。
/// </summary>
public sealed record PlanReadResult(PlanData? Plan, PlanSource? Source, IReadOnlyList<InputFormatException> Errors);
