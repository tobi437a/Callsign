using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using Callsign.Core.Callouts;
using Callsign.Core.Director;
using Callsign.Core.Duty;
using Callsign.Core.Entities;
using Callsign.Core.Ports;
using Callsign.Core.Settings;
using Callsign.Core.TrafficStops;
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
            "ping | state | (settings: key F6, pick <id>) | expect <text> | expect! <text> | duty on|off | call [situationId] | accept | decline | cancel | key <Key> [holdMs] | wait <ms> "
            + "| traffic [model] | stop | onfoot | pick <menuItemId> | kerb | cam [off] | tp <x> <y> <z> | tp scene [metres] | car [model] | lights on|off | damage | god on|off | hour <0-23> | mark <text>";

        readonly string _dir;
        readonly DutyService _duty;
        readonly CalloutService _callouts;
        readonly PacingDirector _director;
        readonly TrafficStopService _stops;
        readonly SettingsMenu _settings;
        readonly EntityScopes _scopes;
        readonly KeyInput _input;
        readonly ILog _log;
        readonly List<(string Id, string[] Args)> _queue = new List<(string, string[])>();
        int _waitUntil;
        Camera? _cam;
        /// <summary>Points drawn as coloured markers every frame ("sides"), to see where road lookups land.</summary>
        readonly List<(Vector3 At, System.Drawing.Color Color)> _markers = new List<(Vector3, System.Drawing.Color)>();
        int _nextPoll;

        public DevBridgeModule(string dir, DutyService duty, CalloutService callouts, PacingDirector director, TrafficStopService stops, SettingsMenu settings, EntityScopes scopes, KeyInput input, ILog log)
        {
            _settings = settings;
            _dir = dir;
            _duty = duty;
            _callouts = callouts;
            _director = director;
            _stops = stops;
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
            foreach (var m in _markers)
                World.DrawMarker(MarkerType.VerticalCylinder, m.At - new Vector3(0, 0, 1), Vector3.Zero, Vector3.Zero,
                    new Vector3(0.6f, 0.6f, 3f), m.Color);
            int now = Environment.TickCount;
            if (now - _nextPoll >= 0)
            {
                _nextPoll = now + 250;
                ReadCommands();
            }
            if (_queue.Count == 0 || now - _waitUntil < 0) return;

            // A paused game (it pauses on focus loss) would sit in the menu; scripted checks need it running.
            if (Function.Call<bool>(Hash.IS_PAUSE_MENU_ACTIVE)) Function.Call(Hash.SET_FRONTEND_ACTIVE, false);

            (string id, string[] args) = _queue[0];
            _queue.RemoveAt(0);
            string status, text;
            try { (status, text) = ("ok", Run(args)); }
            catch (Exception ex) { (status, text) = ("error", ex.Message); }
            string line = status + " [" + string.Join(" ", args) + "] " + text;
            _log.Info("Dev " + line);
            if (id != "-") File.AppendAllText(ResultsFile, id + " " + line + Environment.NewLine);
        }

        public void Stop() => CamOff();

        void CamOff()
        {
            if (_cam == null) return;
            World.RenderingCamera = null;
            _cam.Delete();
            _cam = null;
        }

        Vector3 SceneFocus()
        {
            CalloutScene scene = _callouts.Scene ?? _stops.Scene ?? throw new InvalidOperationException("no scene");
            return new Vector3(scene.Focus.X, scene.Focus.Y, scene.Focus.Z);
        }

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
                if (parts.Length >= 2) _queue.Add((parts[0], parts.Skip(1).ToArray()));
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
                case "cancel": return Check(_callouts.Cancel() || _stops.Cancel(), "no call or stop running");
                case "onfoot":
                    if (player.IsInVehicle()) player.Task.WarpOutOfVehicle(player.CurrentVehicle);
                    return "";

                case "traffic":   // a car with a driver, a little ahead of the player, driving the same way
                    if (!player.IsInVehicle()) throw new InvalidOperationException("get in a car first");
                    Vehicle own = player.CurrentVehicle;
                    var carModel = new Model(a.Length > 1 ? arg(1) : "asea");
                    var driverModel = new Model("a_m_y_business_01");
                    if (!carModel.Request(5000) || !driverModel.Request(5000)) throw new InvalidOperationException("models didn't load");
                    Vector3 ahead = own.Position + own.ForwardVector * 14f;
                    Vehicle traffic = World.CreateVehicle(carModel, ahead, own.Heading);
                    Ped driver = traffic.CreatePedOnSeat(VehicleSeat.Driver, driverModel);
                    carModel.MarkAsNoLongerNeeded();
                    driverModel.MarkAsNoLongerNeeded();
                    traffic.PlaceOnGround();
                    driver.Task.CruiseWithVehicle(traffic, 8f, VehicleDrivingFlags.DrivingModeStopForVehicles);
                    driver.MarkAsNoLongerNeeded();   // ambient, like real traffic
                    traffic.MarkAsNoLongerNeeded();
                    return traffic.LocalizedName + " " + traffic.Mods.LicensePlate;
                case "pick":   // choose a menu item by id (settings menu first), by pressing the keys a player would (one per tick)
                    var interaction = (_callouts.Scene ?? _stops.Scene)?.FocusInteraction;
                    var menu = _settings.IsOpen ? _settings.Menu
                        : interaction != null && interaction.MenuOpen ? interaction.Menu
                        : throw new InvalidOperationException("no menu open");
                    int target = menu.Items.ToList().FindIndex(i => i.Id == arg(1));
                    if (target < 0) throw new ArgumentException("no item '" + arg(1) + "' in: " + string.Join(",", menu.Items.Select(i => i.Id)));
                    int steps = (target - menu.SelectedIndex + menu.Items.Count) % menu.Items.Count;
                    var keys = Enumerable.Repeat(new[] { "key", "Down" }, steps).Append(new[] { "key", "Return" });
                    _queue.InsertRange(0, keys.Select(k => ("-", k)));
                    return menu.Items[target].Label;
                case "nodes":   // road node flags straight ahead of the player's vehicle, every 10 m
                    Entity from = (Entity?)player.CurrentVehicle ?? player;
                    var sb = new System.Text.StringBuilder();
                    for (int d = 0; d <= 80; d += 10)
                    {
                        Vector3 q = from.Position + from.ForwardVector * d;
                        var dens = new OutputArgument(); var fl = new OutputArgument();
                        bool ok = Function.Call<bool>(Hash.GET_VEHICLE_NODE_PROPERTIES, q.X, q.Y, q.Z, dens, fl);
                        sb.Append(d + "m:" + (ok ? fl.GetResult<int>().ToString() : "-") + " ");
                    }
                    return sb.ToString();
                case "sides":   // where the road-side lookups put a spot 30 m ahead (markers: red boundary, green/blue/yellow by-side -1/0/1, white node)
                    _markers.Clear();
                    if (arg(1) == "off") return "";
                    Entity src = (Entity?)player.CurrentVehicle ?? player;
                    Vector3 ahead2 = src.Position + src.ForwardVector * float.Parse(a.Length > 1 ? arg(1) : "30", CultureInfo.InvariantCulture);
                    var res = new System.Text.StringBuilder();
                    string Lat(Vector3 p) => Vector3.Dot(p - ahead2, src.RightVector).ToString("0.0", CultureInfo.InvariantCulture);
                    var nOut = new OutputArgument(); var hOut = new OutputArgument();
                    if (Function.Call<bool>(Hash.GET_CLOSEST_VEHICLE_NODE_WITH_HEADING, ahead2.X, ahead2.Y, ahead2.Z, nOut, hOut, 1, 3f, 0))
                    {
                        Vector3 n = nOut.GetResult<Vector3>();
                        _markers.Add((n, System.Drawing.Color.White));
                        res.Append("node lat " + Lat(n) + " hdg " + hOut.GetResult<float>().ToString("0") + " (car " + src.Heading.ToString("0") + ") ");
                    }
                    var bOut = new OutputArgument();
                    if (Function.Call<bool>(Hash.GET_ROAD_BOUNDARY_USING_HEADING, ahead2.X, ahead2.Y, ahead2.Z, src.Heading, bOut))
                    {
                        Vector3 b2 = bOut.GetResult<Vector3>();
                        _markers.Add((b2, System.Drawing.Color.Red));
                        res.Append("| boundary lat " + Lat(b2) + " ");
                    }
                    var colors = new[] { System.Drawing.Color.Lime, System.Drawing.Color.Blue, System.Drawing.Color.Yellow };
                    for (int side = -1; side <= 1; side++)
                    {
                        var sOut = new OutputArgument();
                        if (Function.Call<bool>(Hash.GET_POSITION_BY_SIDE_OF_ROAD, ahead2.X, ahead2.Y, ahead2.Z, side, sOut))
                        {
                            Vector3 sp = sOut.GetResult<Vector3>();
                            _markers.Add((sp, colors[side + 1]));
                            res.Append("| side" + side + " lat " + Lat(sp) + " ");
                        }
                        else res.Append("| side" + side + " none ");
                    }
                    return res.ToString();
                case "pullinfo":
                    Vehicle? pv = World.GetClosestVehicle(SceneFocus(), 4f);
                    return GamePed.PullOverDebug + " | car to spot " + (pv == null ? "?" : pv.Position.DistanceTo2D(GamePed.LastPullOverSpot).ToString("0.0", CultureInfo.InvariantCulture)) + "m";
                case "kerb":   // how far the scene's vehicle stands from the right-hand road edge (centre to edge)
                    Vehicle sv = World.GetClosestVehicle(SceneFocus(), 4f) ?? throw new InvalidOperationException("no vehicle at the scene");
                    var edge = new OutputArgument();
                    if (!Function.Call<bool>(Hash.GET_ROAD_BOUNDARY_USING_HEADING, sv.Position.X, sv.Position.Y, sv.Position.Z, sv.Heading, edge))
                        return "no road edge found";
                    Vector3 e = edge.GetResult<Vector3>();
                    float lateral = Vector3.Dot(e - sv.Position, sv.RightVector);
                    return "edge " + lateral.ToString("0.0", CultureInfo.InvariantCulture) + "m to the right"
                        + (Function.Call<bool>(Hash.IS_POINT_ON_ROAD, sv.Position.X, sv.Position.Y, sv.Position.Z, sv) ? "" : " (off road)");
                case "cam":    // "cam" = bird's-eye view over the scene; "cam off" = back to normal
                    CamOff();
                    if (arg(1) == "off") return "";
                    Vector3 f = arg(1) == "me" ? player.Position + player.ForwardVector * 20f : SceneFocus();
                    _cam = World.CreateCamera(f + new Vector3(0, -6, 22), Vector3.Zero, 50f);
                    _cam.PointAt(f);
                    World.RenderingCamera = _cam;
                    return Fmt(f);
                case "stop": return Check(_stops.TryStart(), "no traffic stop started");

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
                        CalloutScene scene = _callouts.Scene ?? _stops.Scene ?? throw new InvalidOperationException("no scene");
                        float back = a.Length > 2 ? Num(arg(2)) : 15f;
                        Vector3 focus = new Vector3(scene.Focus.X, scene.Focus.Y, scene.Focus.Z);
                        Vector3 away = player.Position - focus;
                        away.Z = 0;
                        to = focus + (away.Length() > 0.1f ? away.Normalized : Vector3.RelativeFront) * back;
                        Vehicle? at = World.GetClosestVehicle(focus, 3f);
                        if (player.IsInVehicle() && at != null && at != player.CurrentVehicle)
                        {
                            // Park straight behind the scene's vehicle, same heading.
                            player.CurrentVehicle.Position = at.Position - at.ForwardVector * back;
                            player.CurrentVehicle.Heading = at.Heading;
                            return Fmt(player.CurrentVehicle.Position);
                        }
                        if (player.IsInVehicle()) to = World.GetNextPositionOnStreet(to, true);
                        else if (at != null) to = at.Position - at.RightVector * 1.8f;   // at the driver's door
                        else to.Z = focus.Z;
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
                    // On the nearest road, facing the way traffic drives there.
                    Vector3 road = World.GetNextPositionOnStreetWithHeading(player.Position, out float roadHeading, false);
                    if (road == Vector3.Zero) { road = player.Position + player.ForwardVector * 3; roadHeading = player.Heading; }
                    Vehicle car = World.CreateVehicle(model, road, roadHeading);
                    model.MarkAsNoLongerNeeded();
                    player.SetIntoVehicle(car, VehicleSeat.Driver);
                    car.MarkAsNoLongerNeeded();
                    return modelName;
                case "lights":
                    Vehicle v = player.CurrentVehicle ?? throw new InvalidOperationException("not in a vehicle");
                    v.IsSirenActive = arg(1) == "on";
                    return v.IsSirenActive ? "lights on" : "lights off";
                case "damage":   // wreck the player's vehicle a bit (smoking engine, dented body, a flat tyre)
                    Vehicle wreck = player.CurrentVehicle ?? throw new InvalidOperationException("not in a vehicle");
                    wreck.EngineHealth = 250;
                    wreck.BodyHealth = 300;
                    wreck.Wheels[VehicleWheelBoneId.WheelLeftFront].Burst();
                    return "engine=" + wreck.EngineHealth.ToString("0");
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
                + (v != null ? " in=" + v.DisplayName + (v.IsSirenActive ? "+lights" : "") + " " + v.Speed.ToString("0") + "m/s engine=" + v.EngineHealth.ToString("0") : " onfoot")
                + " scopes=" + _scopes.OpenCount
                + " busy=" + (_duty.Activity ?? "no")
                + " callout=" + _callouts.Describe()
                + " stop=" + _stops.Describe()
                + " director=[" + _director.Describe() + "]"
                + " settings=" + (_settings.IsOpen ? "[" + _settings.Menu.Title + ": " + string.Join(",", _settings.Menu.Items.Select(i => i.Id + (i.Value != null ? "=" + i.Value.Replace("~g~", "").Replace("~r~", "") : ""))) + "]" : "closed");
        }

        static string Check(bool ok, string why) => ok ? "" : throw new InvalidOperationException(why);
        static float Num(string s) => float.Parse(s, CultureInfo.InvariantCulture);
        static string Fmt(Vector3 v) => v.X.ToString("0.0", CultureInfo.InvariantCulture) + "," + v.Y.ToString("0.0", CultureInfo.InvariantCulture)
            + "," + v.Z.ToString("0.0", CultureInfo.InvariantCulture);
    }
}
