using System;
using Callsign.Core.Duty;
using Callsign.Core.Entities;
using Callsign.Core.Events;
using Callsign.Core.Ports;
using Callsign.Core.World;

namespace Callsign.Core.Callouts
{
    /// <summary>
    /// Dispatch: while on duty, offers a generated incident now and then, and runs the one the
    /// player accepts. Owns the callout state; announces every change on the bus.
    /// </summary>
    public sealed class CalloutService
    {
        public enum Phase { OffDuty, Waiting, Offered, Active }

        readonly IncidentGenerator _generator;
        readonly DutyService _duty;
        readonly IEventBus _bus;
        readonly IClock _clock;
        readonly IRandom _random;
        readonly CalloutSceneServices _services;
        long _nextOfferAt;
        long _offerExpiresAt;
        CalloutScene? _scene;

        public CalloutService(IncidentGenerator generator, DutyService duty, IEventBus bus, IClock clock,
            IRandom random, CalloutSceneServices services)
        {
            _generator = generator;
            _duty = duty;
            _bus = bus;
            _clock = clock;
            _random = random;
            _services = services;
        }

        public Phase Current { get; private set; } = Phase.OffDuty;

        /// <summary>The offered or running incident.</summary>
        public Incident? Incident { get; private set; }

        CalloutSettings Settings => _services.Settings;

        /// <param name="interactPressed">The player pressed the interact key this frame.</param>
        public void Tick(bool interactPressed)
        {
            if (!_duty.OnDuty)
            {
                if (Current == Phase.Offered || Current == Phase.Active) Finish(CalloutEndReason.Cancelled);
                Current = Phase.OffDuty;
                return;
            }

            switch (Current)
            {
                case Phase.OffDuty:
                    Schedule(Settings.FirstOfferMinMs, Settings.FirstOfferMaxMs);
                    break;
                case Phase.Waiting:
                    if (_clock.NowMs >= _nextOfferAt) TryOffer();
                    break;
                case Phase.Offered:
                    if (_clock.NowMs >= _offerExpiresAt)
                    {
                        _services.Notifier.Notify("~c~Dispatch:~s~ call reassigned.");
                        Finish(CalloutEndReason.OfferExpired);
                    }
                    break;
                case Phase.Active:
                    CalloutEndReason? ended = _scene!.Tick(interactPressed);
                    if (ended != null) Finish(ended.Value);
                    break;
            }
        }

        /// <summary>
        /// Offers a call right now instead of waiting for dispatch. Only while on duty and with no
        /// call offered or running. False if it couldn't (including when no incident fits here).
        /// </summary>
        public bool RequestNow()
        {
            if (!_duty.OnDuty || Current == Phase.Offered || Current == Phase.Active) return false;
            TryOffer();
            if (Current == Phase.Offered) return true;
            _services.Notifier.Notify("~c~Dispatch:~s~ no calls in your area right now.");
            return false;
        }

        /// <summary>Takes the offered call. False if nothing is on offer.</summary>
        public bool Accept()
        {
            if (Current != Phase.Offered) return false;
            Incident incident = Incident!;
            Current = Phase.Active;
            _scene = new CalloutScene(incident, _services);
            _services.Notifier.Notify("~b~Responding~s~ to " + incident.Title + ".");
            _bus.Publish(new CalloutAccepted(incident));
            return true;
        }

        /// <summary>Turns the offered call down. False if nothing is on offer.</summary>
        public bool Decline()
        {
            if (Current != Phase.Offered) return false;
            Finish(CalloutEndReason.Declined);
            return true;
        }

        /// <summary>The player calls off the running callout. False if none is running.</summary>
        public bool Cancel()
        {
            if (Current != Phase.Active) return false;
            _services.Notifier.Notify("Callout ~o~cancelled~s~.");
            Finish(CalloutEndReason.Cancelled);
            return true;
        }

        /// <summary>Script reload/unload: delete the running scene at once. No events.</summary>
        public void Stop()
        {
            _scene?.Close(ReleaseMode.Delete);
            _scene = null;
            Incident = null;
            Current = Phase.OffDuty;
        }

        void TryOffer()
        {
            Position player = _services.World.PlayerPosition;
            Incident? incident = _generator.Generate(player, Settings.MinDistance, Settings.MaxDistance, NearbyStreets(player));
            if (incident == null)
            {
                _nextOfferAt = _clock.NowMs + Settings.RetryMs;
                return;
            }

            Incident = incident;
            Current = Phase.Offered;
            _offerExpiresAt = _clock.NowMs + Settings.OfferTimeoutMs;
            _services.Notifier.Notify("~b~Dispatch:~s~ ~y~" + incident.Title + "~s~~n~" + incident.DispatchMessage);
            _bus.Publish(new CalloutOffered(incident));
        }

        /// <summary>A street somewhere in range, so there's always a fallback when no fixed location is close.</summary>
        LocationPart[] NearbyStreets(Position player)
        {
            double angle = _random.NextDouble() * 2 * Math.PI;
            double distance = Settings.MinDistance + _random.NextDouble() * (Settings.MaxDistance - Settings.MinDistance) * 0.6;
            var target = new Position(player.X + (float)(Math.Cos(angle) * distance), player.Y + (float)(Math.Sin(angle) * distance), player.Z);
            if (!_services.World.TryFindStreetNear(target, out Position street, out _)) return Array.Empty<LocationPart>();
            return new[] { new LocationPart("street", _services.World.DescribeLocation(street), street, new[] { "street", "public" }) };
        }

        void Finish(CalloutEndReason reason)
        {
            Incident incident = Incident!;
            _scene?.Close(ReleaseMode.Dismiss);
            _scene = null;
            Incident = null;
            Current = Phase.Waiting;
            if (reason == CalloutEndReason.Declined || reason == CalloutEndReason.OfferExpired)
                Schedule(Settings.RetryMs, Settings.RetryMs * 2);
            else
                Schedule(Settings.NextOfferMinMs, Settings.NextOfferMaxMs);
            _bus.Publish(new CalloutEnded(incident, reason));
        }

        void Schedule(long minMs, long maxMs)
        {
            Current = Phase.Waiting;
            _nextOfferAt = _clock.NowMs + minMs + (long)(_random.NextDouble() * (maxMs - minMs));
        }
    }
}
