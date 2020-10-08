﻿using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using GLTFRevitExport.Properties;

namespace GLTFRevitExport.GLTF.Types {
    /// <summary>
    /// 
    /// </summary>
    public class glTFNodes : IEnumerable<glTFNode> {
        private List<glTFNode> _items = new List<glTFNode>();
        private Stack<glTFNode> _openItems = new Stack<glTFNode>();

        public glTFNode Peek() => _openItems.Count > 0 ? _openItems.Peek() : null;

        public bool IsOpen() => _openItems.Count > 0;

        public uint Append(glTFNode item) {
            _items.Add(item);
            uint itemIndex = (uint)_items.Count - 1;
            glTFNode openItem = Peek();
            if (openItem is glTFNode parent) {
                if (parent.Children is null)
                    parent.Children = new HashSet<uint>();
                parent.Children.Add(itemIndex);
            }
            return itemIndex;
        }

        public void Open(uint idx) {
            if (_items.ElementAtOrDefault((int)idx) is glTFNode item)
                _openItems.Push(item);
            else
                throw new Exception(StringLib.ItemNotExist);
        }

        public void Close() {
            if (_openItems.Count > 0)
                _openItems.Pop();
        }

        public IEnumerator<glTFNode> GetEnumerator() {
            return new glTFNodesEnum(_items);
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return GetEnumerator();
        }
    }

    public class glTFNodesEnum : IEnumerator<glTFNode> {
        private glTFNode[] _items;
        private int position = -1;

        public glTFNodesEnum(List<glTFNode> items) => _items = items.ToArray();

        public glTFNode Current {
            get {
                try {
                    return _items[position];
                }
                catch (IndexOutOfRangeException) {
                    throw new InvalidOperationException();
                }
            }
        }

        object IEnumerator.Current => Current;

        public void Dispose() {
            _items = null;
        }

        public bool MoveNext() {
            position++;
            return (position < _items.Length);
        }

        public void Reset() {
            position = -1;
        }
    }
}