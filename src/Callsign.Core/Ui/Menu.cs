using System;
using System.Collections.Generic;
using System.Linq;

namespace Callsign.Core.Ui
{
    /// <summary>One entry in a <see cref="Menu"/>. <paramref name="Id"/> is what the owner reacts to.</summary>
    /// <param name="Value">Shown on the right of the row (a setting's current value), or null.</param>
    public sealed record MenuItem(string Id, string Label, bool Enabled = true, string? Value = null);

    /// <summary>Keys for a menu this frame.</summary>
    public readonly record struct MenuInput(bool Up = false, bool Down = false, bool Select = false, bool Back = false);

    /// <summary>
    /// A list the player moves through and picks from. The owner may replace the items every tick
    /// (e.g. to enable an entry); the selection stays on the same item.
    /// </summary>
    public sealed class Menu
    {
        List<MenuItem> _items = new List<MenuItem>();

        public Menu(string title) { Title = title; }

        public string Title { get; set; }

        /// <summary>The key help under the list, or null for the view's default.</summary>
        public string? Hint { get; set; }

        public IReadOnlyList<MenuItem> Items => _items;

        public int SelectedIndex { get; private set; }

        public MenuItem? Selected => _items.Count == 0 ? null : _items[SelectedIndex];

        public void SetItems(IEnumerable<MenuItem> items)
        {
            string? selectedId = Selected?.Id;
            _items = items.ToList();
            int index = selectedId == null ? -1 : _items.FindIndex(i => i.Id == selectedId);
            SelectedIndex = index >= 0 ? index : Math.Min(SelectedIndex, Math.Max(0, _items.Count - 1));
        }

        /// <summary>Moves the selection (wrapping around). Returns the chosen item's id on Select if it is enabled.</summary>
        public string? Handle(MenuInput input)
        {
            if (_items.Count == 0) return null;
            if (input.Up) SelectedIndex = (SelectedIndex - 1 + _items.Count) % _items.Count;
            if (input.Down) SelectedIndex = (SelectedIndex + 1) % _items.Count;
            MenuItem selected = _items[SelectedIndex];
            return input.Select && selected.Enabled ? selected.Id : null;
        }
    }
}
