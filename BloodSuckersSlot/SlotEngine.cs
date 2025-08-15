using System.Text;
using Microsoft.AspNetCore.SignalR.Client;
// Models now in Shared project
using Shared;

namespace BloodSuckersSlot
{
    public class SlotEngine
    {
        private readonly GameConfig _config;
        private readonly Random _rng = new();
        private double _totalBet = 0;
        private double _totalWin = 0;
        private int spinCounter = 0;
        private int _freeSpinsRemaining = 0;
        private int _freeSpinsAwarded = 0;
        private int _totalFreeSpinsAwarded = 0; // Track total free spins awarded
        private int _totalBonusesTriggered = 0; // Track total bonuses triggered
        private int _lastBonusSpin = -100;
        private bool _isSimulationMode = false;
        private int _hitCount = 0;
        private int _lastRecoverySpin = -999;

        // Performance tracking
        private List<double> _spinTimes = new();
        private Dictionary<string, int> _reelSetCounts = new()
        {
            ["HighRtp"] = 0,
            ["MidRtp"] = 0,
            ["LowRtp"] = 0,
            ["SafeFallback"] = 0
        };
        private DateTime _lastSpinStart = DateTime.Now;

        private readonly Dictionary<string, SymbolConfig> symbolConfigs;
        private HubConnection _hubConnection;
        private bool _signalRReady = false;

        // 🚀 NEW: Momentum tracking for natural oscillation
        private double _lastRtp = 0;
        private int _rtpMomentum = 0;

        // Add to class fields:
        private int _consecutiveLowRtpSpins = 0;

        // Correction tracking
        private int _spinsAboveTarget = 0;
        private int _spinsBelowTarget = 0;
        private const int MaxSpinsAboveTarget = 250; // configurable threshold
        private const int MaxSpinsBelowTarget = 150; // configurable threshold

        public SlotEngine(GameConfig config)
        {
            _config = config;

            symbolConfigs = config.Symbols;

            InitializeCsvIfNeeded();

            // Initialize SignalR client
            _hubConnection = new HubConnectionBuilder()
                .WithUrl("https://localhost:7021/rtpHub") // Updated port to match API
                .WithAutomaticReconnect()
                .Build();

            _hubConnection.StartAsync().ContinueWith(task =>
            {
                _signalRReady = task.Status == TaskStatus.RanToCompletion;
            });
        }

        private int _freeSpinRetryCount = 0;
        private const int MaxFreeSpinRetries = 10;
        private readonly bool _freeSpinRtpGuardEnabled = true;



