using System.Collections.Generic;
using System.Linq;
using Callsign.Core.Ports;

namespace Callsign.Core.Suspects
{
    /// <summary>What dispatch knows about a vehicle: found out by running its plate.</summary>
    /// <param name="Plate">As shown on the plate.</param>
    /// <param name="Model">What the vehicle is, e.g. "Karin Futo".</param>
    /// <param name="Owner">Registered owner. Often, but not always, the driver.</param>
    public sealed record VehicleRecord(string Plate, string Model, Identity Owner, bool Stolen, bool RegistrationExpired, bool Uninsured)
    {
        /// <summary>Anything on the record worth acting on.</summary>
        public bool Flagged => Stolen || RegistrationExpired || Uninsured;
    }

    /// <summary>Chances (0..1) for a vehicle's record.</summary>
    public sealed class VehicleRecordChances
    {
        /// <summary>A driver with a warrant is in a stolen car (unless the call already says it's stolen).</summary>
        public double StolenIfWanted { get; set; } = 0.15;
        /// <summary>Someone else's car (family, friend, employer), when it isn't stolen.</summary>
        public double BorrowedCar { get; set; } = 0.2;
        public double RegistrationExpired { get; set; } = 0.1;
        public double Uninsured { get; set; } = 0.07;
    }

    public sealed class VehicleRecordGenerator
    {
        /// <summary>A suspect part with this tag drives a stolen vehicle.</summary>
        public const string StolenTag = "stolen";

        readonly SuspectProfileGenerator _people;
        readonly IRandom _random;
        readonly VehicleRecordChances _chances;

        public VehicleRecordGenerator(SuspectProfileGenerator people, IRandom random, VehicleRecordChances? chances = null)
        {
            _people = people;
            _random = random;
            _chances = chances ?? new VehicleRecordChances();
        }

        /// <param name="driver">Who is driving it.</param>
        /// <param name="tags">The driver's suspect part tags (<see cref="StolenTag"/> forces a stolen vehicle).</param>
        public VehicleRecord Generate(string plate, string model, SuspectProfile driver, IReadOnlyList<string> tags)
        {
            bool stolen = tags.Contains(StolenTag, System.StringComparer.OrdinalIgnoreCase)
                || (driver.Has(SuspectTraits.Warrant) && Chance(_chances.StolenIfWanted));
            Identity owner = stolen || Chance(_chances.BorrowedCar) ? _people.NewIdentity(_random.NextDouble() < 0.5) : driver.Real;
            return new VehicleRecord(plate, model, owner, stolen, Chance(_chances.RegistrationExpired), Chance(_chances.Uninsured));
        }

        bool Chance(double p) => _random.NextDouble() < p;
    }
}
