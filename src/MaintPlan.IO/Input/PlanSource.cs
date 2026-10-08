using MaintPlan.Core.Model;

namespace MaintPlan.IO.Input;

/// <summary>読み込んだ行の、ファイルでの場所。テーブルごとに持つ。</summary>
public sealed record PlanSource(IReadOnlyDictionary<PlanTable, TableSource> Tables);

/// <summary>テーブルを読んだファイルと、PlanData の一覧の順に並べた、各行のファイルの行番号(1行目が列名)。</summary>
public sealed record TableSource(string FilePath, IReadOnlyList<int> LineNumbers);