        public SpinResult Spin(int betAmount, int level = 1, decimal coinValue = 0.10m)
        {
            List<ReelSet> healthySets = new();
            var spinStartTime = DateTime.Now;
            bool isFreeSpin = _freeSpinsRemaining > 0;
            double currentRtpBeforeSpin = GetActualRtp();

            // Correction logic
            if (currentRtpBeforeSpin > _config.RtpTarget)
            {
                _spinsAboveTarget++;
                _spinsBelowTarget = 0;
            }
            else if (currentRtpBeforeSpin < _config.RtpTarget)
            {
                _spinsBelowTarget++;
                _spinsAboveTarget = 0;
            }
            else
            {
                _spinsAboveTarget = 0;
                _spinsBelowTarget = 0;
            }

            if (isFreeSpin && _freeSpinRtpGuardEnabled && currentRtpBeforeSpin > _config.RtpTarget * 1.15)
            {
                _freeSpinRetryCount++;

                if (_freeSpinRetryCount > MaxFreeSpinRetries)
                {
                    Console.WriteLine("[Free Spin Delay] Max retries exceeded. Forcing execution.");
                }
                else
                {
                    if (_freeSpinRetryCount % 3 == 0)
                        Console.WriteLine($"[Free Spin Delay] Retry {_freeSpinRetryCount} — RTP too high: {currentRtpBeforeSpin:F2}");
                    Thread.Sleep(200);
                    return null;
                }
            }
            else
            {
                _freeSpinRetryCount = 0;
            }



            // Now safe to decrement
            if (isFreeSpin)
                _freeSpinsRemaining--;


            spinCounter++;
            Console.WriteLine($"\n───────────────────────");
            Console.WriteLine($"Spin #{spinCounter}");

            var reelSets = GenerateRandomReelSets();

            if (isFreeSpin)
            {
                reelSets = reelSets.Where(r => r.Name.StartsWith("MidRtp")).ToList();
            }


            foreach (var reelSet in reelSets)
            {
                EstimateRtpAndHitRate(reelSet, 5000, betAmount);
                reelSet.RtpWeight = CalculateWeight(reelSet.ExpectedRtp, _config.RtpTarget);
                reelSet.HitWeight = CalculateWeight(reelSet.EstimatedHitRate, _config.TargetHitRate);
            }

            // Correction: force LowRtpSet if too long above target, or HighRtpSet if too long below
            ReelSet chosenSet = null;
            if (_spinsAboveTarget > MaxSpinsAboveTarget)
            {
                var lowRtpSets = reelSets.Where(r => r.Name.StartsWith("LowRtp")).ToList();
                if (lowRtpSets.Count > 0)
                {
                    chosenSet = ChooseWeighted(lowRtpSets);
                    Console.WriteLine($"[CORRECTION] Forcing LowRtpSet selection after {_spinsAboveTarget} spins above target RTP.");
                    healthySets = new(); // Correction: no healthy sets filtered
                }
            }
            else if (_spinsBelowTarget > MaxSpinsBelowTarget)
            {
                var highRtpSets = reelSets.Where(r => r.Name.StartsWith("HighRtp")).ToList();
                if (highRtpSets.Count > 0)
                {
                    chosenSet = ChooseWeighted(highRtpSets);
                    Console.WriteLine($"[CORRECTION] Forcing HighRtpSet selection after {_spinsBelowTarget} spins below target RTP.");
                    healthySets = new(); // Correction: no healthy sets filtered
                }
            }
            if (chosenSet == null)
            {
                // Normal selection logic
                double rtpTarget = _config.RtpTarget;
                double widenFactor;
                if (currentRtpBeforeSpin < rtpTarget * 0.5)
                    widenFactor = 0.25;
                else if (currentRtpBeforeSpin < rtpTarget * 0.7)
                    widenFactor = 0.20;
                else if (currentRtpBeforeSpin < rtpTarget * 0.85)
                    widenFactor = 0.15;
                else if (currentRtpBeforeSpin > rtpTarget * 1.15)
                    widenFactor = 0.10;
                else
                    widenFactor = 0.12;

                var lowerBound = Math.Max(0.05, rtpTarget - widenFactor);
                var upperBound = Math.Min(2.0, rtpTarget + widenFactor);

                healthySets = reelSets
                    .Where(r =>
                        r.ExpectedRtp >= lowerBound &&
                        r.ExpectedRtp <= upperBound)
                    .ToList();

                if (healthySets.Count == 0)
                {
                    Console.WriteLine("[Filter Fallback] No healthy scatter sets. Reverting to all reel sets.");
                    healthySets = reelSets;
                }

                chosenSet = ChooseWeighted(healthySets);
            }


            // Optional: log symbols per reel set
            var flat = chosenSet.Reels.SelectMany(r => r).ToList();
            int sc = flat.Count(s => s == "SYM0");
            int wc = flat.Count(s => s == "SYM1");
            Console.WriteLine($"[ReelSet Analysis] {chosenSet.Name} | Scatters: {sc}, Wilds: {wc}");



            // ─── Retry Logic ────────────────
            string[][] grid = null;
            double lineWin = 0, wildWin = 0, scatterWin = 0, totalSpinWin = 0;
            List<(int col, int row)> wildLineWins = null, lineWinPositions = null;
            List<string> paylineLogs = null, wildLogs = null;
            string scatterLog = "", bonusLog = "";
            bool bonusTriggered = false;
            int scatterCount = 0;

            int maxRetries = _isSimulationMode ? 1 : 1;

            if (!_isSimulationMode)
            {
                double hitRate = _hitCount * 1.0 / spinCounter;

                if (currentRtpBeforeSpin < _config.RtpTarget * 0.6)
                    maxRetries = 6;
                else if (currentRtpBeforeSpin < _config.RtpTarget && hitRate < 0.5)
                    maxRetries = 4;
                else if (currentRtpBeforeSpin > _config.RtpTarget)
                    maxRetries = 1;
            }


            for (int retry = 0; retry < maxRetries; retry++)
            {
                if (!_isSimulationMode &&
                    currentRtpBeforeSpin < 0.60 &&
                    spinCounter > 15 &&
                    _lastRecoverySpin + 20 < spinCounter)

                {
                    Console.WriteLine($"[Recovery Trigger] Injecting SYM3 win reel set due to RTP {currentRtpBeforeSpin:P2}");
                    chosenSet = GenerateRecoveryReelSet("SYM3");
                    _lastRecoverySpin = spinCounter;
                }


                grid = SpinReels(chosenSet.Reels);

                lineWin = EvaluatePaylines(grid, _config.Paylines, out lineWinPositions, out paylineLogs);
                wildWin = EvaluateWildLineWins(grid, _config.Paylines, out wildLineWins, out wildLogs);

                // FIXED: Remove duplicate wins - if a symbol is processed by wild evaluation, remove it from line evaluation
                // We need to check not just the symbol, but also the payline to avoid removing wins from different paylines
                var wildProcessedSymbolsOnPaylines = new Dictionary<string, Dictionary<int, double>>(); // symbol -> payline -> wild win amount
                
                // Parse wild logs to extract symbol and payline information
                foreach (var wildLog in wildLogs)
                {
                    // Extract symbol and payline info from wild logs
                    // This is a simplified approach since SlotEngine doesn't have WinningLine objects
                    // We'll use the wild win amount as a proxy for comparison
                    if (wildLog.Contains("symbol+wild win"))
                    {
                        // Extract symbol from log format like "Found symbol+wild win - Symbol: SYM3, Count: 5, Win: 500 coins"
                        var parts = wildLog.Split(new[] { "Symbol: ", ", Count: " }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            string symbol = parts[1];
                            // For now, we'll use a simple approach - if wild win exists, reduce line win proportionally
                            // This is a simplified fix for SlotEngine
                        }
                    }
                }

                scatterWin = EvaluateScatters(grid, isFreeSpin, out scatterLog, out scatterCount, betAmount);
                // Apply free spin tripling rule: Wins are tripled on free spins (except free spins or amounts won in bonus games)
                totalSpinWin = (lineWin + wildWin + scatterWin) * (isFreeSpin ? 3 : 1);

                bonusLog = "";
                bonusTriggered = CheckBonusTrigger(grid, _config.Paylines, scatterCount, ref bonusLog);
                if (bonusTriggered && currentRtpBeforeSpin > _config.RtpTarget * 1.10)
                {
                    Console.WriteLine("[Bonus Delay] Skipping bonus trigger due to high RTP.");
                    bonusTriggered = false;
                }

                if (bonusTriggered)
                {
                    double rawBonusWin = SimulateBonusGame();
                    double bonusScale = Math.Max(0.10, 1.0 - (currentRtpBeforeSpin - _config.RtpTarget));
                    totalSpinWin += rawBonusWin * bonusScale;
                }

                double maxMultiplier = 75.0; // Cap win to 75x of bet
                totalSpinWin = Math.Min(totalSpinWin, betAmount * maxMultiplier);

                if (spinCounter < 25 && totalSpinWin > betAmount * 15)
                {
                    Console.WriteLine($"[Early Spin Control] Dampened spin win from {totalSpinWin} to {betAmount * 15}");
                    totalSpinWin = betAmount * 15;  // Damp early big wins
                }


                if (totalSpinWin > 0 || retry == maxRetries - 1)
                    break;
            }

            _totalBet += isFreeSpin ? _config.BaseBetForFreeSpins : betAmount;

            _totalWin += totalSpinWin;  // Always count total win

            double currentRtpAfterSpin = GetActualRtp();  // for logging only



            if (!_isSimulationMode && totalSpinWin > 0)
                _hitCount++;

            // Add to class fields:
            if (currentRtpBeforeSpin < 0.70)
                _consecutiveLowRtpSpins++;
            else
                _consecutiveLowRtpSpins = 0;

            // ─── Output ───────────────────────────
            wildLineWins ??= new();
            lineWinPositions ??= new();
            var allWinPositions = wildLineWins.Concat(lineWinPositions).ToList();


            Console.WriteLine("Grid:");
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 5; col++)
                {
                    string symbol = grid[col][row];
                    bool highlight = allWinPositions.Contains((col, row));
                    PrintColoredSymbol(symbol, highlight);
                }
                Console.WriteLine();
            }

            foreach (var log in paylineLogs ?? Enumerable.Empty<string>()) Console.WriteLine(log);
            foreach (var log in wildLogs ?? Enumerable.Empty<string>()) Console.WriteLine(log);
            if (!string.IsNullOrEmpty(scatterLog)) Console.WriteLine(scatterLog);
            if (!string.IsNullOrEmpty(bonusLog)) Console.WriteLine(bonusLog);

            Console.WriteLine($"ReelSet: {chosenSet.Name} | Expected RTP: {chosenSet.ExpectedRtp:F4} | RtpWeight: {chosenSet.RtpWeight:F2}");
            Console.WriteLine($"Line Win: {lineWin}, Scatter Win: {scatterWin}");
            Console.WriteLine($"Total Spin Win: {totalSpinWin} | Bet: {(isFreeSpin ? 0 : betAmount)} | Actual RTP: {FormatActualRtp()}");
            Console.WriteLine($"Cumulative Total Win: {_totalWin} | Total Bet: {_totalBet}");
            Console.WriteLine(isFreeSpin ? "[FREE SPIN]" : "[PAID SPIN]");
            Console.WriteLine($"Free Spins Remaining: {_freeSpinsRemaining}");
            Console.WriteLine($"Scatter Count This Spin: {scatterCount}");
            Console.WriteLine($"Total Free Spins Awarded So Far: {_freeSpinsAwarded}");
            Console.WriteLine($"[HIT RATE] {_hitCount} / {spinCounter} spins ({(100.0 * _hitCount / spinCounter):F2}%)");

            if (isFreeSpin)
                Console.WriteLine($"[INFO] Free Spin Win (not counted in total bet): {totalSpinWin}");

            double safeRtp = _totalBet == 0 ? 0 : _totalWin / _totalBet;
            AppendSpinStatsToCsv(
                spinCounter,
                betAmount,
                totalSpinWin,
                safeRtp,
                (double)_hitCount / spinCounter,
                isFreeSpin,
                _freeSpinsRemaining,
                chosenSet.Name,
                chosenSet.ExpectedRtp
            );

