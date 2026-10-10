using MaintPlan.Core.Model;

namespace MaintPlan.IO.Input;

/// <summary>読み込んだ行の、ファイルでの場所。テーブルごとに持つ。</summary>
public sealed record PlanSource(IReadOnlyDictionary<PlanTable, TableSource> Tables);

/// <summary>
/// テーブルを読んだファイルと、文で示す場所(「工事.csv」か「シート「工事」」)と、PlanData の一覧の順に並べた、各行の行番号(1行目が列名)。
/// ブックから読んだ行の行番号は、Excel の行番号。
/// </summary>
public sealed record TableSource(string FilePath, string Place, IReadOnlyList<int> LineNumbers);
