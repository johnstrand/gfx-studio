namespace GfxStudio.Core.Primitives;

/// <summary>Interpolates colors between two or more ordered stops.</summary>
public sealed class ColorRamp
{
    private readonly ColorStop[] stops;

    public ColorRamp(IReadOnlyList<ColorStop> Stops)
    {
        ArgumentNullException.ThrowIfNull(Stops);

        if (Stops.Count < 2)
        {
            throw new ArgumentException("A color ramp must contain at least two stops.", nameof(Stops));
        }

        stops = new ColorStop[Stops.Count];
        for (var index = 0; index < Stops.Count; index++)
        {
            var stop = Stops[index];
            if (index > 0 && stop.Position <= stops[index - 1].Position)
            {
                throw new ArgumentException("Color stop positions must be strictly increasing.", nameof(Stops));
            }

            stops[index] = stop;
        }

        this.Stops = Array.AsReadOnly(stops);
    }

    public IReadOnlyList<ColorStop> Stops { get; }

    /// <summary>Samples the ramp using linear interpolation and clamps t to [0, 1].</summary>
    public ColorRgba Sample(float t) => SampleCore(t, smooth: false);

    /// <summary>Samples the ramp using smoothstep interpolation and clamps t to [0, 1].</summary>
    public ColorRgba SampleSmooth(float t) => SampleCore(t, smooth: true);

    private ColorRgba SampleCore(float t, bool smooth)
    {
        if (!float.IsFinite(t))
        {
            throw new ArgumentOutOfRangeException(nameof(t), t, "Sample position must be finite.");
        }

        t = Math.Clamp(t, 0f, 1f);
        if (t <= stops[0].Position)
        {
            return stops[0].Color;
        }

        if (t >= stops[^1].Position)
        {
            return stops[^1].Color;
        }

        var upperIndex = 1;
        while (stops[upperIndex].Position < t)
        {
            upperIndex++;
        }

        var left = stops[upperIndex - 1];
        var right = stops[upperIndex];
        var amount = (t - left.Position) / (right.Position - left.Position);
        if (smooth)
        {
            amount = amount * amount * (3f - (2f * amount));
        }

        return new ColorRgba(
            Interpolate(left.Color.R, right.Color.R, amount),
            Interpolate(left.Color.G, right.Color.G, amount),
            Interpolate(left.Color.B, right.Color.B, amount),
            Interpolate(left.Color.A, right.Color.A, amount));
    }

    private static byte Interpolate(byte start, byte end, float amount) =>
        (byte)Math.Clamp((int)MathF.Round(start + ((end - start) * amount), MidpointRounding.AwayFromZero), byte.MinValue, byte.MaxValue);
}
