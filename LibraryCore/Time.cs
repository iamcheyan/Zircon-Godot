using System;
using System.Diagnostics;

namespace Library
{
    public static class Time
    {
        private static readonly DateTime StartTime = DateTime.UtcNow;
        private static readonly Stopwatch Stopwatch = Stopwatch.StartNew();

        /// <summary>
        /// **仅用于测试**的时间偏移：设环境变量 ZIRCON_TIME_OFFSET_SECONDS=N 时，服务端/客户端的
        /// “当前时间”整体平移 N 秒。
        ///
        /// 动机（2026-10-18）：跨天行会重置那一族空引用崩溃只在 UTC 零点触发，而
        /// Time.Now 走的是 DateTime.UtcNow（不受 TZ 影响）、StartTime 又是 readonly，
        /// 无法在不改代码的前提下拨表 —— 于是这条最严重的缺陷一直只有代码级复核。
        /// 有了它就能真跑一次：把时间推到 23:59:5x，让服务端真实跨过零点，
        /// 观察在“20 个孤立行会成员”真实存在的情况下它是否还能存活。
        ///
        /// 生产环境不设该变量即为 0，行为与之前完全一致。
        /// </summary>
        private static readonly TimeSpan TestOffset = ReadTestOffset();

        private static TimeSpan ReadTestOffset()
        {
            string raw = Environment.GetEnvironmentVariable("ZIRCON_TIME_OFFSET_SECONDS");
            return int.TryParse(raw, out int seconds) ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
        }

        public static DateTime Now => StartTime + Stopwatch.Elapsed + TestOffset;
    }
}
