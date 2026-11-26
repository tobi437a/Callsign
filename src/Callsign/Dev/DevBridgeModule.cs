using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using Callsign.Core.Callouts;
using Callsign.Core.Duty;
using Callsign.Core.Entities;
using Callsign.Core.Ports;
using Callsign.Engine;
using Callsign.Modules;
using Keys = System.Windows.Forms.Keys;

namespace Callsign.Dev
{
    /// <summary>
    /// Dev-only remote control, so in-game checks can be scripted (tools\game.ps1 cmd ...).
    /// Runs only when &lt;game&gt;\Callsign\Dev\ exists; players never have that folder.
    /// Reads Dev\commands.txt (lines "&lt;id&gt; &lt;command&gt; [args]"), runs them in order, one per
    /// tick ("wait" pauses the queue), and appends "&lt;id&gt; ok|error &lt;text&gt;" to Dev\results.txt.
    /// Drives the mod through the same services and keys a player uses.
    /// </summary>
    internal sealed class DevBridgeModule : IModule
    {
        public const string Help =
            "ping | state | expect <text> | expect! <text> | duty on|off | call [situationId] | accept | decline | cancel | key <Key> [holdMs] | wait <ms> "
            + "| tp <x> <y> <z> | tp scene [metres] | car [model] | lights on|off | god on|off | hour <0-23> | mark <text>";

        readonly string _dir;
        readonly DutyService _duty;
        readonly CalloutService _callouts;
        readonly EntityScopes _scopes;
        readonly KeyInput _input;
        readonly ILog _log;
        readonly Queue<(string Id, string[] Args)> _queue = new Queue<(string, string[])>();
        int _waitUntil;
        int _nextPoll;

        public DevBridgeModule(string dir, DutyService duty, CalloutService callouts, EntityScopes scopes, KeyInput input, ILog log)
        {
            _dir = dir;
            _duty = duty;
            _callouts = callouts;
            _scopes = scopes;
            _input = input;
            _log = log;
        }

        public string Name => "DevBridge";

        string CommandsFile => Path.Combine(_dir, "commands.txt");
        string ResultsFile => Path.Combine(_dir, "results.txt");

        public void Start() => _log.Info("Dev bridge on: " + _dir);

        public void Tick()
        {
            int now = Environment.TickCount;
            if (now - _nextPoll >= 0)
            {
                _nextPoll = now + 250;
                ReadCommands();
            }
            if (_queue.Count == 0 || now - _waitUntil < 0) return;

            // A paused game (it pauses on focus loss) would sit in the menu; scripted checks need it running.
            if (Function.Call<bool>(Hash.IS_PAUSE_MENU_ACTIVE)) Function.Call(Hash.SET_FRONTEND_ACTIVE, false);

            (string id, string[] args) = _queue.Dequeue();
            string status, text;
            try { (status, text) = ("ok", Run(args)); }
            catch (Exception ex) { (status, text) = ("error", ex.Message); }
            string line = status + " [" + string.Join(" ", args) + "] " + text;
            _log.Info("Dev " + line);
            File.AppendAllText(ResultsFile, id + " " + line + Environment.NewLine);
        }

        public void Stop() { }

        void ReadCommands()
        {
            if (!File.Exists(CommandsFile)) return;
            string[] lines;
            try
            {
                lines = File.ReadAllLines(CommandsFile);
                File.Delete(CommandsFile);
            }
            catch (IOException) { return; }   // still being written; next poll
            foreach (string line in lines)
            {
                string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2) _queue.Enqueue((parts[0], parts.Skip(1).ToArray()));
            }
        }

