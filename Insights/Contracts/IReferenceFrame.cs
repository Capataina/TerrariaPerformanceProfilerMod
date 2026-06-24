#nullable enable

namespace PerformanceProfiler.Insights;

/// <summary>
/// The comparable baseline a detector measures a signal against — the relativity
/// substrate that makes the spine law expressible ("no insight is ever an absolute
/// magnitude; every insight is the deviation of a signal from the comparable
/// baseline for that signal, on this machine, expressed as an effect size"). A
/// frame answers <i>what is normal for this signal in this reference context</i>
/// with a centre and a spread, so a detector can report an effect size rather than
/// a raw number.
///
/// <para>
/// One frame represents one comparison. The families differ only in which frame
/// they consult: Family A a <see cref="BaselineKind.ComparableContexts"/> frame,
/// Family B a <see cref="BaselineKind.SessionFirstHalf"/> / temporal frame, and so
/// on. A provider (Wave 3) hands back the right frame for a query; this interface
/// is the frame itself. Implementations read only an <see cref="IInsightInput"/>,
/// so they are pure logic over declared input (the L1 test axis).
/// </para>
/// </summary>
public interface IReferenceFrame
{
    /// <summary>Which comparison this frame represents; populates an insight's <see cref="BaselineKind"/> clause.</summary>
    BaselineKind Kind { get; }

    /// <summary>True once the frame has accumulated enough samples to be a trustworthy comparison.</summary>
    bool IsReady { get; }

    /// <summary>The comparable centre (median / mean) for the signal; NaN until <see cref="IsReady"/>.</summary>
    double Expected { get; }

    /// <summary>The comparable spread (MAD / standard deviation); NaN until <see cref="IsReady"/>. Used to turn a deviation into an effect size.</summary>
    double Dispersion { get; }

    /// <summary>Number of samples behind the baseline; populates <c>Evidence.BaselineN</c> and gates confidence.</summary>
    int SampleCount { get; }
}
