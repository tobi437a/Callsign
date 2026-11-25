using System;
using System.Collections.Generic;

namespace Callsign.Core.Suspects
{
    /// <summary>
    /// Hidden facts about a suspect. The player doesn't see them; they find them out by
    /// questioning, checking ID, running the name, testing and searching.
    /// </summary>
    [Flags]
    public enum SuspectTraits
    {
        None = 0,
        /// <summary>Wanted on an outstanding warrant (see <see cref="SuspectProfile.WarrantOffence"/>).</summary>
        Warrant = 1 << 0,
        /// <summary>Gives a made-up name. Always comes with <see cref="NoId"/>.</summary>
        FalseName = 1 << 1,
        /// <summary>Has no ID on them.</summary>
        NoId = 1 << 2,
        /// <summary>Has been drinking: over the limit on a breath test.</summary>
        Intoxicated = 1 << 3,
        /// <summary>Under the influence of drugs: a drug swab comes back positive.</summary>
        OnDrugs = 1 << 4,
        /// <summary>Has drugs in their pockets.</summary>
        CarryingDrugs = 1 << 5,
        /// <summary>Has a weapon they keep out of sight (see <see cref="SuspectProfile.ConcealedWeapon"/>).</summary>
        ConcealedWeapon = 1 << 6,
        /// <summary>On edge: more likely to run or panic when things escalate.</summary>
        Nervous = 1 << 7,
    }

    /// <summary>A name and age, as on an ID card.</summary>
    public sealed record Identity(string FirstName, string LastName, int Age)
    {
        public string FullName => FirstName + " " + LastName;
    }

    /// <summary>Who a suspect really is, and what they will tell the officer.</summary>
    public sealed class SuspectProfile
    {
        /// <summary>Blood alcohol limit for a breath test to count as over.</summary>
        public const double LegalAlcoholLimit = 0.08;

        public SuspectProfile(Identity real, Identity stated, SuspectTraits traits, double bloodAlcohol,
            string? warrantOffence, string? concealedWeapon, string? concealedWeaponName, IReadOnlyList<string> pockets)
        {
            Real = real;
            Stated = stated;
            Traits = traits;
            BloodAlcohol = bloodAlcohol;
            WarrantOffence = warrantOffence;
            ConcealedWeapon = concealedWeapon;
            ConcealedWeaponName = concealedWeaponName;
            Pockets = pockets;
        }

        /// <summary>Their real identity (what the records say).</summary>
        public Identity Real { get; }

        /// <summary>The name they give when asked. Differs from <see cref="Real"/> if they lie.</summary>
        public Identity Stated { get; }

        public SuspectTraits Traits { get; }

        /// <summary>Blood alcohol content in percent, e.g. 0.12.</summary>
        public double BloodAlcohol { get; }

        /// <summary>What the warrant is for, or null.</summary>
        public string? WarrantOffence { get; }

        /// <summary>Game name of the hidden weapon (e.g. "WEAPON_KNIFE"), or null.</summary>
        public string? ConcealedWeapon { get; }

        /// <summary>How a search describes the hidden weapon, e.g. "a switchblade".</summary>
        public string? ConcealedWeaponName { get; }

        /// <summary>What a pat-down turns up, drugs and weapon included.</summary>
        public IReadOnlyList<string> Pockets { get; }

        public bool Has(SuspectTraits trait) => (Traits & trait) == trait;

        public bool OverAlcoholLimit => BloodAlcohol >= LegalAlcoholLimit;
    }
}
