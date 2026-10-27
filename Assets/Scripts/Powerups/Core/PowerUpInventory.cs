using System;
using System.Collections.Generic;

namespace Holdem.PowerUps
{
    public enum AddResult { Added, Full }

    /// <summary>A player's power-ups (max 3). Plain C#, no Unity, no poker.</summary>
    public sealed class PowerUpInventory
    {
        public const int MaxSlots = 3;
        private readonly List<PowerUpInstance> _items = new List<PowerUpInstance>(MaxSlots);

        public IReadOnlyList<PowerUpInstance> Items => _items;
        public int Count => _items.Count;
        public bool IsFull => _items.Count >= MaxSlots;

        /// <summary>Adds it if there is a free slot. When Full, the caller must ask the player: discard one held, or discard the new one.</summary>
        public AddResult TryAdd(PowerUpInstance p)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            if (IsFull) return AddResult.Full;
            _items.Add(p);
            return AddResult.Added;
        }

        /// <summary>Full inventory: throw away the held item at index and keep the new one.</summary>
        public PowerUpInstance ReplaceAt(int index, PowerUpInstance incoming)
        {
            Check(index);
            var old = _items[index];
            _items[index] = incoming ?? throw new ArgumentNullException(nameof(incoming));
            return old;
        }

        public PowerUpInstance RemoveAt(int index)
        {
            Check(index);
            var old = _items[index];
            _items.RemoveAt(index);
            return old;
        }

        public void Remove(PowerUpInstance p)
        {
            if (!_items.Remove(p)) throw new InvalidOperationException(p + " is not in the inventory.");
        }

        public void Clear() => _items.Clear();

        /// <summary>Same power-up, same tier, and a higher version exists (Bumba Card I + I = II).</summary>
        public static bool CanMerge(PowerUpInstance a, PowerUpInstance b) =>
            a != null && b != null && a != b && a.Definition == b.Definition && a.Tier == b.Tier && a.Definition.NextTier(a.Tier).HasValue;

        /// <summary>Held item at index that would merge with 'incoming', or -1. Merging never needs a free slot.</summary>
        public int FindMergePartner(PowerUpInstance incoming)
        {
            for (int i = 0; i < _items.Count; i++)
                if (CanMerge(_items[i], incoming)) return i;
            return -1;
        }

        /// <summary>Merges two held items (by index) into one copy a tier up. Optional: the player chooses to do it.</summary>
        public PowerUpInstance MergeHeld(int i, int j)
        {
            Check(i); Check(j);
            if (i == j || !CanMerge(_items[i], _items[j])) throw new InvalidOperationException("These two cannot be merged.");
            var up = new PowerUpInstance(_items[i].Definition, _items[i].Definition.NextTier(_items[i].Tier).Value);
            int hi = Math.Max(i, j), lo = Math.Min(i, j);
            _items.RemoveAt(hi);
            _items[lo] = up;
            return up;
        }

        /// <summary>Merges a NEW power-up into the held item at index (no free slot needed). Returns the upgraded copy.</summary>
        public PowerUpInstance MergeIncoming(int heldIndex, PowerUpInstance incoming)
        {
            Check(heldIndex);
            if (!CanMerge(_items[heldIndex], incoming)) throw new InvalidOperationException("These two cannot be merged.");
            var up = new PowerUpInstance(incoming.Definition, incoming.Definition.NextTier(incoming.Tier).Value);
            _items[heldIndex] = up;
            return up;
        }

        private void Check(int i)
        {
            if (i < 0 || i >= _items.Count) throw new ArgumentOutOfRangeException(nameof(i));
        }
    }
}
