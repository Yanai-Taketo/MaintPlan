using System.Globalization;

namespace MaintPlan.IO.Input;

/// <summary>
/// ブックの数値のセルの値を、有効数字15桁に丸めた数。値は Significand × 10 の Exponent 乗で、
/// Significand は末尾に0のない整数(値が0なら、Significand も Exponent も0)。
/// Excel の数は15桁までなので、15桁に丸めると、浮動小数点数の誤差(0.1+0.2 の 0.30000000000000004 など)が消える。
/// </summary>
internal readonly record struct RoundedNumber(long Significand, int Exponent)
{
    /// <summary>
    /// 浮動小数点数を、有効数字15桁に丸める。浮動小数点数を受け取るのは、ブックの読み込みの境目のここだけとする。
    /// 指数の形の文字(書式「E14」。仮数は小数点以下14桁で、有効数字15桁に丸めたもの)にしてから、仮数と指数を整数で読む。
    /// </summary>
    public static RoundedNumber Of(double value)
    {
        // 例:-1234.5 は「-1.23450000000000E+003」
        var text = value.ToString("E14", CultureInfo.InvariantCulture);
        var mark = text.IndexOf('E', StringComparison.Ordinal);
        var significand = long.Parse(text[..mark].Replace(".", string.Empty, StringComparison.Ordinal), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        var exponent = int.Parse(text[(mark + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) - 14;
        if (significand == 0)
        {
            return default;
        }

        while (significand % 10 == 0)
        {
            significand /= 10;
            exponent++;
        }

        return new RoundedNumber(significand, exponent);
    }

    /// <summary>絶対値が10の15乗以上(16桁以上の数)か。</summary>
    public bool IsTooLarge =>
        Significand != 0 && Math.Abs(Significand).ToString(CultureInfo.InvariantCulture).Length + Exponent > 15;

    /// <summary>整数なら、CSV と同じ形の文字(桁区切りも指数もない数字。負なら「-」を付ける)。整数でないか、16桁以上の数なら null。</summary>
    public string? IntegerText
    {
        get
        {
            if (Exponent < 0 || IsTooLarge)
            {
                return null;
            }

            var value = Significand;
            for (var count = 0; count < Exponent; count++)
            {
                value *= 10;
            }

            return value.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>小数点以下1桁までの数なら、CSV と同じ形の文字(整数は「10」、小数は「8.5」)。ほかは null。</summary>
    public string? TenthsText
    {
        get
        {
            if (Exponent != -1)
            {
                return IntegerText;
            }

            var sign = Significand < 0 ? "-" : string.Empty;
            var magnitude = Math.Abs(Significand);
            return string.Create(CultureInfo.InvariantCulture, $"{sign}{magnitude / 10}.{magnitude % 10}");
        }
    }
}
