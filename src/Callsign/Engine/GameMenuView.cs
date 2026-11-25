using System.Drawing;
using GTA;
using GTA.UI;
using Callsign.Core.Ports;
using Menu = Callsign.Core.Ui.Menu;

namespace Callsign.Engine
{
    /// <summary>Draws a <see cref="Menu"/> as a simple list on the left of the screen (1280x720 layout units).</summary>
    internal sealed class GameMenuView : IMenuView
    {
        const float X = 30, Y = 170, Width = 400, TitleHeight = 36, RowHeight = 30, Padding = 10;

        static readonly Color TitleBack = Color.FromArgb(230, 20, 60, 140);
        static readonly Color RowBack = Color.FromArgb(190, 0, 0, 0);
        static readonly Color SelectedBack = Color.FromArgb(230, 235, 235, 235);
        static readonly Color Text = Color.White;
        static readonly Color SelectedText = Color.Black;
        static readonly Color DisabledText = Color.FromArgb(255, 140, 140, 140);

        // Keys the menu uses that the game would otherwise act on (phone, weapon wheel, ...).
        static readonly Control[] Blocked =
        {
            Control.Phone, Control.PhoneUp, Control.PhoneDown, Control.PhoneSelect, Control.PhoneCancel,
            Control.FrontendUp, Control.FrontendDown, Control.FrontendAccept, Control.FrontendCancel,
            Control.SelectWeapon, Control.NextWeapon, Control.PrevWeapon,
            Control.SelectNextWeapon, Control.SelectPrevWeapon, Control.WeaponWheelNext, Control.WeaponWheelPrev,
        };

        public void Draw(Menu menu)
        {
            foreach (Control control in Blocked) Game.DisableControlThisFrame(control);

            float y = Y;
            new ContainerElement(new PointF(X, y), new SizeF(Width, TitleHeight), TitleBack).Draw();
            new TextElement(menu.Title, new PointF(X + Padding, y + 4), 0.55f, Text, GTA.UI.Font.ChaletLondon).Draw();
            y += TitleHeight;

            for (int i = 0; i < menu.Items.Count; i++)
            {
                var item = menu.Items[i];
                bool selected = i == menu.SelectedIndex;
                new ContainerElement(new PointF(X, y), new SizeF(Width, RowHeight), selected ? SelectedBack : RowBack).Draw();
                Color color = !item.Enabled ? DisabledText : selected ? SelectedText : Text;
                // Colour codes in labels would fight the selected row's dark text, so drop them there.
                string label = selected ? StripColours(item.Label) : item.Label;
                new TextElement(label, new PointF(X + Padding, y + 4), 0.35f, color, GTA.UI.Font.ChaletLondon).Draw();
                y += RowHeight;
            }

            new ContainerElement(new PointF(X, y), new SizeF(Width, RowHeight - 6), RowBack).Draw();
            new TextElement("Arrows: move    Enter: choose    E / Backspace: close", new PointF(X + Padding, y + 3),
                0.28f, DisabledText, GTA.UI.Font.ChaletLondon).Draw();
        }

        static string StripColours(string text) => System.Text.RegularExpressions.Regex.Replace(text, "~[a-z]~", "");
    }
}
