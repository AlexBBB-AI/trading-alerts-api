using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Strategies;

namespace NinjaTrader.NinjaScript.Strategies
{
    public class SMCStrategy : Strategy
    {
        // ── ORB levels ──
        private double orbHigh;
        private double orbLow;
        private bool orbSet;

        // ── Indicators ──
        private SMA smaFast;
        private SMA smaSlow;
        private ATR atr;

        // ── Daily tracking ──
        private int dailyTradeCount;
        private double dailyPnL;
        private DateTime lastTradeDate;
        private DateTime lastStopTime;
        private bool coolingDown;

        // ── FVG tracking ──
        private List<double> bullFVGTop;
        private List<double> bullFVGBot;
        private List<double> bearFVGTop;
        private List<double> bearFVGBot;

        // ── Supply/Demand zones ──
        private List<double> demandZoneHigh;
        private List<double> demandZoneLow;
        private List<double> supplyZoneHigh;
        private List<double> supplyZoneLow;

        // ── Sweep tracking ──
        private bool orbHighSwept;
        private bool orbLowSwept;
        private DateTime lastSweepTime;
        private double sweepLevel;
        private int sweepDirection; // 1 = swept high (look short), -1 = swept low (look long)

        // ── Swing tracking for structure ──
        private double lastSwingHigh;
        private double lastSwingLow;

        // ── Max zones to track ──
        private const int MAX_ZONES = 10;

        #region Parameters

        [NinjaScriptProperty]
        [Display(Name = "ORB Minutes", Order = 1, GroupName = "Parameters")]
        public int ORBMinutes { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Stop Loss Ticks", Order = 2, GroupName = "Parameters")]
        public int StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Profit Target Ticks", Order = 3, GroupName = "Parameters")]
        public int ProfitTargetTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trail Stop Ticks", Order = 4, GroupName = "Parameters")]
        public int TrailStopTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Max Daily Trades", Order = 5, GroupName = "Parameters")]
        public int MaxDailyTrades { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Max Daily Loss", Order = 6, GroupName = "Parameters")]
        public double MaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Cooldown Minutes", Order = 7, GroupName = "Parameters")]
        public int CooldownMinutes { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Min ORB Range Ticks", Order = 8, GroupName = "Parameters")]
        public int MinORBRangeTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Sweep Reclaim Bars", Order = 9, GroupName = "Parameters")]
        public int SweepReclaimBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "FVG Min Ticks", Order = 10, GroupName = "Parameters")]
        public int FVGMinTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Zone Lookback Bars", Order = 11, GroupName = "Parameters")]
        public int ZoneLookbackBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Min Score To Trade", Order = 12, GroupName = "Parameters")]
        public int MinScoreToTrade { get; set; }

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "SMC Strategy v1 - Supply/Demand + FVG + Liquidity Sweeps";
                Name = "SMCStrategy";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsFillLimitOnTouch = false;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                Slippage = 1;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Gtc;
                TraceOrders = true;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 30;
                IsInstantiatedOnEachOptimizationIteration = true;

                // Defaults
                ORBMinutes = 15;
                StopLossTicks = 24;
                ProfitTargetTicks = 48;
                TrailStopTicks = 16;
                MaxDailyTrades = 8;
                MaxDailyLoss = 300;
                CooldownMinutes = 10;
                MinORBRangeTicks = 8;
                SweepReclaimBars = 3;   // bars to confirm sweep reclaim
                FVGMinTicks = 4;        // minimum FVG size in ticks
                ZoneLookbackBars = 50;  // bars to look back for S/D zones
                MinScoreToTrade = 3;    // minimum confluence score (out of 5)
            }
            else if (State == State.DataLoaded)
            {
                smaFast = SMA(9);
                smaSlow = SMA(21);
                atr = ATR(14);

                bullFVGTop = new List<double>();
                bullFVGBot = new List<double>();
                bearFVGTop = new List<double>();
                bearFVGBot = new List<double>();

                demandZoneHigh = new List<double>();
                demandZoneLow = new List<double>();
                supplyZoneHigh = new List<double>();
                supplyZoneLow = new List<double>();

                Print("SMCStrategy v1 LOADED | ORB=" + ORBMinutes + "min | SL=" + StopLossTicks
                    + " | TP=" + ProfitTargetTicks + " | Trail=" + TrailStopTicks
                    + " | MinScore=" + MinScoreToTrade + " | SweepReclaim=" + SweepReclaimBars
                    + " | FVGMin=" + FVGMinTicks);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < BarsRequiredToTrade) return;

