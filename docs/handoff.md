# 引継ぎ資料

2026-10-05 時点の状態です。次のセッションは、この資料と CLAUDE.md、docs/design.md(設計書)を読んでから始めてください。

## 今の状態

- 工程1・段1(ソリューション、CSV の読み込み、例1〜例15のテストデータ)は完了し、PR #1 で main にマージ済み。
- `dotnet test` は286件中224件が成功、62件が失敗。失敗はすべて計算のテストで、計算が未実装のため NotImplementedException になる。
- テストデータの値は、利用者がすべてを確かめられていない。工程1が完成した後の点検で確かめる。
- 次の作業は段2(期間の数え方と月への割り振り)。

## 進め方の決まり

- 段ごとに、main から作業ブランチを切ってプッシュし、PR を作る。利用者が確認してマージする。
- コードの名前(クラス名・プロパティ名など)は英語。テストデータのファイル名とフォルダ名は日本語。
- テストデータの形は tests/data/README.md に従う。テストデータを足すときは、設計書の値をそのまま写し、写し手を分けて突き合わせる。
- 段が終わったら、通ったテストと残った課題を報告して確認を待つ(CLAUDE.md)。

## 構成

| 場所 | 中身 |
| --- | --- |
| src/MaintPlan.Core/Model | 9つのテーブルの型(Tables.cs)、PlanData、YearMonth、選択項目の enum(Enums.cs)と設計書での呼び名(Labels.cs) |
| src/MaintPlan.Core/Calculation | 計算の入口(Calculators.cs)と結果の型(Results.cs)。入口はすべて未実装 |
| src/MaintPlan.IO/Csv | 9つのテーブルの CSV の読み込み(PlanCsvReader)と、CSV を表として読む CsvTable |
| src/MaintPlan.IO/Results | 計算結果を4章の表の形にする ResultTables(すべて未実装)と TextTable |
| src/MaintPlan.Cli | 確認用コンソール。中身は段7で作る |
| tests/MaintPlan.Tests | CalculationTests が期待値ファイルごとに計算して比べる。Support/ActualTables.cs が、期待値の種類ごとに Core の計算と ResultTables を呼ぶ |
| tests/data | 31ケースのテストデータと README |

金額は long、人工と作業日数は10倍した long、日付は DateOnly で持ちます。

## 段ごとに通る期待値

| 段 | 作る計算 | 期待値の種類(ファイル数) |
| --- | --- | --- |
| 2 | Period.DaysByMonth、MonthlyAllocation.Calculate | 月ごとの日数(3)、月ごとの値(10)、人工の月ごとの内訳(3)、計算に使わない修正(2) |
| 3 | BudgetInitialValue.Calculate | 予算額の初期値(6)、労務費の内訳(6) |
| 4 | QuarterlyAggregation.Calculate | 山積み(5)、人工の内訳(2)、未入力の件数(4) |
| 5 | RemainingBudget.Calculate | 残予算と見込み残(6)、未実績見込みの内訳(3) |
| 6 | SaveValidation.Validate | 保存の確認(12) |

各段では、計算のほかに、その期待値の種類に当たる ResultTables のメソッドも作ります。

## 段2で作るもの

- Core:`Period.DaysByMonth` と `MonthlyAllocation.Calculate`。仕様は設計書4章の「共通の決まり」と「月への割り振り」。
- IO:`ResultTables` の `MonthDays`・`MonthlyValues`・`MonthlyManDays`・`UnusedOverrides`。
- 表の作り方(README の期待値の決まりに合わせる):
  - 渡された列名で表を作る。「例」「費用内訳」の列がなければ、その列で分けずに合計する。
  - 「例」は工事の管理番号、「費用内訳」は費用区分、「行」は作業明細の ID。人員区分は「直営」が種別ごとの合計、「直営・機械」が区分ごとの値。
  - 年月の列は YYYY-MM、「YYYY年度 時期未定」、「年度のない時期未定」。
  - 見積額が空の費用内訳の見積額と、年割が1件もない費用内訳の予算額は「未入力」と書く。
  - 人工は小数点以下1桁で書く。
- 段2の完了の条件(8章):共通の決まりの例、例1〜例9、例15の月ごとの値と、計算に使わない修正が一致する。上の表の段2の18ファイルが該当する。

## 確認を受けていない決まり

テストデータを作るときに決めたもので、利用者の確認がまだのものです。

1. 例14の「理由の種類」の名前(README の「値の書き方」)。工事の日付が空欄で作業明細に日付があるとき(例14の変更02)は、「工事の日付が空欄で作業明細に日付がある」だけを出す。
2. 労務費の内訳には、予算額の計算に含めない区分の作業明細の行を出さない(例10の3行目)。
3. 設計書に書かれていない値:工事名は管理番号と同じ、状態は計画中、費用区分は修繕費、計上の考え方は出来高、例1〜例5の人員区分の区分名は「一般」、例5の集計基準日は2027-03-31で含める状態はすべて。

## 環境

- コンテナに .NET が入っていない。セッションの最初に `apt-get install -y dotnet-sdk-10.0` で入れる(Ubuntu のパッケージ。10.0.112 で動作を確認)。クラウド環境のセットアップスクリプトに入れておけば不要になる。
- NuGet の検索サービスはプロキシで止められている。そのため `dotnet new install <パッケージ名>` は使えない。テンプレートが要るときは、api.nuget.org から nupkg を直接取得して `dotnet new install <ファイル>` で入れる。パッケージの復元は通る。
- テストは `dotnet test` で、Microsoft Testing Platform で動く(global.json で指定)。
- GitHub の操作は MCP のツールで行う(gh コマンドは使えない)。

## 次に拾う項目

1. 上の「確認を受けていない決まり」の確認。
2. 段2の着手:main から作業ブランチを切る。
3. 工程1が完成した後の、テストデータの値の点検。
