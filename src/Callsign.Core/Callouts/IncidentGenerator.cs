using System.Collections.Generic;
using System.Linq;
using Callsign.Core.Ports;
using Callsign.Core.World;

namespace Callsign.Core.Callouts
{
    /// <summary>One suspect of an incident: which ped model, and how they will react.</summary>
    public sealed record SuspectRoll(string Model, OutcomePart Outcome);

    /// <summary>One concrete incident: a part of each kind, plus the rolls made for it.</summary>
    /// <param name="Suspects">At least one. In a vehicle, the first one drives.</param>
    /// <param name="VehicleModel">The suspects' vehicle, or null when they are on foot.</param>
    public sealed record Incident(
        SituationPart Situation,
        LocationPart Location,
        SuspectPart Suspect,
        IReadOnlyList<SuspectRoll> Suspects,
        string? VehicleModel = null)
    {
        public string Title => Situation.Title;

        /// <summary>The first suspect's outcome (the driver's, in a vehicle).</summary>
        public OutcomePart Outcome => Suspects[0].Outcome;

        /// <summary>The first suspect's model.</summary>
        public string SuspectModel => Suspects[0].Model;

        /// <summary>The dispatch line with the location, suspect and number of suspects filled in.</summary>
        public string DispatchMessage => Situation.Dispatch
            .Replace("{location}", Location.Name)
            .Replace("{suspect}", Suspect.Description)
            .Replace("{count}", CountWord(Suspects.Count));

        static string CountWord(int n) =>
            n == 1 ? "one" : n == 2 ? "two" : n == 3 ? "three" : n == 4 ? "four" : n.ToString();
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
            var outcomes = _parts.Outcomes.All.Where(situation.Fits).ToList();
            if (!Weighted.TryPick(outcomes, o => o.Weight, _random, out OutcomePart outcome)) return null;

            var rolls = new List<SuspectRoll> { new SuspectRoll(PickModel(suspect), outcome) };
            int count = situation.MaxSuspects <= situation.MinSuspects ? situation.MinSuspects
                : situation.MinSuspects + _random.Next(situation.MaxSuspects - situation.MinSuspects + 1);
            while (rolls.Count < count)
            {
                Weighted.TryPick(outcomes, o => o.Weight, _random, out OutcomePart other);   // can't fail: one was picked above
                rolls.Add(new SuspectRoll(PickModel(suspect), other));
            }
            string? vehicle = suspect.InVehicle ? suspect.Vehicles[_random.Next(suspect.Vehicles.Count)] : null;
            return new Incident(situation, location, suspect, rolls, vehicle);
        }

        string PickModel(SuspectPart suspect) => suspect.Models[_random.Next(suspect.Models.Count)];

        static bool InRange(float distance, float min, float max) => distance >= min && distance <= max;
    }
}
