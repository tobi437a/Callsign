using Callsign.Core.Callouts;

namespace Callsign.Core.Director
{
    /// <summary>
    /// Tilts what dispatch draws and when, on top of the parts' own weights. Factors multiply a part's
    /// weight and are always &gt; 0, so they shift the odds but never rule a fitting part out.
    /// </summary>
    public interface IDispatchDirector
    {
        double Factor(SituationPart situation);
        double Factor(LocationPart location);
        double Factor(SuspectPart suspect);
        double Factor(OutcomePart outcome);

        /// <summary>Multiplies the wait before the next call (1 = as configured).</summary>
        double GapScale { get; }
    }

    /// <summary>Plain weights, configured gaps.</summary>
    public sealed class NoDirector : IDispatchDirector
    {
        public static readonly NoDirector Instance = new NoDirector();
        public double Factor(SituationPart situation) => 1;
        public double Factor(LocationPart location) => 1;
        public double Factor(SuspectPart suspect) => 1;
        public double Factor(OutcomePart outcome) => 1;
        public double GapScale => 1;
    }
}