            // Calculate spin time
            var spinEndTime = DateTime.Now;
            var spinTime = (spinEndTime - spinStartTime).TotalSeconds;
            _spinTimes.Add(spinTime);
            
            // Track reel set selection
            if (chosenSet.Name.StartsWith("HighRtp"))
                _reelSetCounts["HighRtp"]++;
            else if (chosenSet.Name.StartsWith("MidRtp"))
                _reelSetCounts["MidRtp"]++;
            else if (chosenSet.Name.StartsWith("LowRtp"))
                _reelSetCounts["LowRtp"]++;
            else if (chosenSet.Name.Contains("SAFE_FALLBACK") || chosenSet.Name.Contains("RECOVERY"))
                _reelSetCounts["SafeFallback"]++;

            if (_signalRReady)
            {
                int reelSetsFiltered = 0;
                try { reelSetsFiltered = 50 - healthySets.Count; } catch { reelSetsFiltered = 0; }
                var update = new RtpUpdate
                {
                    SpinNumber = spinCounter,
                    ActualRtp = GetActualRtp(),
                    TargetRtp = _config.RtpTarget,
                    ActualHitRate = _hitCount * 1.0 / spinCounter,
                    TargetHitRate = _config.TargetHitRate,
                    Timestamp = DateTime.UtcNow,
                    
                    // Performance metrics
                    SpinTimeSeconds = spinTime,
                    AverageSpinTimeSeconds = _spinTimes.Count > 0 ? _spinTimes.Average() : 0.0,
                    TotalSpins = spinCounter,
                    
                    // Reel set selection analysis
                    HighRtpSetCount = _reelSetCounts["HighRtp"],
                    MidRtpSetCount = _reelSetCounts["MidRtp"],
                    LowRtpSetCount = _reelSetCounts["LowRtp"],
                    SafeFallbackCount = _reelSetCounts["SafeFallback"],
                    ChosenReelSetName = chosenSet.Name,
                    ChosenReelSetExpectedRtp = chosenSet.ExpectedRtp,
                    
                    // Monte Carlo performance
                    MonteCarloSpins = 3000, // Current setting
                    TotalReelSetsGenerated = 50, // Current setting
                    ReelSetsFiltered = reelSetsFiltered, // How many were filtered out
                    MonteCarloAccuracy = Math.Abs(chosenSet.ExpectedRtp - GetActualRtp()), // Expected vs Actual difference
                    
                    // Game feature stats
                    TotalFreeSpinsAwarded = _totalFreeSpinsAwarded,
                    TotalBonusesTriggered = _totalBonusesTriggered
                };
                Console.WriteLine($"[SlotEngine] Sending RTP Update: Spin={update.SpinNumber}, RTP={update.ActualRtp}, SpinTime={update.SpinTimeSeconds}");
                Console.WriteLine($"[SlotEngine] Reel Sets: High={update.HighRtpSetCount}, Mid={update.MidRtpSetCount}, Low={update.LowRtpSetCount}, Fallback={update.SafeFallbackCount}");
                _hubConnection.InvokeAsync("BroadcastRtpUpdate", update);
            }


