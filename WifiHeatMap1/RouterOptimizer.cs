
using System;
using System.Collections.Generic;
using System.Windows;

namespace WifiHeatMap1
{
    public sealed class RouterOptimizationResult
    {
        public Point BestPosition { get; init; }
        public double BestCoveragePercent { get; init; }
        public double? PreviousCoveragePercent { get; init; }
        public int CandidateCount { get; init; }
        public int EvaluationPointCount { get; init; }
    }

    public static class RouterOptimizer
    {
        public static RouterOptimizationResult FindBest(
            double width,
            double height,
            IReadOnlyList<WallSegment> walls,
            double mapUnitsPerMeter,
            double targetRssi,
            Point? currentRouter)
        {
            if (width <= 0 ||
                height <= 0 ||
                mapUnitsPerMeter <= 0)
            {
                throw new ArgumentException(
                    "Invalid floor plan or calibration.");
            }

            double aspect = width / height;

            int candidateColumns = Math.Clamp(
                (int)Math.Round(Math.Sqrt(240 * aspect)),
                8, 30);

            int candidateRows = Math.Clamp(
                (int)Math.Round(240.0 / candidateColumns),
                8, 30);

            int sampleColumns = Math.Clamp(
                (int)Math.Round(Math.Sqrt(600 * aspect)),
                12, 40);

            int sampleRows = Math.Clamp(
                (int)Math.Round(600.0 / sampleColumns),
                12, 40);

            List<Point> receivers = new();

            for (int y = 0; y < sampleRows; y++)
            {
                for (int x = 0; x < sampleColumns; x++)
                {
                    receivers.Add(new Point(
                        (x + 0.5) * width / sampleColumns,
                        (y + 0.5) * height / sampleRows));
                }
            }

            double? previous = currentRouter.HasValue
                ? Score(
                    currentRouter.Value,
                    receivers,
                    walls,
                    mapUnitsPerMeter,
                    targetRssi)
                : null;

            Point best = default;
            double bestCoverage = -1;
            double bestMeanRssi = double.NegativeInfinity;
            int evaluated = 0;

            for (int row = 0; row < candidateRows; row++)
            {
                for (int col = 0;
                     col < candidateColumns;
                     col++)
                {
                    Point candidate = new(
                        (col + 0.5) * width / candidateColumns,
                        (row + 0.5) * height / candidateRows);

                    // Avoid positions immediately on a wall.
                    bool onWall = false;

                    foreach (WallSegment wall in walls)
                    {
                        if (DistanceToSegment(
                            candidate,
                            wall.Start,
                            wall.End)
                            < 0.35 * mapUnitsPerMeter)
                        {
                            onWall = true;
                            break;
                        }
                    }

                    if (onWall)
                        continue;

                    (double coverage, double mean) = Evaluate(
                        candidate,
                        receivers,
                        walls,
                        mapUnitsPerMeter,
                        targetRssi);

                    evaluated++;

                    if (coverage > bestCoverage + 1e-9 ||
                        (Math.Abs(coverage - bestCoverage)
                            <= 1e-9 &&
                         mean > bestMeanRssi))
                    {
                        best = candidate;
                        bestCoverage = coverage;
                        bestMeanRssi = mean;
                    }
                }
            }

            if (evaluated == 0)
            {
                throw new InvalidOperationException(
                    "No eligible router positions were found. " +
                    "Check wall geometry or scale.");
            }

            return new RouterOptimizationResult
            {
                BestPosition = best,
                BestCoveragePercent = bestCoverage,
                PreviousCoveragePercent = previous,
                CandidateCount = evaluated,
                EvaluationPointCount = receivers.Count
            };
        }

        private static double Score(
            Point router,
            List<Point> receivers,
            IReadOnlyList<WallSegment> walls,
            double scale,
            double target)
        {
            return Evaluate(
                router,
                receivers,
                walls,
                scale,
                target).coverage;
        }

        private static (double coverage, double mean) Evaluate(
            Point router,
            List<Point> receivers,
            IReadOnlyList<WallSegment> walls,
            double scale,
            double target)
        {
            int covered = 0;
            double total = 0;

            foreach (Point receiver in receivers)
            {
                double rssi = HeatMapRenderer.PredictRssi(
                    router,
                    receiver,
                    walls,
                    scale);

                if (rssi >= target)
                    covered++;

                total += rssi;
            }

            return (
                100.0 * covered / receivers.Count,
                total / receivers.Count);
        }

        private static double DistanceToSegment(
            Point p,
            Point a,
            Point b)
        {
            Vector ab = b - a;
            double squareLength = ab.LengthSquared;

            if (squareLength <= 1e-12)
                return (p - a).Length;

            double t = Math.Clamp(
                Vector.Multiply(p - a, ab) / squareLength,
                0, 1);

            return (p - (a + t * ab)).Length;
        }
    }
}
