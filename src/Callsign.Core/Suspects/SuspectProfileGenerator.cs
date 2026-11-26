using System;
using System.Collections.Generic;
using System.Linq;
using Callsign.Core.Ports;

namespace Callsign.Core.Suspects
{
    /// <summary>Base chances (0..1) for traits that a part doesn't force.</summary>
    public sealed class TraitChances
    {
        public double Warrant { get; set; } = 0.15;
        /// <summary>Lies about their name, if they have a warrant.</summary>
        public double FalseNameIfWanted { get; set; } = 0.5;
        public double FalseNameOtherwise { get; set; } = 0.03;
        public double NoId { get; set; } = 0.15;
        public double Intoxicated { get; set; } = 0.1;
        /// <summary>Had a drink or two, but is under the limit.</summary>
        public double HadADrink { get; set; } = 0.25;
        public double OnDrugs { get; set; } = 0.08;
        public double CarryingDrugs { get; set; } = 0.12;
        /// <summary>Carries drugs, if they are on drugs.</summary>
        public double CarryingDrugsIfUsing { get; set; } = 0.4;
        public double ConcealedWeapon { get; set; } = 0.08;
        /// <summary>Nervous, with something to hide (warrant, drugs, weapon).</summary>
        public double NervousIfHiding { get; set; } = 0.7;
        public double NervousOtherwise { get; set; } = 0.2;
    }

    /// <summary>
    /// Rolls a suspect's identity and hidden traits. A suspect part can force a trait by carrying
    /// its name as a tag (e.g. "intoxicated", "warrant"); the rest are rolled from <see cref="TraitChances"/>.
    /// </summary>
    public sealed class SuspectProfileGenerator
    {
        readonly SuspectContent _content;
        readonly IRandom _random;
        readonly TraitChances _chances;

        public SuspectProfileGenerator(SuspectContent content, IRandom random, TraitChances? chances = null)
        {
            _content = content;
            _random = random;
            _chances = chances ?? new TraitChances();
        }

        /// <param name="male">The ped is male (picks the first name list).</param>
        /// <param name="tags">The suspect part's tags; trait names among them force that trait.</param>
        /// <param name="visiblyArmed">The suspect already carries a weapon openly: no hidden one on top.</param>
        public SuspectProfile Generate(bool male, IReadOnlyList<string> tags, bool visiblyArmed)
        {
            SuspectTraits forced = Forced(tags);
            bool Roll(SuspectTraits trait, double chance) => (forced & trait) != 0 || _random.NextDouble() < chance;

            SuspectTraits traits = SuspectTraits.None;
            if (Roll(SuspectTraits.Warrant, _chances.Warrant)) traits |= SuspectTraits.Warrant;
            bool wanted = (traits & SuspectTraits.Warrant) != 0;
            if (Roll(SuspectTraits.FalseName, wanted ? _chances.FalseNameIfWanted : _chances.FalseNameOtherwise))
                traits |= SuspectTraits.FalseName | SuspectTraits.NoId;   // can't show an ID that doesn't match the lie
            if (Roll(SuspectTraits.NoId, _chances.NoId)) traits |= SuspectTraits.NoId;
            if (Roll(SuspectTraits.Intoxicated, _chances.Intoxicated)) traits |= SuspectTraits.Intoxicated;
            if (Roll(SuspectTraits.OnDrugs, _chances.OnDrugs)) traits |= SuspectTraits.OnDrugs;
            bool using_ = (traits & SuspectTraits.OnDrugs) != 0;
            if (Roll(SuspectTraits.CarryingDrugs, using_ ? _chances.CarryingDrugsIfUsing : _chances.CarryingDrugs))
                traits |= SuspectTraits.CarryingDrugs;
            if (!visiblyArmed && Roll(SuspectTraits.ConcealedWeapon, _chances.ConcealedWeapon)) traits |= SuspectTraits.ConcealedWeapon;
            bool hiding = (traits & (SuspectTraits.Warrant | SuspectTraits.CarryingDrugs | SuspectTraits.ConcealedWeapon)) != 0;
            if (Roll(SuspectTraits.Nervous, hiding ? _chances.NervousIfHiding : _chances.NervousOtherwise)) traits |= SuspectTraits.Nervous;

            Identity real = NewIdentity(male);
            Identity stated = (traits & SuspectTraits.FalseName) != 0 ? Lie(real, male) : real;

            double bac = (traits & SuspectTraits.Intoxicated) != 0 ? Between(0.09, 0.24)
                : _random.NextDouble() < _chances.HadADrink ? Between(0.01, 0.06)
                : 0;

            string? offence = wanted ? Pick(_content.WarrantOffences) : null;
            HiddenWeapon? weapon = (traits & SuspectTraits.ConcealedWeapon) != 0 && _content.HiddenWeapons.Count > 0
                ? _content.HiddenWeapons[_random.Next(_content.HiddenWeapons.Count)]
                : null;
            if (weapon == null) traits &= ~SuspectTraits.ConcealedWeapon;

            var pockets = new List<string>();
            int everyday = _random.Next(0, 3);
            for (int i = 0; i < everyday && _content.PocketItems.Count > 0; i++)
            {
                string item = Pick(_content.PocketItems)!;
                if (!pockets.Contains(item)) pockets.Add(item);
            }
            if ((traits & SuspectTraits.CarryingDrugs) != 0 && _content.DrugItems.Count > 0) pockets.Add(Pick(_content.DrugItems)!);
            if (weapon != null) pockets.Add(weapon.Name);

            return new SuspectProfile(real, stated, traits, bac, offence, weapon?.Weapon, weapon?.Name, pockets);
        }

