using System;
using System.Collections.Generic;

namespace VividWorld.Core.Rumors
{
    public sealed class TellerRing
    {
        private readonly List<string> _items = new();
        private readonly HashSet<string> _itemSet = new(StringComparer.Ordinal);
        private int _cursor;

        public int Count => _items.Count;
        public IReadOnlyList<string> Ids => _items;

        public int Cursor
        {
            get => _cursor;
            set
            {
                if (value < 0 || _items.Count == 0)
                {
                    _cursor = 0;
                }
                else
                {
                    _cursor = value % _items.Count;
                }
            }
        }

        /// <summary>
        /// 游標現在指著的那一位（輪是空的就回 null）。存檔要存的是他，不是 <see cref="Cursor"/>——
        /// 讀檔時整個輪是從索引重建的，順序與成員都跟遊戲中不同，位置對不回同一個人（§7.3.1、帳本 L-35）。
        /// </summary>
        public string? CurrentId => _items.Count == 0 ? null : _items[_cursor];

        /// <summary>
        /// 把游標移到指定的那一位身上。他不在輪裡（或輪是空的）就不動游標、回傳 false，
        /// 由呼叫端決定退路（讀檔時是退回舊的位置值）。
        /// </summary>
        public bool SetCursorTo(string id)
        {
            if (string.IsNullOrEmpty(id) || _items.Count == 0) return false;
            int index = _items.IndexOf(id);
            if (index < 0) return false;
            _cursor = index;
            return true;
        }

        public bool Contains(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            return _itemSet.Contains(id);
        }

        public void Add(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (_itemSet.Add(id))
            {
                _items.Add(id);
            }
        }

        public bool Remove(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            int index = _items.IndexOf(id);
            if (index < 0) return false;

            _items.RemoveAt(index);
            _itemSet.Remove(id);

            if (_items.Count == 0)
            {
                _cursor = 0;
            }
            else
            {
                if (index < _cursor)
                {
                    _cursor--;
                }
                else if (_cursor >= _items.Count)
                {
                    _cursor %= _items.Count;
                }
            }

            return true;
        }

        public IReadOnlyList<string> TakeBatch(int k)
        {
            if (k <= 0 || _items.Count == 0)
            {
                return Array.Empty<string>();
            }

            int count = Math.Min(k, _items.Count);
            var result = new List<string>(count);

            for (int i = 0; i < count; i++)
            {
                int idx = (_cursor + i) % _items.Count;
                result.Add(_items[idx]);
            }

            _cursor = (_cursor + count) % _items.Count;
            return result;
        }

        public void Rebuild(IEnumerable<string> ids)
        {
            _items.Clear();
            _itemSet.Clear();

            if (ids != null)
            {
                foreach (var id in ids)
                {
                    Add(id);
                }
            }

            if (_items.Count == 0)
            {
                _cursor = 0;
            }
            else
            {
                _cursor %= _items.Count;
            }
        }
    }
}
