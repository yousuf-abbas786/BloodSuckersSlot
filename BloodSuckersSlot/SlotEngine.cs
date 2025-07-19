using System.Text;
using Microsoft.AspNetCore.SignalR.Client;
using BloodSuckersSlot.Api.Models;

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
        private const int MaxFreeSpinsPerSession = 50;
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



        public SpinResult Spin(int betAmount)
        {
            var spinStartTime = DateTime.Now;
            bool isFreeSpin = _freeSpinsRemaining > 0;
            double currentRtpBeforeSpin = GetActualRtp();

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

            var reelSets = GenerateRandomReelSets(); // 🔼 Reduced from 500 to 200 for faster processing

            if (isFreeSpin)
            {
                reelSets = reelSets.Where(r => r.Name.StartsWith("MidRtp")).ToList();
            }


            foreach (var reelSet in reelSets)
            {
                EstimateRtpAndHitRate(reelSet, 3000, betAmount); // 🔼 Increased to 3000 spins for better accuracy
                reelSet.RtpWeight = CalculateWeight(reelSet.ExpectedRtp, _config.RtpTarget);
                reelSet.HitWeight = CalculateWeight(reelSet.EstimatedHitRate, _config.TargetHitRate);
            }

            //double currentRtp = GetActualRtp();



            double rtpTarget = _config.RtpTarget;

            double widenFactor;
            if (currentRtpBeforeSpin < rtpTarget * 0.5)
                widenFactor = 0.25; // 🔼 Reduced from 0.85 to 0.25 for tighter control
            else if (currentRtpBeforeSpin < rtpTarget * 0.7)
                widenFactor = 0.20; // 🔼 Reduced from 0.65 to 0.20
            else if (currentRtpBeforeSpin < rtpTarget * 0.85)
                widenFactor = 0.15; // 🔼 Reduced from 0.45 to 0.15
            else if (currentRtpBeforeSpin > rtpTarget * 1.15)
                widenFactor = 0.10; // 🔼 Reduced from 0.15 to 0.10
            else
                widenFactor = 0.12; // 🔼 Reduced from 0.3 to 0.12 for tighter control

            var lowerBound = Math.Max(0.05, rtpTarget - widenFactor);
            var upperBound = Math.Min(2.0, rtpTarget + widenFactor);

            var healthySets = reelSets
                .Where(r =>
                    r.ExpectedRtp >= lowerBound &&
                    r.ExpectedRtp <= upperBound)
                .ToList();

            //        healthySets = healthySets
            //.OrderByDescending(r => HasMinimumScatters(ConvertToListOfLists(SpinReels(r.Reels)), 2) ? 1 : 0)
            //.ToList();



            if (healthySets.Count == 0)
            {
                Console.WriteLine("[Filter Fallback] No healthy scatter sets. Reverting to all reel sets.");
                healthySets = reelSets;
            }

            var chosenSet = ChooseWeighted(healthySets);


            // Optional: log symbols per reel set
            var flat = chosenSet.Reels.SelectMany(r => r).ToList();
            int sc = flat.Count(s => s == "SYM0");
            int wc = flat.Count(s => s == "SYM1");
            Console.WriteLine($"[ReelSet Analysis] {chosenSet.Name} | Scatters: {sc}, Wilds: {wc}");



            // ─── Retry Logic ────────────────
            string[][] grid = null;
            double lineWin = 0, scatterWin = 0, totalSpinWin = 0;
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
                lineWin += EvaluateWildLineWins(grid, _config.Paylines, out wildLineWins, out wildLogs);

                scatterWin = EvaluateScatters(grid, isFreeSpin, out scatterLog, out scatterCount, betAmount);
                totalSpinWin = (lineWin * (isFreeSpin ? 3 : 1)) + scatterWin;

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
                    double bonusScale = Math.Max(0.10, 1.0 - (currentRtpBeforeSpin - rtpTarget));
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
                    ReelSetsFiltered = 50 - healthySets.Count, // How many were filtered out
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

        private List<ReelSet> GenerateRandomReelSets(int count = 50) // 🔼 Reduced from 500 to 50 for much faster processing
        {
            var sets = new List<ReelSet>();
            var rng = new Random();

            for (int i = 0; i < count; i++)
            {
                var reels = new List<List<string>>();
                
                // Define symbol weights similar to the original approach
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

                // Create RTP-biased sets
                string tag;
                if (i < count * 0.3)
                {
                    tag = "LowRtp";
                    symbolWeights["SYM3"] -= 4;
                    symbolWeights["SYM4"] -= 4;
                    symbolWeights["SYM5"] -= 3;
                    symbolWeights["SYM1"] -= 2;
                    symbolWeights["SYM0"] -= 1;
                }
                else if (i < count * 0.6)
                {
                    tag = "MidRtp";
                }
                else
                {
                    tag = "HighRtp";
                    symbolWeights["SYM3"] += 10;
                    symbolWeights["SYM4"] += 8;
                    symbolWeights["SYM5"] += 6;
                    symbolWeights["SYM6"] += 5;
                    symbolWeights["SYM1"] += 2;
                    symbolWeights["SYM0"] += 2;
                }

                var weightedSymbols = symbolWeights
                    .SelectMany(kvp => Enumerable.Repeat(kvp.Key, kvp.Value))
                    .ToList();

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

                        // Strong symbol bias for visible area
                        if (tag == "HighRtp" && row < 3 && rng.NextDouble() < 0.4)
                        {
                            chosen = new[] { "SYM3", "SYM4", "SYM5", "SYM6", "SYM1" }[rng.Next(5)];
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

            // Fix widen factor calculation for high RTP scenarios
            double widenFactor;
            if (currentRtp > rtpTarget * 1.5) // RTP > 132%
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

            // When RTP is very high, be more permissive to find low RTP sets
            if (currentRtp > rtpTarget * 1.25) // RTP > 110%
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
            // When RTP is very low, be more permissive to find high RTP sets
            else if (currentRtp < rtpTarget * 0.75) // RTP < 66%
            {
                rtpLowerBound = Math.Max(0.05, rtpTarget * 0.5); // Allow sets as low as 50% of target
                rtpUpperBound = Math.Min(1.2, rtpTarget * 1.5); // Allow sets up to 150% of target
                disableScatterGuard = true;
                skipLooksDangerous = true;
            }
            else if (currentRtp < rtpTarget * 0.85) // RTP < 74.8%
            {
                rtpLowerBound = Math.Max(0.05, rtpTarget * 0.6); // Allow sets as low as 60% of target
                rtpUpperBound = Math.Min(1.2, rtpTarget * 1.4); // Allow sets up to 140% of target
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

            // 🧠 Fix 4: Hard caps - much more permissive
            if (r.ExpectedRtp < 0.30) reasons.Add("RTP < 0.30 (Too Low)"); // Much more permissive
            if (r.ExpectedRtp > 1.20 && spinCounter < 5) reasons.Add("RTP > 1.20 too early"); // Much more permissive

            if (!skipLooksDangerous && LooksDangerous(r)) reasons.Add("LooksDangerous");

            // Scatter/flood guards
            if (scatterStacked) reasons.Add("ScatterStacked");
            if (!disableScatterGuard && earlyScatterFlood) reasons.Add("Scatter3Early");
            if (!disableScatterGuard && hotScatterBomb) reasons.Add("Scatter4Hot");
            if (!disableScatterGuard && topRowScatterRtpRisk) reasons.Add("Scatter3Top_RTPHigh");

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

            // Fix widen factor calculation for high RTP scenarios
            double widenFactor;
            if (currentRtp > rtpTarget * 1.5) // RTP > 132%
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
            
            // Adjust hit rate bounds based on current performance
            double hitLowerBound, hitUpperBound;
            if (currentRtp < rtpTarget * 0.75) // RTP < 66%
            {
                // When RTP is low, be more permissive with hit rate to find high RTP sets
                hitLowerBound = Math.Max(0.05, hitRateTarget - 0.4); // Allow lower hit rates
                hitUpperBound = Math.Min(1.0, hitRateTarget + 0.4); // Allow higher hit rates
            }
            else
            {
                hitLowerBound = Math.Max(0.05, hitRateTarget - 0.3);
                hitUpperBound = Math.Min(1.0, hitRateTarget + 0.3);
            }

            if (currentRtp > rtpTarget * 1.05 && spinCounter > 20)
            {
                rtpUpperBound = Math.Min(rtpUpperBound, rtpTarget * 1.03);
                Console.WriteLine("[Clamp] Tightened upper RTP bound due to steady overshoot.");
            }

            var originalSets = sets;

            // Safe filtering - be much more aggressive when far from target
            if (Math.Abs(currentRtp - rtpTarget) > 0.15) // RTP is far from target (>15% deviation)
            {
                // When far from target, be very permissive
                sets = sets
                    .Where(r => r.ExpectedRtp >= 0.30 && r.ExpectedRtp <= 1.50) // Allow wide range
                    .ToList();
                Console.WriteLine($"[FAR FROM TARGET] Skipping IsSafeSet filtering due to RTP deviation {Math.Abs(currentRtp - rtpTarget):F2} > 0.15");
            }
            else
            {
                // Normal filtering when close to target
                sets = sets
                    .Where(r => IsSafeSet(r, currentRtp))
                    .ToList();
            }

            if (spinCounter < 10)
            {
                sets = sets
                    .Where(r => r.ExpectedRtp <= rtpTarget * 1.05 && !LooksDangerous(r))
                    .ToList();

                Console.WriteLine("[Opening Phase] Suppressing risky reel sets in first 10 spins.");
            }

            if (currentRtp > rtpTarget * 1.05 && spinCounter > 10) // RTP > 92.4%
            {
                // When RTP is above target, force LOW RTP sets
                double maxAllowedRtp = rtpTarget * 0.75; // Force sets below 75% of target (less aggressive)
                sets = sets
                    .Where(r => r.ExpectedRtp <= maxAllowedRtp) // Remove LooksDangerous check
                    .ToList();

                Console.WriteLine($"[ABOVE TARGET] Forcing LOW RTP sets (max {maxAllowedRtp:F2}) due to RTP {currentRtp:F2} > 92.4%");
            }
            else if (currentRtp > rtpTarget * 1.25 && spinCounter > 10) // RTP > 110%
            {
                // When RTP is high, force LOW RTP sets - NO SAFETY CHECKS!
                double maxAllowedRtp = rtpTarget * 0.65; // Force sets below 65% of target (less aggressive)
                sets = sets
                    .Where(r => r.ExpectedRtp <= maxAllowedRtp) // REMOVED LooksDangerous check
                    .ToList();

                Console.WriteLine($"[HOT MODE] Forcing LOW RTP sets (max {maxAllowedRtp:F2}) due to RTP {currentRtp:F2} > 110%");
            }
            else if (currentRtp > rtpTarget * 1.5 && spinCounter > 10) // RTP > 132%
            {
                // When RTP is extremely high, force VERY LOW RTP sets - NO SAFETY CHECKS!
                double maxAllowedRtp = rtpTarget * 0.40; // Force sets below 40% of target (more aggressive)
                sets = sets
                    .Where(r => r.ExpectedRtp <= maxAllowedRtp) // REMOVED LooksDangerous check
                    .ToList();

                Console.WriteLine($"[CRITICAL HOT MODE] Forcing VERY LOW RTP sets (max {maxAllowedRtp:F2}) due to RTP {currentRtp:F2} > 132%");
            }

            if (currentRtp < rtpTarget * 0.90 && spinCounter > 10) // RTP < 79.2%
            {
                // When RTP is below target, force HIGH RTP sets
                double minAllowedRtp = rtpTarget * 1.05; // Force sets above 105% of target (more aggressive)
                sets = sets
                    .Where(s => s.ExpectedRtp >= minAllowedRtp)
                    .ToList();

                Console.WriteLine($"[BELOW TARGET] Forcing HIGH RTP sets (min {minAllowedRtp:F2}) due to RTP {currentRtp:F2} < 79.2%");
            }
            else if (currentRtp < rtpTarget * 0.75 && spinCounter > 10) // RTP < 66%
            {
                // When RTP is very low, force HIGH RTP sets
                double minAllowedRtp = rtpTarget * 1.10; // Force sets above 110% of target (more aggressive)
                sets = sets
                    .Where(s => s.ExpectedRtp >= minAllowedRtp)
                    .ToList();

                Console.WriteLine($"[LOW RTP RECOVERY] Forcing HIGH RTP sets (min {minAllowedRtp:F2}) due to RTP {currentRtp:F2} < 66%");
            }
            else if (currentRtp < rtpTarget * 0.90 && spinCounter > 10)
            {
                sets = sets
                    .Where(s => s.ExpectedRtp >= rtpTarget * 0.95)
                    .ToList();

                Console.WriteLine("[Soft Recovery] Allowing moderately high RTP sets (RTP < 90%).");
            }

            // Hit rate control - when hit rate is too high, force low hit rate sets
            if (GetActualHitRate() > hitRateTarget * 1.2 && spinCounter > 10) // Hit rate > 42% (more aggressive)
            {
                double maxAllowedHitRate = hitRateTarget * 0.6; // Force sets below 60% of target hit rate (more aggressive)
                sets = sets
                    .Where(s => s.EstimatedHitRate <= maxAllowedHitRate)
                    .ToList();

                Console.WriteLine($"[HIGH HIT RATE] Forcing LOW hit rate sets (max {maxAllowedHitRate:F2}) due to hit rate {GetActualHitRate():F2} > 42%");
            }
            else if (currentRtp < 0.65 && spinCounter > 20)
            {
                Console.WriteLine("[Low RTP Recovery] Forcing HIGH RTP + High HitRate sets (RTP < 65%)");
                sets = sets
                    .Where(s =>
                        s.ExpectedRtp >= rtpTarget * 1.10 &&
                        s.EstimatedHitRate >= hitRateTarget * 1.2 &&
                        CountScatterInTopRows(s) >= 3)
                    .ToList();
            }

            // Retry final recovery before fallback
            if (!sets.Any())
            {
                sets = originalSets
                    .Where(r => r.ExpectedRtp >= rtpTarget * 0.80)
                    .ToList();

                Console.WriteLine("[Final Recovery Retry] Attempting to allow slightly lower sets.");
            }

            if (!sets.Any() && currentRtp < rtpTarget * 0.65)
            {
                sets = originalSets
                    .Where(r =>
                        r.ExpectedRtp >= 0.80 &&
                        !LooksDangerous(r) &&
                        !LooksScatterStacked(r))
                    .ToList();

                Console.WriteLine("[Recovery Expansion] Allowed moderate sets to reduce deflation.");
            }


            if (!sets.Any())
            {
                Console.WriteLine($"[CRITICAL FAILOVER] Returning SAFE_FALLBACK_REAL_SET due to complete set exhaustion. CurrentRTP: {currentRtp:F2}, OriginalSets: {originalSets.Count}");
                
                // Debug: Show why sets were rejected
                var rejectedSets = originalSets.Where(r => !IsSafeSet(r, currentRtp)).Take(5);
                foreach (var set in rejectedSets)
                {
                    Console.WriteLine($"[DEBUG REJECTED] {set.Name}: RTP={set.ExpectedRtp:F2}, HR={set.EstimatedHitRate:F2}");
                }
                
                return new ReelSet
                {
                    Name = "SAFE_FALLBACK_REAL_SET_FINAL",
                    Reels = GenerateSafeFallbackReels(),
                    ExpectedRtp = 0.65,
                    EstimatedHitRate = 0.10
                };
            }

            Console.WriteLine($"[Filter] RTP: {currentRtp:F2}, Candidates: {sets.Count}, Bounds: RTP[{rtpLowerBound:F2}-{rtpUpperBound:F2}], HR[{hitLowerBound:F2}-{hitUpperBound:F2}]");
            
            // Debug: Show RTP control mode
            if (currentRtp > rtpTarget * 1.05)
                Console.WriteLine($"[DEBUG] RTP CONTROL MODE: ABOVE TARGET (RTP {currentRtp:F2} > {rtpTarget * 1.05:F2})");
            else if (currentRtp < rtpTarget * 0.90)
                Console.WriteLine($"[DEBUG] RTP CONTROL MODE: BELOW TARGET (RTP {currentRtp:F2} < {rtpTarget * 0.90:F2})");
            else
                Console.WriteLine($"[DEBUG] RTP CONTROL MODE: NEAR TARGET (RTP {currentRtp:F2} near {rtpTarget:F2})");
            
            // Debug: Show available RTP ranges
            if (sets.Any())
            {
                var rtpRange = sets.Select(s => s.ExpectedRtp);
                Console.WriteLine($"[DEBUG] Available RTP range: {rtpRange.Min():F2} - {rtpRange.Max():F2}");
            }

            double totalWeight = 0;
            Dictionary<ReelSet, double> weightedMap = new();

            foreach (var set in sets)
            {
                double rtpDistance = Math.Abs(set.ExpectedRtp - rtpTarget);
                double hitDistance = Math.Abs(set.EstimatedHitRate - hitRateTarget);
                
                // AGGRESSIVE BIAS - strong correction for extreme deviations
                double bias;
                if (currentRtp > rtpTarget * 1.05) // RTP is above target
                {
                    // STRONGLY favor LOW RTP sets when RTP is high
                    bias = (set.ExpectedRtp <= rtpTarget * 0.70) ? 3.0 : 0.3;
                }
                else if (currentRtp < rtpTarget * 0.90) // RTP is below target
                {
                    // STRONGLY favor HIGH RTP sets when RTP is low
                    bias = (set.ExpectedRtp >= rtpTarget * 1.10) ? 3.0 : 0.3;
                }
                else
                {
                    // Normal bias when RTP is close to target
                    bias = (set.ExpectedRtp >= 0.80 && set.ExpectedRtp <= 1.05) ? 1.2 : 1.0;
                }
                
                double penalty = set.ExpectedRtp > rtpTarget ? Math.Pow(set.ExpectedRtp - rtpTarget, 2) * 4 : 0;
                double combinedScore = bias / (rtpDistance * 0.6 + hitDistance * 0.4 + penalty + 0.001);
                weightedMap[set] = combinedScore;
                totalWeight += combinedScore;
            }

            double pick = _rng.NextDouble() * totalWeight;
            double cumulative = 0;

            foreach (var kvp in weightedMap)
            {
                cumulative += kvp.Value;
                if (pick <= cumulative)
                    return kvp.Key;
            }

            return sets.OrderBy(r => Math.Abs(r.ExpectedRtp - rtpTarget)).First();
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
            var counted = new HashSet<string>();
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
                        if (symbol == baseSymbol || (isWild && !symbolConfigs[baseSymbol].IsScatter && !symbolConfigs[baseSymbol].IsBonus))
                        {
                            if (isWild) wildUsed = true;
                            matchCount++;
                            tempPositions.Add((col, line[col]));
                        }
                        else break;
                    }
                }

                if (matchCount >= 3 && symbolConfigs[baseSymbol].Payouts.TryGetValue(matchCount, out double payout))
                {
                    string key = $"{baseSymbol}-{matchCount}-{string.Join(",", line)}";

                    //Check if this symbol already won on another line
                    if (counted.Any(k => k.StartsWith($"{baseSymbol}-")))
                        continue;

                    if (!counted.Contains(key))
                    {
                        win += payout;
                        matchedPositions.AddRange(tempPositions);
                        counted.Add(key);

                        if (!_isSimulationMode)
                        {
                            paylineLogs.Add(
                                $"Payline win on line [{string.Join(",", line)}]: {baseSymbol} x{matchCount} => {payout} coins" +
                                (wildUsed ? " (with wilds)" : ""));
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
                int remainingAllowance = MaxFreeSpinsPerSession - _freeSpinsAwarded;
                int toAward = Math.Min(remainingAllowance, freeSpinsAwarded);
                if (toAward > 0)
                {
                    _freeSpinsRemaining += toAward;
                    _freeSpinsAwarded += toAward;
                    _totalFreeSpinsAwarded += toAward; // Track total free spins awarded
                    scatterLog = $"Free Spins Triggered! SYM0 x{scatterCount} => +{toAward} Free Spins";
                }
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
                int count = 0;
                for (int col = 0; col < 5; col++)
                {
                    int row = line[col];
                    string symbol = grid[col][row];

                    if (symbol == "SYM1")
                        count++;
                    else
                        break; // must be consecutive
                }

                if (count >= 2 && symbolConfigs["SYM1"].Payouts.TryGetValue(count, out double payout))
                {
                    for (int col = 0; col < count; col++)
                        wildWinPositions.Add((col, line[col]));

                    wildWin += payout;

                    wildLogs.Add($"Wild-only win on line [{string.Join(",", line)}]: SYM1 x{count} => {payout} coins");
                }
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
                        if (_isSimulationMode || spinCounter - _lastBonusSpin >= 50) // Reduced from 250 to 50 spins
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

            // Be more permissive when RTP is high (we need low RTP sets)
            bool dangerous;
            if (currentRtp > rtpTarget * 1.2) // RTP > 105.6%
            {
                // When RTP is high, be more permissive to find low RTP sets
                dangerous = scatterCount >= 10 || wildCount >= 12 || (scatterCount >= 8 && wildCount >= 8);
            }
            else if (currentRtp > rtpTarget) // RTP > 100%
            {
                // Moderate permissiveness
                dangerous = scatterCount >= 8 || wildCount >= 10 || (scatterCount >= 6 && wildCount >= 6);
            }
            else
            {
                // Normal strictness when RTP is low
                dangerous = scatterCount >= 6 || wildCount >= 8 || (scatterCount >= 5 && wildCount >= 5);
            }

            // Top row scatter limits - also context-aware
            bool topRowDangerous;
            if (currentRtp > rtpTarget * 1.2)
            {
                topRowDangerous = topRowScatters >= 7; // Much more permissive when RTP high
            }
            else if (currentRtp > rtpTarget)
            {
                topRowDangerous = topRowScatters >= 6; // More permissive
            }
            else
            {
                topRowDangerous = topRowScatters >= 5; // More permissive
            }

            if (dangerous || topRowDangerous || isStackedScatter)
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



    }
}