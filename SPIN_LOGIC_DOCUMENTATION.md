# BloodSuckers Slot - Spin Logic Documentation

## Overview
This document describes the intelligent spin logic system implemented in `SpinLogicHelper.cs` that dynamically selects reel sets based on current performance metrics to maintain target RTP and hit rate values.

## System Architecture

### Core Components
- **SpinLogicHelper**: Main spin execution engine
- **SlotEvaluationService**: Win evaluation and bonus logic
- **GameConfig**: Configuration-driven parameters
- **ReelSet**: Data structure containing reel data and performance metrics

### Key Features
- ✅ **Intelligent Reel Set Selection** - Based on actual ExpectedRtp and EstimatedHitRate values
- ✅ **Volatility Management** - Real-time volatility calculation and control
- ✅ **Free Spin System** - Proper triggering and state management
- ✅ **Performance Targeting** - Active correction toward RTP and hit rate targets
- ✅ **Configuration-Driven** - All parameters configurable via appsettings.json

## Spin Execution Flow

### 1. Initialization & State Analysis
```csharp
bool isFreeSpin = _freeSpinsRemaining > 0;
double currentRtpBeforeSpin = GetActualRtp();
double currentHitRateBeforeSpin = GetActualHitRate();
double currentVolatility = CalculateCurrentVolatility();
```

**What Happens:**
- Determines if this is a free spin
- Calculates current RTP from all previous spins
- Calculates current hit rate from all previous spins
- Calculates current volatility based on recent win patterns

### 2. Free Spin State Management
```csharp
if (isFreeSpin)
{
    _freeSpinsRemaining--;
    Console.WriteLine($"🎰 FREE SPIN EXECUTED! Remaining: {_freeSpinsRemaining}");
}
```

**What Happens:**
- Decrements remaining free spins
- Logs free spin execution
- Free spins use MidRtp reel sets only

### 3. Reel Set Weighting
```csharp
foreach (var reelSet in reelSets)
{
    // RTP weight: closer to target = higher weight
    double rtpWeight = CalculateRtpWeight(reelSet.ExpectedRtp, config.RtpTarget, currentRtpBeforeSpin);
    
    // Hit Rate weight: closer to target = higher weight  
    double hitRateWeight = CalculateHitRateWeight(reelSet.EstimatedHitRate, config.TargetHitRate, currentHitRateBeforeSpin);
    
    // Volatility weight: consider the reel set's impact on current volatility
    double volatilityWeight = CalculateVolatilityWeight(reelSet, currentVolatility, config);
    
    // Combined weight using configurable multipliers
    reelSet.CombinedWeight = (rtpWeight * config.RtpWeightMultiplier) + 
                            (hitRateWeight * config.HitRateWeightMultiplier) + 
                            (volatilityWeight * config.VolatilityWeightMultiplier);
}
```

**Weight Calculation Logic:**
- **RTP Weight**: `1.0 / (|ExpectedRtp - TargetRtp| + 0.01)`
- **Hit Rate Weight**: `1.0 / (|EstimatedHitRate - TargetHitRate| + 0.01)`
- **Volatility Weight**: `1.0 / (estimatedVolatility + 0.01)`

**Default Multipliers:**
- RTP: 30% (reduced from 50% to prevent dominance)
- Hit Rate: 40% (increased for better balance)
- Volatility: 30% (increased for stability)

### 4. Intelligent Reel Set Selection

#### Selection Criteria
```csharp
bool needHigherRtp = currentRtp < config.RtpTarget * 0.9;        // Below 90% of target
bool needLowerRtp = currentRtp > config.RtpTarget * 1.1;         // Above 110% of target
bool needHigherHitRate = currentHitRate < config.TargetHitRate * 0.8;  // Below 80% of target
bool needLowerHitRate = currentHitRate > config.TargetHitRate * 1.1;   // Above 110% of target
bool needLowerVolatility = currentVolatility > config.VolatilityThreshold; // Above 1.5 threshold
```

#### RTP Selection Logic
**When RTP is too low (< 90% of target):**
```csharp
var rtpCandidates = reelSets
    .Where(r => r.ExpectedRtp >= config.RtpTarget * 0.85 && r.ExpectedRtp <= config.RtpTarget * 1.05) // 74.8% to 92.4% of target
    .OrderBy(r => Math.Abs(r.ExpectedRtp - config.RtpTarget)) // Closest to target, not highest
    .Take(config.MaxCandidatesPerCategory) // Use configurable limit
    .ToList();
```

