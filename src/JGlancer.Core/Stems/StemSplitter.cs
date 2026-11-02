namespace JGlancer.Core.Stems;

/// <summary>
/// A network that separates one piece of stereo audio (<see cref="SegmentFrames"/> frames, planar: left then right)
/// into <see cref="Sources"/> stereo sources (planar, source after source). The ONNX model in the app, an identity
/// in the tests.
/// </summary>
public interface IStemSeparator
{
    int Sources { get; }

    int SegmentFrames { get; }

    /// <param name="input">2 × <see cref="SegmentFrames"/> samples.</param>
    /// <param name="output">Sources × 2 × <see cref="SegmentFrames"/> samples.</param>
    void Separate(ReadOnlySpan<float> input, Span<float> output);
}

/// <summary>Where one stem's audio goes, in order, as it is finished (a WAV file being written).</summary>
public interface IStemSink
{
    void Write(ReadOnlySpan<float> left, ReadOnlySpan<float> right);
}

/// <summary>One piece of the song the network runs on.</summary>
public readonly record struct StemSegment(int Offset, int Length);

/// <summary>
/// Splits a song as Demucs' own <c>apply_model</c> does (W-85): the audio is normalized by its mono mix, cut into
/// pieces of the model's length overlapping by a quarter, each piece run through the network (a short last piece
/// gets the audio before it as context, zeros past the end) and joined with triangular cross-fades. Finished audio
/// goes to the sinks as each piece completes, so a long song never holds its stems in memory at once.
/// </summary>
public static class StemSplitter
{
    /// <summary>The share of a piece that overlaps the next.</summary>
    public const double Overlap = 0.25;

    /// <summary>The pieces of a song of <paramref name="frames"/> frames.</summary>
    public static IReadOnlyList<StemSegment> Plan(int frames, int segment)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(segment);
        var stride = (int)((1 - Overlap) * segment);
        var plan = new List<StemSegment>();
        for (var offset = 0; offset < frames; offset += stride)
        {
            plan.Add(new StemSegment(offset, Math.Min(segment, frames - offset)));
        }
        return plan;
    }

    /// <summary>The cross-fade of a piece: rising to 1 in the middle and falling again (Demucs' transition weight).</summary>
    public static float[] Weights(int segment)
    {
        var half = segment / 2;
        var weights = new float[segment];
        for (var i = 0; i < segment; i++)
        {
            weights[i] = i < half ? i + 1 : segment - i;
        }
        var max = weights.Max();
        for (var i = 0; i < segment; i++)
        {
            weights[i] /= max;
        }
        return weights;
    }

    /// <summary>
    /// Splits <paramref name="left"/> / <paramref name="right"/> (at the model's sample rate) and writes the sources
    /// named by <paramref name="sinks"/> (source index → sink). <paramref name="progress"/> gets the share done after
    /// each piece; cancellation is checked between pieces.
    /// </summary>
    public static void Split(float[] left, float[] right, IStemSeparator separator, IReadOnlyDictionary<int, IStemSink> sinks,
        Action<double>? progress, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(separator);
        ArgumentNullException.ThrowIfNull(sinks);
        var frames = Math.Min(left.Length, right.Length);
        var segment = separator.SegmentFrames;
        var (mean, deviation) = Normalization(left, right, frames);
        var plan = Plan(frames, segment);
        var weights = Weights(segment);
        var input = new float[2 * segment];
        var output = new float[separator.Sources * 2 * segment];
        // Sums of weighted output and of weights, from the first frame not written yet.
        var sums = sinks.Keys.ToDictionary(source => source, _ => (Left: new float[segment], Right: new float[segment]));
        var weightSums = new float[segment];
        var start = 0;
        for (var index = 0; index < plan.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var (offset, length) = plan[index];
            // A short piece is centred in the network's length with the audio around it (zeros past the ends).
            var pad = (segment - length) / 2;
            var from = offset - pad;
            for (var i = 0; i < segment; i++)
            {
                var frame = from + i;
                var inside = frame >= 0 && frame < frames;
                input[i] = inside ? (left[frame] - mean) / deviation : 0;
                input[segment + i] = inside ? (right[frame] - mean) / deviation : 0;
            }
            separator.Separate(input, output);
            var shift = offset - start;
            for (var i = 0; i < length; i++)
            {
                weightSums[shift + i] += weights[i];
            }
            foreach (var (source, (sumLeft, sumRight)) in sums)
            {
                var baseLeft = source * 2 * segment + pad;
                var baseRight = baseLeft + segment;
                for (var i = 0; i < length; i++)
                {
                    sumLeft[shift + i] += output[baseLeft + i] * weights[i];
                    sumRight[shift + i] += output[baseRight + i] * weights[i];
                }
            }
            // Everything before the next piece is final.
            var next = index + 1 < plan.Count ? plan[index + 1].Offset : frames;
            var done = next - start;
            foreach (var (source, (sumLeft, sumRight)) in sums)
            {
                var outLeft = new float[done];
                var outRight = new float[done];
                for (var i = 0; i < done; i++)
                {
                    outLeft[i] = sumLeft[i] / weightSums[i] * deviation + mean;
                    outRight[i] = sumRight[i] / weightSums[i] * deviation + mean;
                }
                sinks[source].Write(outLeft, outRight);
                Slide(sumLeft, done);
                Slide(sumRight, done);
            }
            Slide(weightSums, done);
            start = next;
            progress?.Invoke((double)(index + 1) / plan.Count);
        }
    }

    /// <summary>Demucs normalizes by the mono mix: its mean and (unbiased) standard deviation.</summary>
    private static (float Mean, float Deviation) Normalization(float[] left, float[] right, int frames)
    {
        if (frames == 0)
        {
            return (0, 1);
        }
        double sum = 0;
        for (var i = 0; i < frames; i++)
        {
            sum += (left[i] + right[i]) / 2.0;
        }
        var mean = sum / frames;
        double squares = 0;
        for (var i = 0; i < frames; i++)
        {
            var d = (left[i] + right[i]) / 2.0 - mean;
            squares += d * d;
        }
        var deviation = frames > 1 ? Math.Sqrt(squares / (frames - 1)) : 0;
        // Silence: no scaling (Demucs would divide by zero).
        return ((float)mean, deviation > 1e-8 ? (float)deviation : 1);
    }

    private static void Slide(float[] buffer, int by)
    {
        if (by >= buffer.Length)
        {
            Array.Clear(buffer);
            return;
        }
        Array.Copy(buffer, by, buffer, 0, buffer.Length - by);
        Array.Clear(buffer, buffer.Length - by, by);
    }
}
