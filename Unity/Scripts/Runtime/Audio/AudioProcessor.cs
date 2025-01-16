using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace SK.Libretro.Unity
{
    [RequireComponent(typeof(AudioSource)), DisallowMultipleComponent]
    public sealed class AudioProcessor : MonoBehaviour, IAudioProcessor
    {
        private const int AUDIO_BUFFER_SIZE = 65535;

        private AudioSource _audioSource;
        private int _inputSampleRate;
        private int _outputSampleRate;

        private RingBuffer<float> _circularBuffer;

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (_circularBuffer.IsEmpty)
                return;

            for (int i = 0; i < data.Length; ++i)
                data[i] = _circularBuffer.Dequeue();
        }

        private void OnDestroy() => Dispose();

        public void Init(int sampleRate) => MainThreadDispatcher.Enqueue(() =>
        {
            if (_audioSource)
                _audioSource.Stop();

            if (_circularBuffer != null)
                _circularBuffer.Dispose();
            _circularBuffer = new RingBuffer<float>(AUDIO_BUFFER_SIZE, Allocator.Persistent);

            _inputSampleRate = sampleRate;
            _outputSampleRate = AudioSettings.outputSampleRate;

            if (!_audioSource)
                _audioSource = GetComponent<AudioSource>();
            _audioSource.Play();
        });

        public void DeInit() => MainThreadDispatcher.Enqueue(() =>
        {
            if (_audioSource)
                _audioSource.Stop();

            if (_circularBuffer != null)
                _circularBuffer.Dispose();
        });

        public void Dispose() => MainThreadDispatcher.Enqueue(() =>
        {
            if (_audioSource)
                _audioSource.Stop();

            if (_circularBuffer != null)
                _circularBuffer.Dispose();
        });

        public void ProcessSample(short left, short right) => MainThreadDispatcher.Enqueue(() =>
        {
            float ratio                 = (float)_outputSampleRate / _inputSampleRate;
            int sourceSamplesCount      = 2;
            int destinationSamplesCount = (int)(sourceSamplesCount * ratio);
            for (int i = 0; i < destinationSamplesCount; i++)
            {
                float sampleIndex = i / ratio;
                int sampleIndex1 = (int)math.floor(sampleIndex);
                if (sampleIndex1 > sourceSamplesCount - 1)
                    sampleIndex1 = sourceSamplesCount - 1;
                int sampleIndex2 = (int)math.ceil(sampleIndex);
                if (sampleIndex2 > sourceSamplesCount - 1)
                    sampleIndex2 = sourceSamplesCount - 1;
                float interpolationFactor = sampleIndex - sampleIndex1;
                _circularBuffer.Enqueue(math.lerp(left  * AudioHandler.NORMALIZED_GAIN,
                                                  right * AudioHandler.NORMALIZED_GAIN,
                                                  interpolationFactor));
            }
        });

        public unsafe void ProcessSampleBatch(IntPtr data, nuint frames) => MainThreadDispatcher.Enqueue(() =>
        {
            short* sourceSamples        = (short*)data;
            float ratio                 = (float)_outputSampleRate / _inputSampleRate;
            int sourceSamplesCount      = (int)frames * 2;
            int destinationSamplesCount = (int)(sourceSamplesCount * ratio);
            for (int i = 0; i < destinationSamplesCount; i++)
            {
                float sampleIndex = i / ratio;
                int sampleIndex1 = (int)math.floor(sampleIndex);
                if (sampleIndex1 > sourceSamplesCount - 1)
                    sampleIndex1 = sourceSamplesCount - 1;
                int sampleIndex2 = (int)math.ceil(sampleIndex);
                if (sampleIndex2 > sourceSamplesCount - 1)
                    sampleIndex2 = sourceSamplesCount - 1;
                float interpolationFactor = sampleIndex - sampleIndex1;
                _circularBuffer.Enqueue(math.lerp(sourceSamples[sampleIndex1] * AudioHandler.NORMALIZED_GAIN,
                                                  sourceSamples[sampleIndex2] * AudioHandler.NORMALIZED_GAIN,
                                                  interpolationFactor));
            }
        });

        // Custom RingBuffer implementation
        public class RingBuffer<T> where T : struct
        {
            private NativeArray<T> _buffer;
            private int _head;
            private int _tail;
            private int _size;
            private int _capacity;

            public RingBuffer(int capacity, Allocator allocator)
            {
                _capacity = capacity;
                _size = 0;
                _head = 0;
                _tail = 0;
                _buffer = new NativeArray<T>(capacity, allocator);
            }

            public bool IsEmpty => _size == 0;
            public bool IsFull => _size == _capacity;
            public int Length => _size;

            public void Enqueue(T item)
            {
                if (IsFull)
                    throw new InvalidOperationException("Buffer is full.");
                
                _buffer[_tail] = item;
                _tail = (_tail + 1) % _capacity;
                _size++;
            }

            public T Dequeue()
            {
                if (IsEmpty)
                    throw new InvalidOperationException("Buffer is empty.");
                
                T item = _buffer[_head];
                _head = (_head + 1) % _capacity;
                _size--;
                return item;
            }

            public void Dispose()
            {
                if (_buffer.IsCreated)
                    _buffer.Dispose();
            }
        }
    }
}
