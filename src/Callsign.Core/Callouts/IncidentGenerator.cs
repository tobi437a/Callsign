using System.Collections.Generic;
using System.Linq;
using Callsign.Core.Ports;
using Callsign.Core.World;

namespace Callsign.Core.Callouts
{
    /// <summary>One concrete incident: a part of each kind, plus the rolls made for it.</summary>
    public sealed record Incident(
        SituationPart Situation,
        LocationPart Location,
        SuspectPart Suspect,
        OutcomePart Outcome,
        string SuspectModel)
    {
        public string Title => Situation.Title;

        /// <summary>The dispatch line with the location and suspect filled in.</summary>
        public string DispatchMessage => Situation.Dispatch
            .Replace("{location}", Location.Name)
            .Replace("{suspect}", Suspect.Description);
    }

    /// <summary>
    /// Assembles incidents from the registered parts: picks a situation that has at least one
    /// fitting location in range, suspect and outcome, then picks each of those by weight.
    /// </summary>
    public sealed class IncidentGenerator
    {
        readonly CalloutParts _parts;
        readonly IRandom _random;

        public IncidentGenerator(CalloutParts parts, IRandom random)
        {
            _parts = parts;
            _random = random;
        }

        /// <param name="player">Where the player is now.</param>
        /// <param name="minDistance">Locations closer than this are skipped (no call next to you).</param>
        /// <param name="maxDistance">Locations further than this are skipped.</param>
        /// <param name="extraLocations">One-off locations for this draw only (e.g. a street near the player).</param>
        /// <returns>Null when no combination fits.</returns>
        public Incident? Generate(Position player, float minDistance, float maxDistance,
            IEnumerable<LocationPart>? extraLocations = null)
        {
            var locations = _parts.Locations.All
                .Concat(extraLocations ?? Enumerable.Empty<LocationPart>())
                .Where(l => InRange(l.Position.DistanceTo(player), minDistance, maxDistance))
                .ToList();

            var situations = _parts.Situations.All
                .Where(s => locations.Any(l => s.Fits(l) && l.Weight > 0)
                            && _parts.Suspects.All.Any(p => s.Fits(p) && p.Models.Count > 0 && p.Weight > 0)
                            && _parts.Outcomes.All.Any(o => s.Fits(o) && o.Weight > 0))
                .ToList();

            if (!Weighted.TryPick(situations, s => s.Weight, _random, out SituationPart situation)) return null;
            if (!Weighted.TryPick(locations.Where(situation.Fits).ToList(), l => l.Weight, _random, out LocationPart location)) return null;
            if (!Weighted.TryPick(_parts.Suspects.All.Where(p => situation.Fits(p) && p.Models.Count > 0).ToList(), p => p.Weight, _random, out SuspectPart suspect)) return null;
            if (!Weighted.TryPick(_parts.Outcomes.All.Where(situation.Fits).ToList(), o => o.Weight, _random, out OutcomePart outcome)) return null;

            string model = suspect.Models[_random.Next(suspect.Models.Count)];
            return new Incident(situation, location, suspect, outcome, model);
        }

        static bool InRange(float distance, float min, float max) => distance >= min && distance <= max;
    }
}