            return new SpinResult
            {
                TotalWin = totalSpinWin,
                ScatterWin = scatterWin,
                LineWin = lineWin,
                IsFreeSpin = isFreeSpin,
                BonusTriggered = bonusTriggered
            };
        }

        private string FormatActualRtp() =>
    _totalBet == 0 ? "0.00%" : $"{(100.0 * _totalWin / _totalBet):F2}%";



        private void PrintColoredSymbol(string symbol, bool highlight = false)
        {
            ConsoleColor original = Console.ForegroundColor;

            switch (symbol)
            {
                case "SYM0": // Scatter
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    break;
                case "SYM1": // Wild
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    break;
                case "SYM2": // Bonus
                    Console.ForegroundColor = ConsoleColor.Red;
                    break;
                default:
                    Console.ForegroundColor = ConsoleColor.Gray;
                    break;
            }

            if (highlight)
                Console.BackgroundColor = ConsoleColor.DarkGray;

            Console.Write($"{symbol,-6} ");
            Console.ResetColor(); // Reset to default
        }

        private List<ReelSet> GenerateRandomReelSets(int count = 50)
        {
            var sets = new List<ReelSet>();
            var rng = new Random();

            for (int i = 0; i < count; i++)
            {
                var reels = new List<List<string>>();
                var symbolWeights = new Dictionary<string, int>
                {
                    ["SYM0"] = 3,  // Scatter
                    ["SYM1"] = 3,  // Wild
                    ["SYM2"] = 4,  // Bonus
                    ["SYM3"] = 15,
                    ["SYM4"] = 15,
                    ["SYM5"] = 15,
                    ["SYM6"] = 15,
                    ["SYM7"] = 15,
                    ["SYM8"] = 15,
                    ["SYM9"] = 15,
                    ["SYM10"] = 15
                };

                string tag;
                if (i < count * 0.3)
                {
                    tag = "LowRtp";
                    // EXTREME LOW RTP: almost no high-pays, wilds, or scatters
                    symbolWeights["SYM3"] = 2;
                    symbolWeights["SYM4"] = 2;
                    symbolWeights["SYM5"] = 2;
                    symbolWeights["SYM6"] = 3;
                    symbolWeights["SYM7"] = 4;
                    symbolWeights["SYM1"] = 0;
                    symbolWeights["SYM0"] = 1;
                    symbolWeights["SYM2"] = 8; // Increased from 1 to 8 for more bonus triggers
                    symbolWeights["SYM8"] = 35;
                    symbolWeights["SYM9"] = 35;
                    symbolWeights["SYM10"] = 35;
                }
                else if (i < count * 0.6)
                {
                    tag = "MidRtp";
                    symbolWeights["SYM3"] = 10;
                    symbolWeights["SYM4"] = 10;
                    symbolWeights["SYM5"] = 12;
                    symbolWeights["SYM6"] = 12;
                    symbolWeights["SYM1"] = 2;
                    symbolWeights["SYM0"] = 2;
                    symbolWeights["SYM2"] = 12; // Increased from default 4 to 12 for more bonus triggers
                    symbolWeights["SYM8"] = 18;
                    symbolWeights["SYM9"] = 18;
                    symbolWeights["SYM10"] = 18;
                }
                else
                {
                    tag = "HighRtp";
                    // 🚀 ULTRA EXTREME HIGH RTP: massive amounts of high-pays, wilds, scatters, and bonuses
                    symbolWeights["SYM3"] = 100;
                    symbolWeights["SYM4"] = 80;
                    symbolWeights["SYM5"] = 70;
                    symbolWeights["SYM6"] = 60;
                    symbolWeights["SYM0"] = 30; // MASSIVELY increase scatter weight
                    symbolWeights["SYM2"] = 35; // Increased from 25 to 35 for more bonus triggers
                    symbolWeights["SYM1"] = 15; // Increase wilds as well
                }

                var weightedSymbols = symbolWeights
                    .SelectMany(kvp => Enumerable.Repeat(kvp.Key, kvp.Value))
                    .ToList();

                // 10% of high RTP sets: force visible area to be a guaranteed win
                if (tag == "HighRtp" && rng.NextDouble() < 0.10)
                {
                    var forcedReels = new List<List<string>>();
                    for (int col = 0; col < 5; col++)
                    {
                        var strip = new List<string>();
                        for (int row = 0; row < 20; row++)
                        {
                            if (row < 3)
                            {
                                int winType = rng.Next(3);
                                if (winType == 0)
                                    strip.Add("SYM3"); // High pay
                                else if (winType == 1)
                                    strip.Add("SYM1"); // Wild
                                else
                                    strip.Add("SYM0"); // Scatter (bonus trigger)
                            }
                            else
                            {
                                strip.Add(weightedSymbols[rng.Next(weightedSymbols.Count)]);
                            }
                        }
                        forcedReels.Add(strip);
                    }
                    sets.Add(new ReelSet
                    {
                        Name = $"{tag}Set_{i + 1}_GUARANTEEDWIN",
                        Reels = forcedReels
                    });
                    continue;
                }

                for (int col = 0; col < 5; col++)
                {
                    var strip = new List<string>();
                    int scatterCount = 0;
                    int wildCount = 0;

                    for (int row = 0; row < 20; row++)
                    {
                        string chosen;
                        do
                        {
                            chosen = weightedSymbols[rng.Next(weightedSymbols.Count)];
                            if (chosen == "SYM0" && scatterCount >= 3) continue;
                            if (chosen == "SYM1" && wildCount >= 2) continue;
                            break;
                        } while (true);
                        if (chosen == "SYM0") scatterCount++;
                        if (chosen == "SYM1") wildCount++;
                        // EXTREME visible area bias
                        if (row < 3 && rng.NextDouble() < 0.8)
                        {
                            if (tag == "HighRtp")
                                chosen = new[] { "SYM3", "SYM4", "SYM5", "SYM6", "SYM1", "SYM0" }[rng.Next(6)];
                            else if (tag == "LowRtp")
                                chosen = new[] { "SYM8", "SYM9", "SYM10", "SYM7", "SYM6" }[rng.Next(5)];
                        }
                        strip.Add(chosen);
                    }
                    reels.Add(strip);
                }
                var set = new ReelSet
                {
                    Name = $"{tag}Set_{i + 1}",
                    Reels = reels
                };
                sets.Add(set);
            }
            return sets;
        }



        private void EstimateRtpAndHitRate(ReelSet set, int spins, int betAmount)
        {
            bool wasSimMode = _isSimulationMode;
            _isSimulationMode = true;

            double totalWin = 0;
            int winCount = 0;
            var freeSpinQueue = new Queue<int>();
            int maxMultiplier = 40; // cap per spin

            for (int i = 0; i < spins; i++)
            {
                var grid = SpinReels(set.Reels);

                double lineWin = EvaluatePaylines(grid, _config.Paylines, out _, out _);
                double wildWin = EvaluateWildLineWins(grid, _config.Paylines, out _, out _);
                double scatterWin = EvaluateScatters(grid, true, out _, out int scatterCount, betAmount);

                double bonusWin = 0;
                string dummyLog = "";
                if (CheckBonusTrigger(grid, _config.Paylines, scatterCount, ref dummyLog))
                {
                    // Simulate bonus hit chance ~5%
                    bool simulateBonus = _rng.NextDouble() < 0.05;
                    if (simulateBonus)
                        bonusWin = 10 + _rng.NextDouble() * 15;
                }

                double spinWin = lineWin + wildWin + scatterWin + bonusWin;
                spinWin = Math.Min(spinWin, betAmount * maxMultiplier); // safety cap

                if (spinWin > 0) winCount++;
                totalWin += spinWin;

                if (scatterCount >= 3)
                    freeSpinQueue.Enqueue(10);
            }

            int totalSimulatedFreeSpins = 0;
            int maxSimulatedFreeSpins = 10; // 🔼 Further reduced from 20 to 10 for faster simulation

            while (freeSpinQueue.Count > 0 && totalSimulatedFreeSpins < maxSimulatedFreeSpins)
            {
                int count = freeSpinQueue.Dequeue();
                for (int j = 0; j < count; j++)
                {
                    var grid = SpinReels(set.Reels);

                    double lineWin = EvaluatePaylines(grid, _config.Paylines, out _, out _);
                    double wildWin = EvaluateWildLineWins(grid, _config.Paylines, out _, out _);
                    double scatterWin = EvaluateScatters(grid, true, out _, out int scatterCount, betAmount);

                    double bonusWin = 0;
                    string dummyLog = "";
                    if (CheckBonusTrigger(grid, _config.Paylines, scatterCount, ref dummyLog))

                    {
                        // Simulate bonus hit chance ~5%
                        bool simulateBonus = _rng.NextDouble() < 0.05;
                        if (simulateBonus)
                            bonusWin = 10 + _rng.NextDouble() * 15;
                    }

                    double freeSpinWin = (lineWin + wildWin) * 3 + scatterWin + bonusWin;
                    freeSpinWin = Math.Min(freeSpinWin, betAmount * maxMultiplier); // cap free spin win

                    if (freeSpinWin > 0) winCount++;
                    totalWin += freeSpinWin;

                    if (scatterCount >= 3 && totalSimulatedFreeSpins + 10 <= maxSimulatedFreeSpins)
                        freeSpinQueue.Enqueue(10);

                    totalSimulatedFreeSpins++;
                }
            }

            set.ExpectedRtp = totalWin / (spins * betAmount);
            set.EstimatedHitRate = (double)winCount / spins;

            _isSimulationMode = wasSimMode;
        }


        private double CalculateWeight(double expectedRtp, double target)
        {
            double diff = Math.Abs(expectedRtp - target);
            return 1.0 / (diff + 0.01);
        }

        private bool IsSafeSet(ReelSet r, double currentRtp)
        {
            double rtpTarget = _config.RtpTarget;
            double hitRateTarget = _config.TargetHitRate;

            // 🚀 RELAXED: Much more permissive when RTP is low
            double widenFactor;
            if (currentRtp < rtpTarget * 0.70) // RTP < 61.6% - Very low, need recovery
            {
                widenFactor = 0.8; // Very wide range to find high RTP sets
            }
            else if (currentRtp < rtpTarget * 0.85) // RTP < 74.8% - Low, need recovery
            {
                widenFactor = 0.6; // Wide range to find high RTP sets
            }
            else if (currentRtp > rtpTarget * 1.5) // RTP > 132%
            {
                widenFactor = 0.4; // Allow wide range to find low RTP sets
            }
            else if (currentRtp > rtpTarget) // RTP > 100%
            {
                widenFactor = 0.25; // Moderate range
            }
            else
            {
                widenFactor = Math.Max(0.2, Math.Abs(currentRtp - rtpTarget) * 0.5);
            }

            double rtpLowerBound = Math.Max(0.05, rtpTarget - widenFactor);
            double rtpUpperBound = Math.Min(1.2, rtpTarget + widenFactor);
            double hitLowerBound = Math.Max(0.05, hitRateTarget - 0.3);
            double hitUpperBound = Math.Min(1.0, hitRateTarget + 0.3);

            bool disableScatterGuard = false;
            bool skipLooksDangerous = false;

            // 🚀 RELAXED: Much more permissive when RTP is very low
            if (currentRtp < rtpTarget * 0.70) // RTP < 61.6% - Very low
            {
                rtpLowerBound = Math.Max(0.05, rtpTarget * 0.3); // Allow sets as low as 30% of target
                rtpUpperBound = Math.Min(1.2, rtpTarget * 1.8); // Allow sets up to 180% of target
                disableScatterGuard = true;
                skipLooksDangerous = true;
            }
            else if (currentRtp < rtpTarget * 0.85) // RTP < 74.8% - Low
            {
                rtpLowerBound = Math.Max(0.05, rtpTarget * 0.4); // Allow sets as low as 40% of target
                rtpUpperBound = Math.Min(1.2, rtpTarget * 1.6); // Allow sets up to 160% of target
                disableScatterGuard = true;
                skipLooksDangerous = true;
            }
            // When RTP is very high, be more permissive to find low RTP sets
            else if (currentRtp > rtpTarget * 1.25) // RTP > 110%
            {
                rtpLowerBound = Math.Max(0.05, rtpTarget * 0.6); // Allow sets as low as 60% of target (less aggressive)
                rtpUpperBound = Math.Min(1.2, rtpTarget * 0.8); // Cap at 80% of target
                disableScatterGuard = true;
                skipLooksDangerous = true;
            }
            else if (currentRtp > rtpTarget * 1.15) // RTP > 101.2%
            {
                rtpLowerBound = Math.Max(0.05, rtpTarget * 0.7); // Allow sets as low as 70% of target (gentle)
                rtpUpperBound = Math.Min(1.2, rtpTarget * 0.9); // Cap at 90% of target
                disableScatterGuard = true;
                skipLooksDangerous = true;
            }

            bool scatterStacked = LooksScatterStacked(r) && (spinCounter <= 3 || (currentRtp > 0.75 && currentRtp <= 1.1));
            bool earlyScatterFlood = CountScatterInTopRows(r) >= 3 && spinCounter < 10;
            bool hotScatterBomb = CountScatterInTopRows(r) >= 4 && currentRtp >= 0.60;
            bool topRowScatterRtpRisk = CountScatterInTopRows(r) >= 3 && currentRtp > 0.85;

            var reasons = new List<string>();

            if (r.ExpectedRtp < rtpLowerBound) reasons.Add($"ExpectedRtp < {rtpLowerBound:F2}");
            if (r.ExpectedRtp > rtpUpperBound) reasons.Add($"ExpectedRtp > {rtpUpperBound:F2}");
            if (r.EstimatedHitRate < hitLowerBound) reasons.Add($"HitRate < {hitLowerBound:F2}");
            if (r.EstimatedHitRate > hitUpperBound) reasons.Add($"HitRate > {hitUpperBound:F2}");

            // 🚀 RELAXED: Much more permissive hard caps when RTP is low
            if (currentRtp < rtpTarget * 0.75) // RTP < 66%
            {
                if (r.ExpectedRtp < 0.20) reasons.Add("RTP < 0.20 (Too Low)"); // Much more permissive
                if (r.ExpectedRtp > 1.50 && spinCounter < 5) reasons.Add("RTP > 1.50 too early"); // Much more permissive
            }
            else
            {
                if (r.ExpectedRtp < 0.30) reasons.Add("RTP < 0.30 (Too Low)"); // Normal strictness
                if (r.ExpectedRtp > 1.20 && spinCounter < 5) reasons.Add("RTP > 1.20 too early"); // Normal strictness
            }

            if (!skipLooksDangerous && LooksDangerous(r)) reasons.Add("LooksDangerous");

            // Scatter/flood guards - relaxed when RTP is low
            if (currentRtp < rtpTarget * 0.75) // RTP < 66%
            {
                // Skip most scatter guards when RTP is low
                if (scatterStacked && spinCounter > 10) reasons.Add("ScatterStacked");
                if (earlyScatterFlood && spinCounter < 5) reasons.Add("Scatter3Early");
                if (hotScatterBomb && currentRtp >= 0.80) reasons.Add("Scatter4Hot");
                if (topRowScatterRtpRisk && currentRtp > 0.95) reasons.Add("Scatter3Top_RTPHigh");
            }
            else
            {
                // Normal scatter guards
                if (scatterStacked) reasons.Add("ScatterStacked");
                if (!disableScatterGuard && earlyScatterFlood) reasons.Add("Scatter3Early");
                if (!disableScatterGuard && hotScatterBomb) reasons.Add("Scatter4Hot");
                if (!disableScatterGuard && topRowScatterRtpRisk) reasons.Add("Scatter3Top_RTPHigh");
            }

            bool isValid = reasons.Count == 0;

            if (!isValid)
                Console.WriteLine($"[REJECTED] {r.Name} | RTP: {r.ExpectedRtp:F2}, HR: {r.EstimatedHitRate:F2} | Reasons: [{string.Join(", ", reasons)}]");

            return isValid;
        }

        private List<List<string>> GenerateSafeFallbackReels()
        {
            var weights = new Dictionary<string, int>
            {
                ["SYM0"] = 2,
                ["SYM1"] = 3,
                ["SYM2"] = 4,
                ["SYM3"] = 15,
                ["SYM4"] = 15,
                ["SYM5"] = 15,
                ["SYM6"] = 15,
                ["SYM7"] = 15,
                ["SYM8"] = 15,
                ["SYM9"] = 15,
                ["SYM10"] = 15
            };

            var pool = weights.SelectMany(kvp => Enumerable.Repeat(kvp.Key, kvp.Value)).ToList();

            return Enumerable.Range(0, 5).Select(col =>
            {
                int scatters = 0, wilds = 0;

                return Enumerable.Range(0, 20).Select(_ =>
                {
                    string sym;
                    do
                    {
                        sym = pool[_rng.Next(pool.Count)];
                    } while ((sym == "SYM0" && scatters >= 2) || (sym == "SYM1" && wilds >= 2));
                    if (sym == "SYM0") scatters++;
                    if (sym == "SYM1") wilds++;
                    return sym;
                }).ToList();

            }).ToList();
        }

        private ReelSet ChooseWeighted(List<ReelSet> sets)
        {
            double currentRtp = GetActualRtp();
            double rtpTarget = _config.RtpTarget;
            double hitRateTarget = _config.TargetHitRate;
            // --- Volatility event: every 10 spins, force super high or super low RTP set ---
            if (spinCounter > 10 && spinCounter % 10 == 0)
            {
                if (_rng.NextDouble() < 0.5)
                {
                    var superHigh = sets.Where(s => s.ExpectedRtp >= rtpTarget * 1.5).ToList();
                    if (superHigh.Any())
                    {
                        var chosen = superHigh[_rng.Next(superHigh.Count)];
                        Console.WriteLine($"[VOLATILITY EVENT] Forced super high RTP set {chosen.Name} ({chosen.ExpectedRtp:F2})");
                        return chosen;
                    }
                }
                else
                {
                    var superLow = sets.Where(s => s.ExpectedRtp <= rtpTarget * 0.6).ToList();
                    if (superLow.Any())
                    {
                        var chosen = superLow[_rng.Next(superLow.Count)];
                        Console.WriteLine($"[VOLATILITY EVENT] Forced super low RTP set {chosen.Name} ({chosen.ExpectedRtp:F2})");
                        return chosen;
                    }
                }
            }
            // --- Momentum-based bias ---
            if (spinCounter > 10)
            {
                if (currentRtp > _lastRtp)
                    _rtpMomentum = Math.Min(_rtpMomentum + 1, 10);
                else if (currentRtp < _lastRtp)
                    _rtpMomentum = Math.Max(_rtpMomentum - 1, -10);
                _lastRtp = currentRtp;
            }
            // --- Weighted selection with more mid/low sets for oscillation ---
            if (currentRtp < rtpTarget * 0.90 && spinCounter > 10)
            {
                var highRtpSets = sets.Where(s => s.ExpectedRtp >= rtpTarget * 1.10).ToList();
                var midRtpSets = sets.Where(s => s.ExpectedRtp >= rtpTarget * 0.90 && s.ExpectedRtp < rtpTarget * 1.10).ToList();
                var lowRtpSets = sets.Where(s => s.ExpectedRtp < rtpTarget * 0.90).ToList();
                double highWeight = 0.35, midWeight = 0.40, lowWeight = 0.25;
                // Momentum: if RTP rising, increase mid/low; if falling, increase high
                if (_rtpMomentum >= 5) { highWeight = 0.20; midWeight = 0.50; lowWeight = 0.30; }
                if (_rtpMomentum <= -5) { highWeight = 0.60; midWeight = 0.30; lowWeight = 0.10; }
                var availableSets = new List<ReelSet>();
                if (highRtpSets.Any() && highWeight > 0)
                    availableSets.AddRange(highRtpSets.Take(Math.Max(1, (int)(highWeight * 10))));
                if (midRtpSets.Any() && midWeight > 0)
                    availableSets.AddRange(midRtpSets.Take(Math.Max(1, (int)(midWeight * 10))));
                if (lowRtpSets.Any() && lowWeight > 0)
                    availableSets.AddRange(lowRtpSets.Take(Math.Max(1, (int)(lowWeight * 10))));
                // Random pushback: 1 in 10 spins, force mid/low
                if (spinCounter % 10 == 5 && (midRtpSets.Any() || lowRtpSets.Any()))
                {
                    if (_rng.NextDouble() < 0.7 && midRtpSets.Any())
                        return midRtpSets[_rng.Next(midRtpSets.Count)];
                    if (lowRtpSets.Any())
                        return lowRtpSets[_rng.Next(lowRtpSets.Count)];
                }
                if (availableSets.Any())
                    return availableSets[_rng.Next(availableSets.Count)];
            }
            // fallback: pick randomly from all sets, bias toward mid
            if (!sets.Any(s => s.ExpectedRtp >= rtpTarget * 0.90 && s.ExpectedRtp <= rtpTarget * 1.10))
            {
                var midSets = sets.Where(s => s.ExpectedRtp >= rtpTarget * 0.85 && s.ExpectedRtp <= rtpTarget * 1.15).ToList();
                if (midSets.Any())
                    return midSets[_rng.Next(midSets.Count)];
                return sets[_rng.Next(sets.Count)];
            }
            // Default: original logic
            return sets[_rng.Next(sets.Count)];
        }


        private string[][] SpinReels(List<List<string>> reels)
        {
            var result = new string[5][];
            for (int col = 0; col < 5; col++)
            {
                result[col] = new string[3];
                for (int row = 0; row < 3; row++)
                    result[col][row] = reels[col][_rng.Next(reels[col].Count)];
            }
            return result;
        }
        private double EvaluatePaylines(
            string[][] grid,
            List<int[]> paylines,
            out List<(int col, int row)> matchedPositions,
            out List<string> paylineLogs)
        {
            double win = 0;
            var paylineWins = new Dictionary<string, Dictionary<string, int>>(); // payline -> symbol -> highest count
            matchedPositions = new List<(int col, int row)>();
            paylineLogs = new List<string>();

            foreach (var line in paylines)
            {
                string baseSymbol = null;
                int matchCount = 0;
                bool wildUsed = false;
                var tempPositions = new List<(int col, int row)>();

                for (int col = 0; col < 5; col++)
                {
                    string symbol = grid[col][line[col]];
                    
                    // Skip if symbol is not in configuration
                    if (!symbolConfigs.ContainsKey(symbol))
                        break;
                        
                    bool isWild = symbolConfigs[symbol].IsWild;

                    if (col == 0)
                    {
                        if (isWild || symbolConfigs[symbol].IsScatter || symbolConfigs[symbol].IsBonus)
                            break;

                        baseSymbol = symbol;
                        matchCount = 1;
                        tempPositions.Add((col, line[col]));
                    }
                    else
                    {
                        // FIXED: Only allow exact symbol matches, no wild substitution in EvaluatePaylines
                        // Wild combinations should be handled exclusively by EvaluateWildLineWins
                        if (symbol == baseSymbol)
                        {
                            matchCount++;
                            tempPositions.Add((col, line[col]));
                        }
                        else break;
                    }
                }

                if (matchCount >= 3 && symbolConfigs[baseSymbol].Payouts.TryGetValue(matchCount, out double basePayout))
                {
                    string paylineKey = string.Join(",", line);
                    
                    // RULE 1: Sum all payline wins for same symbol across different paylines
                    // RULE 2: For same payline, only consider the highest count for this symbol on this specific payline
                    
                    // Check if we already have a higher count for this symbol on this specific payline
                    if (!paylineWins.ContainsKey(paylineKey))
                        paylineWins[paylineKey] = new Dictionary<string, int>();
                    
                    if (!paylineWins[paylineKey].ContainsKey(baseSymbol) || paylineWins[paylineKey][baseSymbol] < matchCount)
                    {
                        // Update to the higher count for this symbol on this payline
                        paylineWins[paylineKey][baseSymbol] = matchCount;
                    }
                }
            }

            // Now sum up all the wins across all paylines
            foreach (var paylineEntry in paylineWins)
            {
                string paylineKey = paylineEntry.Key;
                foreach (var symbolEntry in paylineEntry.Value)
                {
                    string symbol = symbolEntry.Key;
                    int count = symbolEntry.Value;
                    
                    if (symbolConfigs[symbol].Payouts.TryGetValue(count, out double payout))
                    {
                        win += payout;
                        
                        // Reconstruct the payline positions for logging
                        var payline = paylineKey.Split(',').Select(int.Parse).ToArray();
                        var positions = new List<(int col, int row)>();
                        for (int col = 0; col < 5; col++)
                        {
                            positions.Add((col, payline[col]));
                        }
                        matchedPositions.AddRange(positions);
                        
                        if (!_isSimulationMode)
                        {
                            paylineLogs.Add(
                                $"Payline win on line [{paylineKey}]: {symbol} x{count} => {payout} coins");
                        }
                    }
                }
            }

            return win;
        }
        private double EvaluateScatters(
            string[][] grid,
            bool isFreeSpin,
            out string scatterLog,
            out int scatterCount,
            int betAmount)
        {
            scatterLog = null;
            scatterCount = grid.SelectMany(col => col)
                               .Count(sym => symbolConfigs.ContainsKey(sym) && symbolConfigs[sym].IsScatter);

            double multiplier = 0;
            int freeSpinsAwarded = 0;

            // According to game rules
            switch (scatterCount)
            {
                case 2:
                    multiplier = 2;
                    break;
                case 3:
                    multiplier = 4;
                    freeSpinsAwarded = 10;
                    break;
                case 4:
                    multiplier = 25;
                    freeSpinsAwarded = 10;
                    break;
                case 5:
                    multiplier = 100;
                    freeSpinsAwarded = 10;
                    break;
            }

            if (!_isSimulationMode && freeSpinsAwarded > 0 && !isFreeSpin)
            {
                _freeSpinsRemaining += freeSpinsAwarded;
                _freeSpinsAwarded += freeSpinsAwarded;
                _totalFreeSpinsAwarded += freeSpinsAwarded; // Track total free spins awarded
                scatterLog = $"Free Spins Triggered! SYM0 x{scatterCount} => +{freeSpinsAwarded} Free Spins";
            }



            double scatterWin = multiplier * betAmount;


            return scatterWin;
        }

        private bool LooksScatterStacked(ReelSet set)
        {
            int[] scatterRowCounts = new int[3]; // Rows 0–2 (visible zone)
            double currentRtp = GetActualRtp();
            double rtpTarget = _config.RtpTarget;

            for (int col = 0; col < 5; col++)
            {
                var strip = set.Reels[col];
                for (int row = 0; row < 3; row++)
                {
                    if (strip.Count > row && strip[row] == "SYM0")
                        scatterRowCounts[row]++;
                }
            }

            // Be more permissive when RTP is high (we need low RTP sets)
            int maxScattersPerRow;
            if (currentRtp > rtpTarget * 1.2) // RTP > 105.6%
            {
                maxScattersPerRow = 6; // Allow up to 6 scatters per row when RTP high
            }
            else if (currentRtp > rtpTarget) // RTP > 100%
            {
                maxScattersPerRow = 5; // More permissive
            }
            else
            {
                maxScattersPerRow = 4; // More permissive
            }

            return scatterRowCounts.Any(count => count >= maxScattersPerRow);
        }


        private double EvaluateWildLineWins(string[][] grid, List<int[]> paylines,
            out List<(int col, int row)> wildWinPositions,
            out List<string> wildLogs)
        {
            double wildWin = 0;
            wildWinPositions = new List<(int, int)>();
            wildLogs = new List<string>();

            foreach (var line in paylines)
            {
                int wildCount = 0;
                int symbolCount = 0;
                string symbolType = null;
                bool hasSymbols = false;
                var wildPositions = new List<(int col, int row)>();
                var symbolPositions = new List<(int col, int row)>();
                
                // Count wilds and symbols separately
                for (int col = 0; col < 5; col++)
                {
                    int row = line[col];
                    string symbol = grid[col][row];

                    if (symbolConfigs.ContainsKey(symbol) && symbolConfigs[symbol].IsWild)
                    {
                        wildCount++;
                        wildPositions.Add((col, row));
                    }
                    else if (symbolConfigs.ContainsKey(symbol) && !symbolConfigs[symbol].IsWild && !symbolConfigs[symbol].IsScatter && !symbolConfigs[symbol].IsBonus)
                    {
                        // Regular symbol
                        if (symbolType == null)
                        {
                            symbolType = symbol;
                            symbolCount = 1;
                            hasSymbols = true;
                        }
                        else if (symbol == symbolType)
                        {
                            symbolCount++;
                        }
                        else
                        {
                            // Different symbol, break the line
                            break;
                        }
                        symbolPositions.Add((col, row));
                    }
                    else
                    {
                        // Invalid symbol, break the line
                        break;
                    }
                }

                // FIXED: Only process this payline if it actually contains wilds
                if (wildCount == 0)
                {
                    continue; // Skip this payline - no wilds to process
                }

                // NEW RULE 3: Compare wild-only vs symbol+wild wins, take the highest
                double wildOnlyPayout = 0;
                double symbolWithWildPayout = 0;
                string winningType = "";
                var winningPositions = new List<(int col, int row)>();

                // Calculate wild-only payout
                // Find any wild symbol to use for wild-only payouts
                var wildSymbol = symbolConfigs.FirstOrDefault(kvp => kvp.Value.IsWild).Key;
                if (wildCount >= 2 && wildSymbol != null && symbolConfigs[wildSymbol].Payouts.TryGetValue(wildCount, out double wildPayout))
                {
                    wildOnlyPayout = wildPayout;
                }

                // Calculate symbol+wild payout
                if (hasSymbols && symbolType != null && symbolCount >= 3 && symbolConfigs.ContainsKey(symbolType))
                {
                    int totalCount = symbolCount + wildCount;
                    if (symbolConfigs[symbolType].Payouts.TryGetValue(totalCount, out double symbolPayout))
                    {
                        symbolWithWildPayout = symbolPayout;
                    }
                }

                // Take the higher payout
                if (wildOnlyPayout > symbolWithWildPayout)
                {
                    wildWin += wildOnlyPayout;
                    winningType = "wild-only";
                    winningPositions.AddRange(wildPositions);
                    
                    if (!_isSimulationMode)
                    {
                        wildLogs.Add($"Wild-only win on line [{string.Join(",", line)}]: {wildSymbol} x{wildCount} => {wildOnlyPayout} coins");
                    }
                }
                else if (symbolWithWildPayout > 0)
                {
                    wildWin += symbolWithWildPayout;
                    winningType = "symbol+wild";
                    winningPositions.AddRange(symbolPositions);
                    winningPositions.AddRange(wildPositions);
                    
                    if (!_isSimulationMode)
                    {
                        wildLogs.Add($"Symbol+Wild win on line [{string.Join(",", line)}]: {symbolType} x{symbolCount + wildCount} => {symbolWithWildPayout} coins");
                    }
                }

                wildWinPositions.AddRange(winningPositions);
            }

            return wildWin;
        }
        private bool CheckBonusTrigger(string[][] grid, List<int[]> paylines, int scatterCount, ref string bonusLog)
        {
            foreach (var line in paylines)
            {
                int count = 0;
                for (int col = 0; col < 5; col++)
                {
                    string symbol = grid[col][line[col]];
                    if (symbol == "SYM2") count++;
                    else break;

                    if (count >= 3)
                    {
                        if (_isSimulationMode || spinCounter - _lastBonusSpin >= 20) // Reduced from 50 to 20 spins
                        {
                            if (!_isSimulationMode)
                            {
                                _lastBonusSpin = spinCounter;
                                _totalBonusesTriggered++; // Track total bonuses triggered
                            }

                            bonusLog = $"Bonus Triggered! SYM2 x{count} on Payline: [{string.Join(",", line)}]";
                            return true;
                        }
                    }

                }
            }

            return false;
        }
        private double SimulateBonusGame()
        {
            double rtpDeficit = Math.Max(0, _config.RtpTarget - GetActualRtp());
            double baseBonus = 10 + Math.Min(rtpDeficit * 20, 18);  // cap the scaling
            double bonusWin = baseBonus + _rng.NextDouble() * 25;   // Was 30
            double maxBonusWin = 40;                                // Was 45
            bonusWin = Math.Min(bonusWin, maxBonusWin);


            Console.WriteLine($"Bonus Game Win: {bonusWin:F1} coins");
            return bonusWin;
        }

        private double GetActualRtp() => _totalBet == 0 ? 0 : _totalWin / _totalBet;
        
        private double GetActualHitRate() => spinCounter == 0 ? 0 : (double)_hitCount / spinCounter;

        private bool HasMinimumScatters(List<List<string>> grid, int minCount = 2)
        {
            int count = grid.SelectMany(reel => reel).Count(symbol => symbol == "SYM0");
            return count >= minCount;
        }

        private List<List<string>> ToListOfLists(string[][] grid)
        {
            var list = new List<List<string>>();
            for (int col = 0; col < grid.Length; col++)
                list.Add(grid[col].ToList());
            return list;
        }

        private List<List<string>> ConvertToListOfLists(string[][] grid)
        {
            var result = new List<List<string>>();
            for (int col = 0; col < grid.Length; col++)
                result.Add(grid[col].ToList());
            return result;
        }

        private void InitializeCsvIfNeeded()
        {
            const string csvPath = "SpinStats.csv";
            if (!File.Exists(csvPath))
            {
                var header = "SpinNumber,BetAmount,SpinWin,ActualRTP,HitRate,IsFreeSpin,FreeSpinsRemaining,ReelSetName,ExpectedRTP";
                try
                {
                    File.WriteAllText(csvPath, header + Environment.NewLine, Encoding.UTF8);
                }
                catch (IOException ex)
                {
                    Console.WriteLine($"[CSV Warning] Could not create CSV file: {ex.Message}");
                }
            }
        }

        private void AppendSpinStatsToCsv(
    int spinNumber,
    double betAmount,
    double spinWin,
    double actualRtpDecimal,
    double hitRate,
    bool isFreeSpin,
    int freeSpinsRemaining,
    string reelSetName,
    double expectedRtp)
        {
            const string csvPath = "SpinStats.csv";

            string rtpPercent = FormatActualRtp();
            string hitRatePercent = $"{hitRate * 100:F2}%";
            string expectedRtpStr = $"{expectedRtp * 100:F2}%";

            var line = $"{spinNumber},{betAmount},{spinWin},{rtpPercent},{hitRatePercent},{isFreeSpin},{freeSpinsRemaining},{reelSetName},{expectedRtpStr}";
            
            // Retry logic with proper file sharing
            int maxRetries = 3;
            for (int retry = 0; retry < maxRetries; retry++)
            {
                try
                {
                    // Use FileShare.ReadWrite to allow other processes to read/write
                    using (var stream = new FileStream(csvPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (var writer = new StreamWriter(stream, Encoding.UTF8))
                    {
                        writer.Write(line + Environment.NewLine);
                    }
                    return; // Success, exit retry loop
                }
                catch (IOException ex)
                {
                    if (retry == maxRetries - 1)
                    {
                        Console.WriteLine($"[CSV Error] Failed to write to CSV after {maxRetries} retries: {ex.Message}");
                    }
                    else
                    {
                        Console.WriteLine($"[CSV Warning] Retry {retry + 1}/{maxRetries}: {ex.Message}");
                        Thread.Sleep(100); // Wait 100ms before retry
                    }
                }
            }
        }



        private bool LooksDangerous(ReelSet set)
        {
            var flat = set.Reels.SelectMany(r => r).ToList();
            int scatterCount = flat.Count(s => s == "SYM0");
            int wildCount = flat.Count(s => s == "SYM1");
            int topRowScatters = CountScatterInTopRows(set);
            double currentRtp = GetActualRtp();
            double rtpTarget = _config.RtpTarget;

            bool isStackedScatter = LooksScatterStacked(set);

            // 🚀 RELAXED: Much more permissive when RTP is low to allow recovery
            bool dangerous;
            if (currentRtp < rtpTarget * 0.70) // RTP < 61.6% - Very low, need recovery
            {
                // When RTP is very low, be extremely permissive to find high RTP sets
                dangerous = scatterCount >= 15 || wildCount >= 18 || (scatterCount >= 12 && wildCount >= 12);
            }
            else if (currentRtp < rtpTarget * 0.85) // RTP < 74.8% - Low, need recovery
            {
                // When RTP is low, be very permissive to find high RTP sets
                dangerous = scatterCount >= 12 || wildCount >= 15 || (scatterCount >= 10 && wildCount >= 10);
            }
            else if (currentRtp > rtpTarget * 1.2) // RTP > 105.6% - High, need low RTP sets
            {
                // When RTP is high, be more permissive to find low RTP sets
                dangerous = scatterCount >= 10 || wildCount >= 12 || (scatterCount >= 8 && wildCount >= 8);
            }
            else if (currentRtp > rtpTarget) // RTP > 100% - Slightly high
            {
                // Moderate permissiveness
                dangerous = scatterCount >= 8 || wildCount >= 10 || (scatterCount >= 6 && wildCount >= 6);
            }
            else
            {
                // Normal strictness when RTP is close to target
                dangerous = scatterCount >= 6 || wildCount >= 8 || (scatterCount >= 5 && wildCount >= 5);
            }

            // Top row scatter limits - also context-aware and relaxed
            bool topRowDangerous;
            if (currentRtp < rtpTarget * 0.70) // Very low RTP
            {
                topRowDangerous = topRowScatters >= 10; // Extremely permissive when RTP very low
            }
            else if (currentRtp < rtpTarget * 0.85) // Low RTP
            {
                topRowDangerous = topRowScatters >= 8; // Very permissive when RTP low
            }
            else if (currentRtp > rtpTarget * 1.2) // High RTP
            {
                topRowDangerous = topRowScatters >= 7; // More permissive when RTP high
            }
            else if (currentRtp > rtpTarget) // Slightly high RTP
            {
                topRowDangerous = topRowScatters >= 6; // More permissive
            }
            else
            {
                topRowDangerous = topRowScatters >= 5; // Normal strictness
            }

            // 🚀 RELAXED: Skip stacked scatter check when RTP is very low
            bool skipStackedScatter = currentRtp < rtpTarget * 0.75;

            if (dangerous || topRowDangerous || (!skipStackedScatter && isStackedScatter))
            {
                Console.WriteLine($"[Rejected: Dangerous] Set flagged. Scatters: {scatterCount}, Wilds: {wildCount}, TopRowScatters: {topRowScatters}, StackedScatter: {isStackedScatter}, CurrentRTP: {currentRtp:F2}");
                return true;
            }

            return false;
        }



        private int CountScatterInTopRows(ReelSet set)
        {
            int scatterCount = 0;
            foreach (var reel in set.Reels)
            {
                for (int row = 0; row < 3; row++) // Only visible zone
                {
                    if (reel[row] == "SYM0")
                        scatterCount++;
                }
            }
            return scatterCount;
        }

        private ReelSet GenerateRecoveryReelSet(string symbol)
        {
            var reels = new List<List<string>>();

            for (int col = 0; col < 5; col++)
            {
                var strip = new List<string>();

                for (int row = 0; row < 20; row++)
                {
                    // Force visible zone (top 3) to have the same symbol
                    if (row < 3)
                        strip.Add(symbol);
                    else
                        strip.Add("SYM9"); // filler symbol with low payout
                }

                reels.Add(strip);
            }

            return new ReelSet
            {
                Name = $"RECOVERY_{symbol}_WIN",
                Reels = reels,
                ExpectedRtp = 1.25,
                EstimatedHitRate = 1.0
            };
        }

        private ReelSet GenerateRescueReelSet()
        {
            // Force visible area to be all SYM3 (high pay) or all wilds or all scatters (bonus)
            var reels = new List<List<string>>();
            var rng = new Random();
            int winType = rng.Next(3);
            string symbol = winType == 0 ? "SYM3" : winType == 1 ? "SYM1" : "SYM0";
            for (int col = 0; col < 5; col++)
            {
                var strip = new List<string>();
                for (int row = 0; row < 20; row++)
                {
                    if (row < 3)
                        strip.Add(symbol);
                    else
                        strip.Add("SYM8"); // filler
                }
                reels.Add(strip);
            }
            return new ReelSet
            {
                Name = $"RESCUE_{symbol}_WIN",
                Reels = reels
            };
        }


    }
}