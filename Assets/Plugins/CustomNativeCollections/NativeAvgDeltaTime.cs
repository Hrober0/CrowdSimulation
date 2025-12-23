using System;
using Unity.Collections;

namespace CustomNativeCollections
{
    
    public struct NativeAvgDeltaTime : IDisposable
    {
        private const int KEEP_FRAMES = 60;
        private const float KEEP_FRAMES_INV = 1f / 60;
        
        private NativeQueue<float> _deltaTimes;
        private float _sumOfDeltaTimes;

        public NativeAvgDeltaTime(Allocator allocator, float startDeltaTime = 1f / 60)
        {
            _sumOfDeltaTimes = 0;
            _deltaTimes = new NativeQueue<float>(allocator);
            for (int i = 0; i < KEEP_FRAMES; i++)
            {
                _deltaTimes.Enqueue(startDeltaTime);
                _sumOfDeltaTimes += startDeltaTime;
            }
        }

        public void Update(float currentDeltaTime)
        {
            var oldestDT = _deltaTimes.Dequeue();
            _sumOfDeltaTimes -= oldestDT;
            
            _deltaTimes.Enqueue(currentDeltaTime);
            _sumOfDeltaTimes += currentDeltaTime;
        }
        
        public float EverageDeltaTime => _sumOfDeltaTimes * KEEP_FRAMES_INV;
        
        public void Dispose()
        {
            _deltaTimes.Dispose();
        }
    }
}