            DateTime barTime = Time[0];
            DateTime today = barTime.Date;

            // ── New day reset ──
            if (today != lastTradeDate)
            {
                lastTradeDate = today;
                dailyTradeCount = 0;
                dailyPnL = 0;
                orbSet = false;
                orbHigh = double.MinValue;
                orbLow = double.MaxValue;
                coolingDown = false;
                orbHighSwept = false;
                orbLowSwept = false;
                sweepDirection = 0;
                lastSwingHigh = 0;
                lastSwingLow = double.MaxValue;

                bullFVGTop.Clear();
                bullFVGBot.Clear();
                bearFVGTop.Clear();
                bearFVGBot.Clear();
                demandZoneHigh.Clear();
                demandZoneLow.Clear();
                supplyZoneHigh.Clear();
                supplyZoneLow.Clear();

                Print("=== NEW DAY: " + today.ToString("yyyy-MM-dd") + " ===");
            }

            int barTOD = ToTime(Time[0]);
            int orbStartTOD = 93000;
            int orbEndTOD = 93000 + (ORBMinutes * 100);
            if (ORBMinutes == 5) orbEndTOD = 93500;
            if (ORBMinutes == 15) orbEndTOD = 94500;
            if (ORBMinutes == 30) orbEndTOD = 100000;
            int tradeEndTOD = 154500;

            // ── Build ORB ──
            if (barTOD >= orbStartTOD && barTOD <= orbEndTOD)
            {
                if (High[0] > orbHigh) orbHigh = High[0];
                if (Low[0] < orbLow) orbLow = Low[0];
                return;
            }

            // ── Set ORB ──
            if (barTOD > orbEndTOD && !orbSet && orbHigh != double.MinValue)
            {
                orbSet = true;
                double rangeTicks = (orbHigh - orbLow) / TickSize;
                Print("*** ORB SET *** High: " + orbHigh + " Low: " + orbLow + " Range: " + rangeTicks + " ticks");
                if (rangeTicks < MinORBRangeTicks)
                {
                    Print("!!! ORB RANGE TOO SMALL (" + rangeTicks + " < " + MinORBRangeTicks + ") - SKIPPING DAY");
                    orbSet = false;
                    return;
                }
            }

            if (!orbSet) return;
            if (barTOD > tradeEndTOD) return;

            // ══════════════════════════════════════
            // STEP 1: Detect Fair Value Gaps (FVGs)
            // ══════════════════════════════════════
            DetectFVG();

            // ══════════════════════════════════════
            // STEP 2: Detect Supply/Demand Zones
            // ══════════════════════════════════════
            DetectSupplyDemandZones();

            // ══════════════════════════════════════
            // STEP 3: Track Swing Structure
            // ══════════════════════════════════════
            UpdateSwingPoints();

            // ══════════════════════════════════════
            // STEP 4: Detect Liquidity Sweeps
            // ══════════════════════════════════════
            DetectSweeps(barTime);

            // ══════════════════════════════════════
            // STEP 5: Remove filled FVGs
            // ══════════════════════════════════════
            CleanFilledFVGs();

            // ══════════════════════════════════════
            // STEP 6: Entry Logic (Confluence Scoring)
            // ══════════════════════════════════════
            if (dailyTradeCount >= MaxDailyTrades) return;
            if (MaxDailyLoss > 0 && dailyPnL <= -MaxDailyLoss) return;

            // Cooldown check
            if (coolingDown)
            {
                TimeSpan elapsed = barTime - lastStopTime;
                if (elapsed.TotalMinutes >= CooldownMinutes)
                {
                    coolingDown = false;
                    Print("--- Cooldown over at " + barTime.ToString("HH:mm:ss") + " ---");
                }
                else return;
            }

            if (Position.MarketPosition != MarketPosition.Flat) return;

            // ── Score LONG setup ──
            int longScore = ScoreLongSetup();
            // ── Score SHORT setup ──
            int shortScore = ScoreShortSetup();

