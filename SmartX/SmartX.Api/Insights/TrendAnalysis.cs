namespace SmartX.Api.Insights
{
    /// <summary>
    /// A straight line fitted through a device's recent readings.
    /// SlopePerMinute is how fast the value is moving; RSquared (0..1) is how well a straight line
    /// explains the movement - a steady decline scores near 1, random noise near 0.
    /// </summary>
    public readonly record struct TrendFit(double SlopePerMinute, double RSquared, double LastValue, int Points, double WindowMinutes);
    //----------------------------------------------------------//


    /// <summary>
    /// Ordinary least-squares regression over a timeline.
    /// The input has to be in time order for "the last value" and the window length to mean anything,
    /// which is exactly what the SortedDictionary timeline guarantees.
    /// </summary>
    public static class TrendAnalysis
    {
        public static TrendFit? Fit(IReadOnlyList<(DateTimeOffset TimestampUtc, double Value)> points, int minimumPoints = 8)
        {
            if (points.Count < minimumPoints)
                return null;

            var origin = points[0].TimestampUtc;
            double n = points.Count;
            double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;

            foreach (var (timestamp, value) in points)
            {
                double x = (timestamp - origin).TotalMinutes;
                sumX += x;
                sumY += value;
                sumXY += x * value;
                sumXX += x * x;
            }

            double denominator = n * sumXX - sumX * sumX;
            if (Math.Abs(denominator) < 1e-9)
                return null;

            double slope = (n * sumXY - sumX * sumY) / denominator;
            double intercept = (sumY - slope * sumX) / n;
            double meanY = sumY / n;

            double totalSquares = 0, residualSquares = 0;
            foreach (var (timestamp, value) in points)
            {
                double x = (timestamp - origin).TotalMinutes;
                double predicted = intercept + slope * x;
                totalSquares += (value - meanY) * (value - meanY);
                residualSquares += (value - predicted) * (value - predicted);
            }

            double rSquared = totalSquares < 1e-9 ? 0 : Math.Clamp(1 - residualSquares / totalSquares, 0, 1);
            double window = (points[^1].TimestampUtc - origin).TotalMinutes;

            return new TrendFit(slope, rSquared, points[^1].Value, points.Count, window);
        }
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
