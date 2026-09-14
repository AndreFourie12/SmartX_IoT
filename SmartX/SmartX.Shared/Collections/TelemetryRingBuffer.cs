using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace SmartX.Shared.Collections
{
    /// <summary>
    /// Fixed-capacity circular buffer purpose-built for telemetry history
    /// Writes and the oldest-firstenumeration used for the dashboard timeline are both O(1)/O(n)-single-pass with no element shifting
    /// once Capacity is reached, the next Add simply overwrites the oldest slot.
    /// </summary>
    public sealed class TelemetryRingBuffer<T> : IReadOnlyCollection<T>
    {
        private readonly T[] _buffer;
        private int _start;
        private int _count;


        /// <summary>
        /// Method to prevent capacity from being negative
        /// Only returns postive readings
        /// </summary>
        public TelemetryRingBuffer(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");

            _buffer = new T[capacity];
        }
        //----------------------------------------------------------//

        public int Capacity => _buffer.Length;
        public int Count => _count;


        /// <summary>
        /// Writes an item into the next slot and grows the count until the buffer is full.
        /// Once at capacity it advances the start index instead, so the oldest item is overwritten.
        /// </summary>
        public void Add(T item)
        {
            int writeIndex = (_start + _count) % _buffer.Length;
            _buffer[writeIndex] = item;

            if(_count < _buffer.Length)
            {
                _count++;
            }
            else
            {
                _start = (_start + 1) % _buffer.Length;
            }
        }
        //----------------------------------------------------------//

        public T? Latest => _count == 0 ? default : _buffer[(_start + _count - 1) % _buffer.Length];


        /// <summary>
        /// Enumerates from oldest to newest.
        /// </summary>
        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < _count; i++)
            {
                yield return _buffer[(_start + i) % _buffer.Length];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

//------------------------------------------- End Of File -------------------------------------------//