namespace GfxStudio.Core.Evaluation;

/// <summary>Validates and converts evaluated values used as deterministic seeds.</summary>
internal static class GfxSeedValue
{
    private const double UInt64ExclusiveUpperBound = 18446744073709551616d;

    public static bool TryParse(GfxValue? value, out ulong seed)
    {
        if (value is GfxNumber number
            && double.IsFinite(number.Value)
            && number.Value >= 0
            && number.Value < UInt64ExclusiveUpperBound
            && number.Value == Math.Truncate(number.Value))
        {
            seed = (ulong)number.Value;
            return true;
        }

        seed = 0;
        return false;
    }
}
