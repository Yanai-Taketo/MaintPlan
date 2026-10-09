using System.IO.Compression;
using System.Text;

namespace MaintPlan.Tests.Support;

/// <summary>
/// ブックとして開けないファイル(利用者が決めた文言の「壊れている、パスワード付きなど」)を作る。
/// どれも、例外の種類によらず「&lt;ファイル名&gt;: ブックとして読めません。」の1件にする(作りの細部)。
/// ClosedXML 0.105.1 は、ブックでない zip には NullReferenceException を、ほかには FileFormatException を投げる(確かめ済み)ので、
/// 特定の例外だけを捕まえる読み方では、どれかで止まる。
/// </summary>
public static class UnreadableWorkbooks
{
    public const string Text = "文字のファイル";

    public const string Empty = "空のファイル";

    /// <summary>正しい zip で、中身がブックでないもの(別の zip の名前を変えたものなど)。エントリは a.txt の1つだけ。</summary>
    public const string ZipWithoutWorkbook = "ブックでない zip";

    /// <summary>パスワード付きのブックの代わり。パスワード付きの .xlsx は、zip ではなく CFB(複合ファイル)の形で保存される。</summary>
    public const string Encrypted = "CFB の印で始まるファイル";

    /// <summary>正しいブック(例12/条件1の入力.xlsx)の、前の半分だけ。</summary>
    public const string Truncated = "途中で切れたブック";

    /// <summary>CFB の先頭の8バイト。</summary>
    private static readonly byte[] CfbSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    public static TheoryData<string> Kinds => [Text, Empty, ZipWithoutWorkbook, Encrypted, Truncated];

    /// <summary>kind のファイルを path に作る。path にファイルがあれば置き換える。</summary>
    public static void Write(string kind, string path)
    {
        File.Delete(path);
        switch (kind)
        {
            case Text:
                File.WriteAllText(path, "ID,管理番号\n1,例1\n");
                break;
            case Empty:
                File.WriteAllBytes(path, []);
                break;
            case ZipWithoutWorkbook:
            {
                using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
                using var writer = new StreamWriter(archive.CreateEntry("a.txt").Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write("ブックではない");
                break;
            }

            case Encrypted:
            {
                // CFB の見出しの大きさ(512バイト)にし、印の後は0で埋める
                var header = new byte[512];
                CfbSignature.CopyTo(header, 0);
                File.WriteAllBytes(path, header);
                break;
            }

            case Truncated:
            {
                var workbook = File.ReadAllBytes(TestCases.Get("例12/条件1").WorkbookPath);
                File.WriteAllBytes(path, workbook[..(workbook.Length / 2)]);
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "知らないファイルの種類です。");
        }
    }
}
