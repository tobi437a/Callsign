using System;
using System.Collections.Generic;
using System.Linq;

namespace Callsign.Core.Suspects
{
    /// <summary>Substances a roadside drug swab screens for. A suspect can have several in their system.</summary>
    [Flags]
    public enum Drug
    {
        None = 0,
        Cannabis = 1 << 0,
        Cocaine = 1 << 1,
        Methamphetamine = 1 << 2,
        Opiates = 1 << 3,
        Benzodiazepines = 1 << 4,
    }

    /// <summary>The panel a swab tests, in the order the result lists it.</summary>
    public static class DrugPanel
    {
        public static readonly IReadOnlyList<Drug> All =
            new[] { Drug.Cannabis, Drug.Cocaine, Drug.Methamphetamine, Drug.Opiates, Drug.Benzodiazepines };

        /// <summary>How the swab result names the substance.</summary>
        public static string Name(Drug drug) => drug switch
        {
            Drug.Cannabis => "THC",
            Drug.Cocaine => "Cocaine",
            Drug.Methamphetamine => "Amphetamines",
            Drug.Opiates => "Opiates",
            Drug.Benzodiazepines => "Benzodiazepines",
            _ => drug.ToString(),
        };

        /// <summary>The single drugs in a set, in panel order.</summary>
        public static IEnumerable<Drug> In(Drug set) => All.Where(d => (set & d) != 0);
    }
}
