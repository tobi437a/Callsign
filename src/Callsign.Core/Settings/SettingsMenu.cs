using System.Collections.Generic;
using System.Linq;
using Callsign.Core.Ui;

namespace Callsign.Core.Settings
{
    /// <summary>
    /// One screen of the settings menu. Items are rebuilt every tick, so they can show current values.
    /// To add settings: add items to a page, or add a page to the list the bootstrap gives <see cref="SettingsMenu"/>.
    /// </summary>
    public interface ISettingsPage
    {
        /// <summary>Shown in the main list and as the page's title.</summary>
        string Title { get; }

        IEnumerable<MenuItem> Items();

        /// <summary>The player picked the item with this id.</summary>
        void Choose(string id);
    }

    /// <summary>The settings menu: a main list of pages. Backspace goes back to the main list, then closes.</summary>
    public sealed class SettingsMenu
    {
        public const string CloseId = "close";
        const string MainTitle = "Callsign settings";
        const string Hint = "Arrows: move    Enter: choose    Backspace: back";

        readonly IReadOnlyList<ISettingsPage> _pages;
        ISettingsPage? _page;

        public SettingsMenu(IEnumerable<ISettingsPage> pages)
        {
            _pages = pages.ToList();
            Menu = new Menu(MainTitle) { Hint = Hint };
        }

        public Menu Menu { get; }

        public bool IsOpen { get; private set; }

        /// <summary>The open page, or null on the main list.</summary>
        public ISettingsPage? Page => _page;

        public void Open()
        {
            IsOpen = true;
            Show(null);
        }

        public void Close()
        {
            IsOpen = false;
            _page = null;
        }

        public void Tick(MenuInput input)
        {
            if (!IsOpen) return;
            Refresh();
            if (input.Back)
            {
                if (_page == null) Close();
                else Show(null);
                return;
            }
            string? id = Menu.Handle(input);
            if (id == null) return;
            if (_page != null) _page.Choose(id);
            else if (id == CloseId) Close();
            else if (int.TryParse(id, out int index) && index >= 0 && index < _pages.Count) Show(_pages[index]);
            if (IsOpen) Refresh();
        }

        void Show(ISettingsPage? page)
        {
            _page = page;
            Menu.Title = page?.Title ?? MainTitle;
            Menu.SetItems(new MenuItem[0]);   // a page always opens on its first item
            Refresh();
        }

        void Refresh()
        {
            Menu.SetItems(_page != null
                ? _page.Items()
                : _pages.Select((p, i) => new MenuItem(i.ToString(), p.Title, Value: ">"))
                    .Append(new MenuItem(CloseId, "Close")));
        }
    }
}