            // Only take the stronger signal if both qualify
            if (longScore >= MinScoreToTrade && longScore > shortScore)
            {
                EnterLong(1, "SMC Long");
                dailyTradeCount++;
                SetStopLoss("SMC Long", CalculationMode.Ticks, StopLossTicks, false);
                SetProfitTarget("SMC Long", CalculationMode.Ticks, ProfitTargetTicks);
                if (TrailStopTicks > 0)
                    SetTrailStop("SMC Long", CalculationMode.Ticks, TrailStopTicks, false);

                Print(">>> LONG #" + dailyTradeCount + " @ " + Close[0]
                    + " | Score: " + longScore + "/5"
                    + " | SMA9=" + smaFast[0].ToString("F2") + " SMA21=" + smaSlow[0].ToString("F2")
                    + " | ATR=" + atr[0].ToString("F2")
                    + " | Sweep=" + (sweepDirection == -1 ? "YES" : "no")
                    + " | FVG=" + IsInBullFVG()
                    + " | Demand=" + IsNearDemandZone());
            }
            else if (shortScore >= MinScoreToTrade && shortScore > longScore)
            {
                EnterShort(1, "SMC Short");
                dailyTradeCount++;
                SetStopLoss("SMC Short", CalculationMode.Ticks, StopLossTicks, false);
                SetProfitTarget("SMC Short", CalculationMode.Ticks, ProfitTargetTicks);
                if (TrailStopTicks > 0)
                    SetTrailStop("SMC Short", CalculationMode.Ticks, TrailStopTicks, false);

                Print(">>> SHORT #" + dailyTradeCount + " @ " + Close[0]
                    + " | Score: " + shortScore + "/5"
                    + " | SMA9=" + smaFast[0].ToString("F2") + " SMA21=" + smaSlow[0].ToString("F2")
                    + " | ATR=" + atr[0].ToString("F2")
                    + " | Sweep=" + (sweepDirection == 1 ? "YES" : "no")
                    + " | FVG=" + IsInBearFVG()
                    + " | Supply=" + IsNearSupplyZone());
            }
        }

        // ══════════════════════════════════════════
        // FVG DETECTION
        // A bullish FVG: bar[2].high < bar[0].low (gap up)
        // A bearish FVG: bar[2].low > bar[0].high (gap down)
        // ══════════════════════════════════════════
        private void DetectFVG()
        {
            if (CurrentBar < 3) return;

            double fvgMinSize = FVGMinTicks * TickSize;

            // Bullish FVG: candle 2 ago high < current candle low
            // Gap between High[2] and Low[0], middle candle is the impulse
            if (Low[0] > High[2] && (Low[0] - High[2]) >= fvgMinSize)
            {
                if (bullFVGTop.Count < MAX_ZONES)
                {
                    bullFVGTop.Add(Low[0]);
                    bullFVGBot.Add(High[2]);
                    Print("  [FVG] BULL gap: " + High[2].ToString("F2") + " - " + Low[0].ToString("F2"));
                }
            }

            // Bearish FVG: candle 2 ago low > current candle high
            if (High[0] < Low[2] && (Low[2] - High[0]) >= fvgMinSize)
            {
                if (bearFVGTop.Count < MAX_ZONES)
                {
                    bearFVGTop.Add(Low[2]);
                    bearFVGBot.Add(High[0]);
                    Print("  [FVG] BEAR gap: " + High[0].ToString("F2") + " - " + Low[2].ToString("F2"));
                }
            }
        }

        // ══════════════════════════════════════════
        // SUPPLY/DEMAND ZONE DETECTION
        // Demand: strong bullish bar after consolidation/down move
        // Supply: strong bearish bar after consolidation/up move
        // ══════════════════════════════════════════
        private void DetectSupplyDemandZones()
        {
            if (CurrentBar < 5) return;

            double barRange = High[0] - Low[0];
            double avgRange = atr[0];

            // Strong bullish engulfing = potential demand zone (base of the move)
            if (Close[0] > Open[0] && barRange > avgRange * 1.5
                && Close[1] < Open[1]) // preceded by bearish bar
            {
                // Demand zone = the bearish bar before the impulse
                if (demandZoneHigh.Count < MAX_ZONES)
                {
                    demandZoneHigh.Add(Math.Max(Open[1], Close[1]));
                    demandZoneLow.Add(Low[1]);
                    Print("  [S/D] DEMAND zone: " + Low[1].ToString("F2") + " - " + Math.Max(Open[1], Close[1]).ToString("F2"));
                }
            }

            // Strong bearish engulfing = potential supply zone
            if (Close[0] < Open[0] && barRange > avgRange * 1.5
                && Close[1] > Open[1]) // preceded by bullish bar
            {
                if (supplyZoneHigh.Count < MAX_ZONES)
                {
                    supplyZoneHigh.Add(High[1]);
                    supplyZoneLow.Add(Math.Min(Open[1], Close[1]));
                    Print("  [S/D] SUPPLY zone: " + Math.Min(Open[1], Close[1]).ToString("F2") + " - " + High[1].ToString("F2"));
                }
            }
        }