**When RTP is too high (> 110% of target):**
```csharp
var lowRtpCandidates = reelSets
    .Where(r => r.ExpectedRtp >= config.RtpTarget * 0.7 && r.ExpectedRtp <= config.RtpTarget * 0.95) // 61.6% to 83.6% of target
    .OrderByDescending(r => r.ExpectedRtp) // Prefer higher ones in this range
    .Take(config.MaxCandidatesPerCategory)
    .ToList();
```

#### Hit Rate Selection Logic
**When Hit Rate is too low (< 80% of target):**
```csharp
var hitRateCandidates = reelSets
    .Where(r => r.EstimatedHitRate >= config.TargetHitRate * 0.8 && r.EstimatedHitRate <= config.TargetHitRate * 1.1) // 36% to 49.5% of target
    .OrderBy(r => Math.Abs(r.EstimatedHitRate - config.TargetHitRate)) // Closest to target, not highest
    .Take(config.MaxCandidatesPerCategory)
    .ToList();
```

**When Hit Rate is too high (> 110% of target):**
```csharp
var lowHitRateCandidates = reelSets
    .Where(r => r.EstimatedHitRate >= config.TargetHitRate * 0.6 && r.EstimatedHitRate <= config.TargetHitRate * 0.9) // 27% to 40.5% of target
    .OrderByDescending(r => r.EstimatedHitRate) // Prefer higher ones in this range
    .Take(config.MaxCandidatesPerCategory)
    .ToList();
```

### 5. Volatility Management

#### Volatility Calculation
```csharp
private static double CalculateCurrentVolatility()
{
    if (_recentWins.Count < 10) return 1.0; // Default volatility if not enough data

    var wins = _recentWins.ToArray();
    var mean = wins.Average();
    var variance = wins.Select(w => Math.Pow(w - mean, 2)).Average();
    var standardDeviation = Math.Sqrt(variance);
    
    // Normalize volatility (0 = very stable, 3+ = very volatile)
    return standardDeviation / (mean > 0 ? mean : 1.0);
}
```

**Volatility Interpretation:**
- **0.0 - 1.0**: Very stable, consistent wins
- **1.0 - 2.0**: Medium volatility, some variation
- **2.0+**: High volatility, large swings

#### Volatility Tracking
```csharp
private static void UpdateVolatilityTracking(double winAmount, double betAmount)
{
    // Normalize win amount by bet amount for consistent volatility calculation
    double normalizedWin = betAmount > 0 ? winAmount / betAmount : winAmount;
    
    _recentWins.Add(normalizedWin);
    
    // Keep only the most recent wins (sliding window)
    if (_recentWins.Count > _maxRecentWins)
    {
        _recentWins.RemoveAt(0);
    }
}
```

**Configuration:**
- **MaxRecentWinsForVolatility**: 100 (default)
- **VolatilityThreshold**: 1.5 (triggers stability measures)

### 6. Free Spin System

#### Free Spin Triggering
```csharp
if (scatterCount >= 3 && !isFreeSpin)
{
    // Award free spins based on scatter count (same logic as original SlotEngine)
    switch (scatterCount)
    {
        case 3: freeSpinsAwarded = 10; break;
        case 4: freeSpinsAwarded = 10; break;
        case 5: freeSpinsAwarded = 10; break;
    }
    
    if (freeSpinsAwarded > 0)
    {
        _freeSpinsRemaining += freeSpinsAwarded;
        _freeSpinsAwarded += freeSpinsAwarded;
        _totalFreeSpinsAwarded += freeSpinsAwarded;
    }
}
```

**Free Spin Rules:**
- **3+ Scatters**: Award 10 free spins
- **Free Spin Reel Sets**: Only MidRtp reel sets used
- **Win Multiplier**: All wins tripled during free spins
- **State Tracking**: Proper countdown and session tracking

### 7. Win Evaluation & Processing

#### Win Types
1. **Line Wins**: Standard payline combinations
2. **Wild Wins**: Wild symbol substitutions
3. **Scatter Wins**: Scatter symbol payouts
4. **Bonus Wins**: Bonus game payouts

#### Duplicate Win Prevention
```csharp
// Remove line wins for symbols that were already processed by wild evaluation on the same payline
var wildProcessedSymbolsOnPaylines = new Dictionary<string, Dictionary<int, double>>();
// ... logic to prevent double-counting wins
```

#### Win Caps
```csharp
double maxMultiplier = 75.0; // Cap win to 75x of bet
totalWin = Math.Min(totalWin, betAmount * maxMultiplier);
```

## Configuration Parameters

