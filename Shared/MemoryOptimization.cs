using System.Collections.Concurrent;

namespace Shared
{
    // Memory pooling for ReelSet objects to reduce GC pressure
    public class ReelSetPool
    {
        private static readonly ConcurrentQueue<ReelSet> _pool = new();
        private static readonly object _lock = new();
        private static int _totalCreated = 0;
        private static int _totalReused = 0;
        private static int _maxPoolSize = 1000;
        
        public static ReelSet Rent()
        {
            if (_pool.TryDequeue(out var reelSet))
            {
                Interlocked.Increment(ref _totalReused);
                return reelSet;
            }
            
            Interlocked.Increment(ref _totalCreated);
            return new ReelSet();
        }
        
        public static void Return(ReelSet reelSet)
        {
            if (reelSet == null) return;
            
            // Clear the reel set for reuse
            reelSet.Name = null;
            reelSet.Reels?.Clear();
            reelSet.ExpectedRtp = 0;
            reelSet.EstimatedHitRate = 0;
            reelSet.RtpWeight = 0;
            reelSet.HitWeight = 0;
            reelSet.CombinedWeight = 0;
            
            // Add to pool if not full
            if (_pool.Count < _maxPoolSize)
            {
                _pool.Enqueue(reelSet);
            }
        }
        
        public static void SetMaxPoolSize(int maxSize)
        {
            _maxPoolSize = maxSize;
        }
        
        public static (int totalCreated, int totalReused, int poolSize) GetStats()
        {
            return (_totalCreated, _totalReused, _pool.Count);
        }
        
        public static void Clear()
        {
            lock (_lock)
            {
                while (_pool.TryDequeue(out _)) { }
            }
        }
    }

    // Memory monitoring service
    public static class MemoryMonitor
    {
        private static readonly List<MemorySnapshot> _snapshots = new();
        private static readonly object _lock = new();
        
        public static void TakeSnapshot(string label)
        {
            var snapshot = new MemorySnapshot
            {
                Timestamp = DateTime.UtcNow,
                Label = label,
                TotalMemory = GC.GetTotalMemory(false),
                Gen0Collections = GC.CollectionCount(0),
                Gen1Collections = GC.CollectionCount(1),
                Gen2Collections = GC.CollectionCount(2)
            };
            
            lock (_lock)
            {
                _snapshots.Add(snapshot);
                
                // Keep only last 100 snapshots
                if (_snapshots.Count > 100)
                {
                    _snapshots.RemoveAt(0);
                }
            }
        }
        
        public static List<MemorySnapshot> GetSnapshots()
        {
            lock (_lock)
            {
                return _snapshots.ToList();
            }
        }
        
        public static MemorySnapshot GetLatestSnapshot()
        {
            lock (_lock)
            {
                return _snapshots.Count > 0 ? _snapshots[_snapshots.Count - 1] : null;
            }
        }
        
        public static void ClearSnapshots()
        {
            lock (_lock)
            {
                _snapshots.Clear();
            }
        }
    }
    
    public class MemorySnapshot
    {
        public DateTime Timestamp { get; set; }
        public string Label { get; set; }
        public long TotalMemory { get; set; }
        public int Gen0Collections { get; set; }
        public int Gen1Collections { get; set; }
        public int Gen2Collections { get; set; }
        
        public double TotalMemoryMB => TotalMemory / (1024.0 * 1024.0);
    }
}