        // ══════════════════════════════════════════
        // SWING POINT TRACKING
        // ══════════════════════════════════════════
        private void UpdateSwingPoints()
        {
            if (CurrentBar < 5) return;

            // Swing high: bar[2] higher than bars around it
            if (High[2] > High[3] && High[2] > High[1])
            {
                lastSwingHigh = High[2];
            }

            // Swing low: bar[2] lower than bars around it
            if (Low[2] < Low[3] && Low[2] < Low[1])
            {
                lastSwingLow = Low[2];
            }
        }

        // ══════════════════════════════════════════
        // LIQUIDITY SWEEP DETECTION
        // Sweep = price pokes beyond key level then closes back
        // ══════════════════════════════════════════
        private void DetectSweeps(DateTime barTime)
        {
            // Sweep of ORB High: wick above but close below
            if (!orbHighSwept && High[0] > orbHigh && Close[0] < orbHigh)
            {
                orbHighSwept = true;
                sweepDirection = 1; // swept high = bearish (look short)
                sweepLevel = orbHigh;
                lastSweepTime = barTime;
                Print("  [SWEEP] ORB HIGH swept! High=" + High[0].ToString("F2") + " Close=" + Close[0].ToString("F2") + " below " + orbHigh.ToString("F2"));
            }

            // Sweep of ORB Low: wick below but close above
            if (!orbLowSwept && Low[0] < orbLow && Close[0] > orbLow)
            {
                orbLowSwept = true;
                sweepDirection = -1; // swept low = bullish (look long)
                sweepLevel = orbLow;
                lastSweepTime = barTime;
                Print("  [SWEEP] ORB LOW swept! Low=" + Low[0].ToString("F2") + " Close=" + Close[0].ToString("F2") + " above " + orbLow.ToString("F2"));
            }

            // Sweep of swing high
            if (lastSwingHigh > 0 && High[0] > lastSwingHigh && Close[0] < lastSwingHigh)
            {
                sweepDirection = 1;
                sweepLevel = lastSwingHigh;
                lastSweepTime = barTime;
                Print("  [SWEEP] Swing High swept @ " + lastSwingHigh.ToString("F2"));
            }

            // Sweep of swing low
            if (lastSwingLow < double.MaxValue && lastSwingLow > 0 && Low[0] < lastSwingLow && Close[0] > lastSwingLow)
            {
                sweepDirection = -1;
                sweepLevel = lastSwingLow;
                lastSweepTime = barTime;
                Print("  [SWEEP] Swing Low swept @ " + lastSwingLow.ToString("F2"));
            }

            // Expire sweep signal after N bars
            if (sweepDirection != 0 && lastSweepTime != DateTime.MinValue)
            {
                TimeSpan sinceSweep = barTime - lastSweepTime;
                if (sinceSweep.TotalMinutes > SweepReclaimBars * 5) // roughly N bars at 1-5min
                {
                    sweepDirection = 0;
                }
            }
        }

        // ══════════════════════════════════════════
        // CLEAN FILLED FVGs (price traded through them)
        // ══════════════════════════════════════════
        private void CleanFilledFVGs()
        {
            // Remove bull FVGs that got filled (price went below bottom)
            for (int i = bullFVGBot.Count - 1; i >= 0; i--)
            {
                if (Low[0] < bullFVGBot[i])
                {
                    bullFVGTop.RemoveAt(i);
                    bullFVGBot.RemoveAt(i);
                }
            }

            // Remove bear FVGs that got filled (price went above top)
            for (int i = bearFVGTop.Count - 1; i >= 0; i--)
            {
                if (High[0] > bearFVGTop[i])
                {
                    bearFVGTop.RemoveAt(i);
                    bearFVGBot.RemoveAt(i);
                }
            }
        }