### appsettings.json
```json
{
  "GameConfig": {
    "RtpTarget": 0.88,                    // Target RTP (88%)
    "TargetHitRate": 0.45,                // Target hit rate (45%)
    
    "VolatilityThreshold": 1.5,           // Volatility threshold for stability measures
    "MaxRecentWinsForVolatility": 100,    // Number of recent wins to track
    
    "RtpWeightMultiplier": 0.3,           // RTP weight in combined score (30%)
    "HitRateWeightMultiplier": 0.4,       // Hit rate weight in combined score (40%)
    "VolatilityWeightMultiplier": 0.3,    // Volatility weight in combined score (30%)
    
    "MaxCandidatesPerCategory": 20        // Maximum candidates per selection category
  }
}
```

## Performance Targeting Logic

### RTP Targeting
- **Below 90% of target**: Select reel sets closer to 88% (74.8% to 92.4%)
- **Above 110% of target**: Actively select lower RTP reel sets (61.6% to 83.6%)
- **Between 90-110%**: Use balanced selection

### Hit Rate Targeting
- **Below 80% of target**: Select reel sets closer to 45% (36% to 49.5%)
- **Above 110% of target**: Actively select lower hit rate reel sets (27% to 40.5%)
- **Between 80-110%**: Use balanced selection

### Volatility Control
- **Above 1.5 threshold**: Prioritize stable reel sets
- **Below 1.5 threshold**: Allow natural variation

## Debug Logging

### Selection Information
```
🎯 REEL SET SELECTION: Current RTP: 124.82%, Target: 88.00%
🎯 REEL SET SELECTION: Current Hit Rate: 47.39%, Target: 45.00%
🎯 REEL SET SELECTION: Chosen: HighRtpSet_15 | Expected RTP: 95.20% | Estimated Hit Rate: 42.30%
```

### Free Spin Information
```
🎰 FREE SPINS TRIGGERED! SYM0 x3 => +10 Free Spins
🎰 FREE SPIN EXECUTED! Remaining: 9
```

### Performance Metrics
```
ReelSet: HighRtpSet_15 | Expected RTP: 0.9520 | Estimated Hit Rate: 0.4230
Line Win: 150, Wild Win: 0, Scatter Win: 0
Total Spin Win: 150 | Bet: 25 | Actual RTP: 1.2482
[HIT RATE] 127 / 268 spins (47.39%)
[VOLATILITY] Current: 1.2345 | Recent wins: 100
```

## Expected Behavior

### Immediate (50-100 spins)
- **RTP**: Should start declining toward 88% target
- **Hit Rate**: Should stabilize around 45% target
- **Selection**: More conservative, closer to targets

### Medium Term (100-200 spins)
- **RTP**: Should approach 88% target
- **Hit Rate**: Should maintain 45% target
- **Performance**: Stable, predictable

### Long Term (200+ spins)
- **Both metrics**: Stay close to targets
- **Volatility**: Reduced swings
- **Balance**: Consistent performance

## Troubleshooting

### High RTP Issues
- **Symptom**: RTP > 110% of target
- **Cause**: System selecting too many high RTP reel sets
- **Solution**: Active correction logic will select lower RTP reel sets

### Low Hit Rate Issues
- **Symptom**: Hit Rate < 80% of target
- **Cause**: System selecting too many low hit rate reel sets
- **Solution**: Active correction logic will select higher hit rate reel sets

### Volatility Issues
- **Symptom**: High volatility (> 2.0)
- **Cause**: Large swings in win amounts
- **Solution**: System will prioritize stable reel sets

## System Benefits

1. **Data-Driven Selection**: Uses actual ExpectedRtp and EstimatedHitRate values, not tag names
2. **Active Correction**: Automatically adjusts selection based on current performance
3. **Balanced Weights**: No single metric dominates selection
4. **Volatility Control**: Maintains game stability
5. **Configuration-Driven**: All parameters adjustable without code changes
6. **Real-Time Adaptation**: Responds to performance changes immediately

## Performance Monitoring

### Key Metrics to Watch
- **Current RTP vs Target**: Should converge toward 88%
- **Current Hit Rate vs Target**: Should converge toward 45%
- **Volatility**: Should stabilize below 2.0
- **Free Spin Triggers**: Should work correctly with 3+ scatters
- **Reel Set Selection**: Should show intelligent choices in debug logs

### Success Indicators
- ✅ RTP approaching 88% target
- ✅ Hit Rate approaching 45% target
- ✅ Reduced volatility swings
- ✅ Free spins triggering correctly
- ✅ Debug logs showing intelligent selection
- ✅ Performance stabilizing over time
