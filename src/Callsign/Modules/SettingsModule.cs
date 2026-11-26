using System;
using Callsign.Core.Ports;
using Callsign.Core.Settings;
using Callsign.Core.Ui;
using Callsign.Engine;
using Keys = System.Windows.Forms.Keys;

namespace Callsign.Modules
{
    /// <summary>
    /// Opens and draws the settings menu. Ticks before the gameplay modules: while the menu is open
    /// it takes the menu keys (and E) for itself, so they don't also drive a suspect interaction.
    /// </summary>
    internal sealed class SettingsModule : IModule
    {
        const Keys MenuKey = Keys.F6;
        static readonly Keys[] OwnedKeys = { Keys.Up, Keys.Down, Keys.Enter, Keys.Back, Keys.E };

        readonly SettingsMenu _menu;
        readonly IMenuView _view;
        readonly KeyInput _input;
        readonly Func<bool> _otherMenuOpen;

        /// <param name="otherMenuOpen">True while another menu (a suspect interaction) is on screen: the settings wait for it to close.</param>
        public SettingsModule(SettingsMenu menu, IMenuView view, KeyInput input, Func<bool> otherMenuOpen)
        {
            _menu = menu;
            _view = view;
            _input = input;
            _otherMenuOpen = otherMenuOpen;
        }

        public string Name => "Settings";

        public void Start() { }

        public void Tick()
        {
            if (_input.WasPressed(MenuKey))
            {
                if (_menu.IsOpen) _menu.Close();
                else if (_otherMenuOpen()) GTA.UI.Screen.ShowSubtitle("Close the open menu first", 2000);
                else _menu.Open();
            }
            if (!_menu.IsOpen) return;

            _menu.Tick(new MenuInput(_input.WasPressed(Keys.Up), _input.WasPressed(Keys.Down),
                _input.WasPressed(Keys.Enter), _input.WasPressed(Keys.Back)));
            foreach (Keys key in OwnedKeys) _input.Consume(key);
            if (_menu.IsOpen) _view.Draw(_menu.Menu);
        }

        public void Stop() => _menu.Close();
    }
}