        // ══════════════════════════════════════════
        // CONFLUENCE SCORING — LONG
        // Each factor = 1 point. Need MinScoreToTrade to enter.
        // ══════════════════════════════════════════
        private int ScoreLongSetup()
        {
            int score = 0;

            // 1. Trend: SMA9 > SMA21 (bullish structure)
            if (smaFast[0] > smaSlow[0])
            {
                score++;
            }

            // 2. Liquidity sweep of lows (swept low = smart money grabbed stops, now reversing up)
            if (sweepDirection == -1)
            {
                score++;
            }

            // 3. Price in or near a bullish FVG (price filling into gap = magnet)
            if (IsInBullFVG())
            {
                score++;
            }

            // 4. Price at/near demand zone
            if (IsNearDemandZone())
            {
                score++;
            }

            // 5. Bullish price action: current bar closes bullish + above ORB midpoint
            double orbMid = (orbHigh + orbLow) / 2;
            if (Close[0] > Open[0] && Close[0] > orbMid)
            {
                score++;
            }

            return score;
        }

        // ══════════════════════════════════════════
        // CONFLUENCE SCORING — SHORT
        // ══════════════════════════════════════════
        private int ScoreShortSetup()
        {
            int score = 0;

            // 1. Trend: SMA9 < SMA21 (bearish structure)
            if (smaFast[0] < smaSlow[0])
            {
                score++;
            }

            // 2. Liquidity sweep of highs (swept high = smart money grabbed stops, now reversing down)
            if (sweepDirection == 1)
            {
                score++;
            }

            // 3. Price in or near a bearish FVG
            if (IsInBearFVG())
            {
                score++;
            }

            // 4. Price at/near supply zone
            if (IsNearSupplyZone())
            {
                score++;
            }

            // 5. Bearish price action: current bar closes bearish + below ORB midpoint
            double orbMid = (orbHigh + orbLow) / 2;
            if (Close[0] < Open[0] && Close[0] < orbMid)
            {
                score++;
            }

            return score;
        }

        // ── Helper: Is price in a bullish FVG zone? ──
        private bool IsInBullFVG()
        {
            double tolerance = 2 * TickSize;
            for (int i = 0; i < bullFVGBot.Count; i++)
            {
                if (Close[0] >= bullFVGBot[i] - tolerance && Close[0] <= bullFVGTop[i] + tolerance)
                    return true;
            }
            return false;
        }

        // ── Helper: Is price in a bearish FVG zone? ──
        private bool IsInBearFVG()
        {
            double tolerance = 2 * TickSize;
            for (int i = 0; i < bearFVGBot.Count; i++)
            {
                if (Close[0] >= bearFVGBot[i] - tolerance && Close[0] <= bearFVGTop[i] + tolerance)
                    return true;
            }
            return false;
        }

        // ── Helper: Is price near a demand zone? ──
        private bool IsNearDemandZone()
        {
            double tolerance = 4 * TickSize;
            for (int i = 0; i < demandZoneLow.Count; i++)
            {
                if (Close[0] >= demandZoneLow[i] - tolerance && Close[0] <= demandZoneHigh[i] + tolerance)
                    return true;
            }
            return false;
        }

        // ── Helper: Is price near a supply zone? ──
        private bool IsNearSupplyZone()
        {
            double tolerance = 4 * TickSize;
            for (int i = 0; i < supplyZoneLow.Count; i++)
            {
                if (Close[0] >= supplyZoneLow[i] - tolerance && Close[0] <= supplyZoneHigh[i] + tolerance)
                    return true;
            }
            return false;
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution.Order != null && execution.Order.OrderState == OrderState.Filled)
            {
                Print("FILL: " + execution.Order.Name + " @ " + price + " at " + time.ToString("HH:mm:ss"));
                if (Position.MarketPosition == MarketPosition.Flat)
                {
                    double lastPnl = SystemPerformance.AllTrades.Count > 0 ? SystemPerformance.AllTrades[SystemPerformance.AllTrades.Count - 1].ProfitCurrency : 0;
                    dailyPnL += lastPnl;
                    string result = lastPnl >= 0 ? "WIN" : "LOSS";
                    Print(">>> " + result + " $" + lastPnl.ToString("F2") + " | Daily PnL: $" + dailyPnL.ToString("F2") + " | Trades: " + dailyTradeCount);

                    if (execution.Order.Name == "Stop loss" || execution.Order.Name == "Trail stop")
                    {
                        coolingDown = true;
                        lastStopTime = time;
                        Print("!!! COOLDOWN STARTED - no entries for " + CooldownMinutes + " min until " + time.AddMinutes(CooldownMinutes).ToString("HH:mm:ss"));
                    }
                }
            }
        }
    }
}