        static SuspectTraits Forced(IReadOnlyList<string> tags)
        {
            SuspectTraits forced = SuspectTraits.None;
            foreach (string tag in tags)
                if (Enum.TryParse(tag, true, out SuspectTraits t) && t != SuspectTraits.None && Enum.IsDefined(typeof(SuspectTraits), t))
                    forced |= t;
            if ((forced & SuspectTraits.FalseName) != 0) forced |= SuspectTraits.NoId;
            return forced;
        }

        /// <summary>A random person's name and age (e.g. a vehicle's registered owner).</summary>
        public Identity NewIdentity(bool male) =>
            new Identity(Pick(male ? _content.MaleNames : _content.FemaleNames) ?? "John",
                Pick(_content.LastNames) ?? "Doe", _random.Next(18, 66));

        /// <summary>A different name, about the same age (people lie about names, not decades).</summary>
        Identity Lie(Identity real, bool male)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                Identity fake = NewIdentity(male) with { Age = Math.Max(18, real.Age + _random.Next(-3, 4)) };
                if (fake.FullName != real.FullName) return fake;
            }
            return real with { LastName = real.LastName + "s" };
        }

        string? Pick(IReadOnlyList<string> list) => list.Count == 0 ? null : list[_random.Next(list.Count)];

        double Between(double min, double max) => Math.Round(min + _random.NextDouble() * (max - min), 3);
    }

    /// <summary>Picks what a suspect says, preferring lines written for one of their traits.</summary>
    public sealed class Dialogue
    {
        readonly SuspectContent _content;
        readonly IRandom _random;

        public Dialogue(SuspectContent content, IRandom random)
        {
            _content = content;
            _random = random;
        }

        /// <summary>
        /// A line for <paramref name="topic"/> that fits where the suspect was stopped, with <c>{name}</c>
        /// filled in. Empty if there are none.
        /// </summary>
        public string Say(DialogueTopic topic, SuspectProfile suspect, DialogueContext context = DialogueContext.OnFoot)
        {
            var lines = _content.Lines
                .Where(l => l.Topic == topic && (l.Context == DialogueContext.Any || l.Context == context))
                .ToList();
            var forTraits = lines.Where(l => l.Trait != SuspectTraits.None && suspect.Has(l.Trait)).ToList();
            var pool = forTraits.Count > 0 ? forTraits : lines.Where(l => l.Trait == SuspectTraits.None).ToList();
            if (pool.Count == 0) return "";
            return pool[_random.Next(pool.Count)].Text.Replace("{name}", suspect.Stated.FullName);
        }
    }
}