        string Run(string[] a)
        {
            string arg(int i) => i < a.Length ? a[i] : "";
            Ped player = Game.Player.Character;
            switch (a[0].ToLowerInvariant())
            {
                case "ping": return "pong";
                case "help": return Help;
                case "state": return State();
                case "expect":    // the state line contains the text (a scenario check)
                case "expect!":   // ... or doesn't
                    string want = string.Join(" ", a.Skip(1)), state = State();
                    bool has = state.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (has != (a[0] == "expect")) throw new InvalidOperationException("FAILED " + a[0] + " '" + want + "' in: " + state);
                    return "";
                case "mark":
                    _log.Info("---- " + string.Join(" ", a.Skip(1)) + " ----");
                    return "";

                case "duty":
                    if ((arg(1) == "on") != _duty.OnDuty) _duty.Toggle();
                    return _duty.OnDuty ? "on duty" : "off duty";

                case "call":
                    if (_callouts.Current == CalloutService.Phase.OffDuty) throw new InvalidOperationException("off duty (or duty not ticked yet)");
                    if (!_callouts.RequestNow(a.Length > 1 ? arg(1) : null)) throw new InvalidOperationException("no call offered (" + _callouts.Current + ")");
                    return _callouts.Describe();
                case "accept": return Check(_callouts.Accept(), "nothing offered");
                case "decline": return Check(_callouts.Decline(), "nothing offered");
                case "cancel": return Check(_callouts.Cancel(), "no call running");

                case "key":
                    var key = (Keys)Enum.Parse(typeof(Keys), arg(1), ignoreCase: true);
                    int hold = a.Length > 2 ? int.Parse(arg(2), CultureInfo.InvariantCulture) : 50;
                    _input.Inject(key, hold);
                    return key + " " + hold + "ms";
                case "wait":
                    _waitUntil = Environment.TickCount + int.Parse(arg(1), CultureInfo.InvariantCulture);
                    return "";

                case "tp":
                    Vector3 to;
                    if (arg(1) == "scene")
                    {
                        CalloutScene scene = _callouts.Scene ?? throw new InvalidOperationException("no scene");
                        float back = a.Length > 2 ? Num(arg(2)) : 15f;
                        Vector3 focus = new Vector3(scene.Focus.X, scene.Focus.Y, scene.Focus.Z);
                        Vector3 away = player.Position - focus;
                        away.Z = 0;
                        to = focus + (away.Length() > 0.1f ? away.Normalized : Vector3.RelativeFront) * back;
                        to = World.GetNextPositionOnStreet(to, true);
                    }
                    else to = new Vector3(Num(arg(1)), Num(arg(2)), Num(arg(3)));
                    Entity mover = player.IsInVehicle() ? (Entity)player.CurrentVehicle : player;
                    mover.Position = to;
                    return Fmt(to);

                case "car":
                    string modelName = a.Length > 1 ? arg(1) : "police3";
                    var model = new Model(modelName);
                    if (!model.IsVehicle) throw new ArgumentException("not a vehicle: " + modelName);
                    if (!model.Request(5000)) throw new InvalidOperationException("model didn't load");   // dev tool: blocking is fine here
                    player.CurrentVehicle?.Delete();
                    Vehicle car = World.CreateVehicle(model, player.Position + player.ForwardVector * 3, player.Heading);
                    model.MarkAsNoLongerNeeded();
                    player.SetIntoVehicle(car, VehicleSeat.Driver);
                    car.MarkAsNoLongerNeeded();
                    return modelName;
                case "lights":
                    Vehicle v = player.CurrentVehicle ?? throw new InvalidOperationException("not in a vehicle");
                    v.IsSirenActive = arg(1) == "on";
                    return v.IsSirenActive ? "lights on" : "lights off";
                case "god":
                    player.IsInvincible = arg(1) == "on";
                    return player.IsInvincible ? "invincible" : "mortal";
                case "hour":
                    World.CurrentTimeOfDay = TimeSpan.FromHours(int.Parse(arg(1), CultureInfo.InvariantCulture));
                    return World.CurrentTimeOfDay.ToString();
            }
            throw new ArgumentException("unknown command '" + a[0] + "'. Commands: " + Help);
        }

        string State()
        {
            Ped p = Game.Player.Character;
            Vehicle? v = p.CurrentVehicle;
            return "duty=" + (_duty.OnDuty ? "on" : "off")
                + " player=" + Fmt(p.Position) + (p.IsDead ? " DEAD" : "")
                + (v != null ? " in=" + v.DisplayName + (v.IsSirenActive ? "+lights" : "") + " " + v.Speed.ToString("0") + "m/s" : " onfoot")
                + " scopes=" + _scopes.OpenCount
                + " callout=" + _callouts.Describe();
        }

        static string Check(bool ok, string why) => ok ? "" : throw new InvalidOperationException(why);
        static float Num(string s) => float.Parse(s, CultureInfo.InvariantCulture);
        static string Fmt(Vector3 v) => v.X.ToString("0.0", CultureInfo.InvariantCulture) + "," + v.Y.ToString("0.0", CultureInfo.InvariantCulture)
            + "," + v.Z.ToString("0.0", CultureInfo.InvariantCulture);
    }
}
