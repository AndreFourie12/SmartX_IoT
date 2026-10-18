using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SmartX.Shared.Contracts;
using SmartX.Shared.Models;

namespace SmartX.Client.Controls
{
    /// <summary>
    /// The selected device's timeline: readings over the chosen window, with the warning and critical
    /// thresholds shaded so an operator can see at a glance how close a value is to an alarm.
    ///
    /// The points arrive already in timestamp order - the API reads them out of a SortedDictionary - so
    /// this only has to map each one to a pixel and draw a single path. No sorting, no per-point elements.
    /// Readings that land within a few pixels of each other are averaged into one point, so dense bursts
    /// stay readable instead of turning into vertical zigzags.
    /// </summary>
    public sealed class TimelineChart : FrameworkElement
    {
        public static readonly DependencyProperty TimelineProperty = DependencyProperty.Register(
            nameof(Timeline), typeof(TimelineDto), typeof(TimelineChart),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public TimelineDto? Timeline
        {
            get => (TimelineDto?)GetValue(TimelineProperty);
            set => SetValue(TimelineProperty, value);
        }

        private static readonly Brush AxisText = Frozen(Color.FromRgb(0x6B, 0x72, 0x80));
        private static readonly Brush GridBrush = Frozen(Color.FromRgb(0xEE, 0xF1, 0xF5));
        private static readonly Brush LineBrush = Frozen(Color.FromRgb(0x25, 0x63, 0xEB));
        private static readonly Brush CriticalFill = Frozen(Color.FromArgb(0x14, 0xDC, 0x26, 0x26));
        private static readonly Brush CriticalLine = Frozen(Color.FromRgb(0xDC, 0x26, 0x26));
        private static readonly Brush WarningText = Frozen(Color.FromRgb(0xB4, 0x53, 0x09));
        private static readonly Typeface LabelFace = new("Cascadia Mono, Consolas, Courier New");

        private const double PlotRight = 12;
        private const double PlotTop = 10;
        private const double PlotBottom = 24;

        // Smallest on-screen width of a time slot. Readings in the same slot are merged into one point.
        private const double SlotPixels = 6;


        protected override void OnRender(DrawingContext drawing)
        {
            double width = ActualWidth, height = ActualHeight;
            drawing.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));

            var timeline = Timeline;

            if (timeline is null || timeline.Points.Count == 0)
            {
                DrawText(drawing, timeline is null ? "Select a device to see its timeline." : "No readings in this window yet.",
                    new Point(width / 2, height / 2), 12.5, AxisText, centred: true, middle: true);
                return;
            }

            bool onOff = timeline.Category.IsActuator();
            var (min, max) = onOff ? (-0.15, 1.15) : ValueRange(timeline);
            var labels = AxisLabels(min, max, onOff, AxisSuffix(timeline.Unit));

            // The plot starts just right of the widest axis label, so "28.6%" is never cut off.
            double plotLeft = Math.Max(36, labels.Max(label => Format(label.Text, 10.5, AxisText).Width) + 14);
            var plot = new Rect(plotLeft, PlotTop, Math.Max(1, width - plotLeft - PlotRight), Math.Max(1, height - PlotTop - PlotBottom));

            double fromTicks = timeline.FromUtc.UtcTicks, toTicks = timeline.ToUtc.UtcTicks;
            double X(DateTimeOffset t) => plot.Left + (t.UtcTicks - fromTicks) / Math.Max(1, toTicks - fromTicks) * plot.Width;
            double Y(double v) => plot.Bottom - (v - min) / (max - min) * plot.Height;

            drawing.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));

            if (!onOff && timeline.Band is { } band)
                DrawBands(drawing, band, plot, min, max, AxisSuffix(timeline.Unit), Y);

            DrawGrid(drawing, plot, labels, Y);
            DrawTimeAxis(drawing, timeline, plot);
            DrawSeries(drawing, timeline, plot, onOff, X, Y);

            drawing.Pop();
        }
        //----------------------------------------------------------//


        /// <summary>
        /// The y-axis range: the data plus any nearby threshold, so a value approaching its limit is drawn
        /// with the limit in view.
        /// </summary>
        private static (double Min, double Max) ValueRange(TimelineDto timeline)
        {
            double min = timeline.Points.Min(point => point.Value);
            double max = timeline.Points.Max(point => point.Value);

            if (timeline.Band is { } band)
            {
                foreach (var limit in new[] { band.CriticalLow, band.WarningLow, band.WarningHigh, band.CriticalHigh })
                {
                    if (limit is { } value && value >= min - (max - min + 10) && value <= max + (max - min + 10))
                    {
                        min = Math.Min(min, value);
                        max = Math.Max(max, value);
                    }
                }
            }

            double padding = Math.Max((max - min) * 0.12, 1);
            return (min - padding, max + padding);
        }
        //----------------------------------------------------------//


        private void DrawBands(DrawingContext drawing, Shared.Streaming.ThresholdBand band, Rect plot, double min, double max, string suffix, Func<double, double> y)
        {
            void Shade(double from, double to, Brush fill)
            {
                double top = Math.Clamp(y(Math.Max(from, to)), plot.Top, plot.Bottom);
                double bottom = Math.Clamp(y(Math.Min(from, to)), plot.Top, plot.Bottom);
                if (bottom - top > 0.5)
                    drawing.DrawRectangle(fill, null, new Rect(plot.Left, top, plot.Width, bottom - top));
            }

            // Dotted threshold line, named on the left: "Warning · 30%".
            void Line(double? value, string name, Brush brush)
            {
                if (value is not { } v || v < min || v > max)
                    return;

                var pen = new Pen(brush, 1) { DashStyle = DashStyles.Dot };
                pen.Freeze();
                drawing.DrawLine(pen, new Point(plot.Left, y(v)), new Point(plot.Right, y(v)));
                DrawText(drawing, $"{name} · {v.ToString("0.#", CultureInfo.InvariantCulture)}{suffix}", new Point(plot.Left + 8, y(v) - 16), 10.5, brush);
            }

            if (band.CriticalLow is { } critLow)
                Shade(min, critLow, CriticalFill);
            if (band.CriticalHigh is { } critHigh)
                Shade(critHigh, max, CriticalFill);

            Line(band.WarningLow, "Warning", WarningText);
            Line(band.CriticalLow, "Critical", CriticalLine);
            Line(band.WarningHigh, "Warning", WarningText);
            Line(band.CriticalHigh, "Critical", CriticalLine);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// The y-axis gridlines and their labels: OFF/ON for actuators, otherwise five evenly spaced values.
        /// </summary>
        private static List<(double Value, string Text)> AxisLabels(double min, double max, bool onOff, string suffix)
        {
            if (onOff)
                return [(0.0, "OFF"), (1.0, "ON")];

            string format = Math.Abs(max - min) < 10 ? "0.0" : "0";

            return Enumerable.Range(0, 5)
                .Select(i => min + (max - min) * i / 4)
                .Select(value => (value, value.ToString(format, CultureInfo.InvariantCulture) + suffix))
                .ToList();
        }
        //----------------------------------------------------------//


        private void DrawGrid(DrawingContext drawing, Rect plot, List<(double Value, string Text)> labels, Func<double, double> y)
        {
            var gridPen = new Pen(GridBrush, 1);
            gridPen.Freeze();

            foreach (var (value, text) in labels)
            {
                drawing.DrawLine(gridPen, new Point(plot.Left, y(value)), new Point(plot.Right, y(value)));
                DrawText(drawing, text, new Point(plot.Left - 8, y(value) - 7), 10.5, AxisText, rightAligned: true);
            }
        }
        //----------------------------------------------------------//


        private void DrawTimeAxis(DrawingContext drawing, TimelineDto timeline, Rect plot)
        {
            // Seconds only matter on short windows; longer ones read better as plain clock times.
            string format = timeline.ToUtc - timeline.FromUtc <= TimeSpan.FromMinutes(2) ? "HH:mm:ss" : "HH:mm";

            for (int i = 1; i <= 5; i++)
            {
                var at = timeline.FromUtc + (timeline.ToUtc - timeline.FromUtc) * ((i - 0.5) / 5);
                double x = plot.Left + plot.Width * (i - 0.5) / 5;
                DrawText(drawing, at.ToLocalTime().ToString(format, CultureInfo.InvariantCulture),
                    new Point(x, plot.Bottom + 7), 10.5, AxisText, centred: true);
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// One few-pixel-wide slice of the time axis and the readings that fell into it.
        /// </summary>
        private sealed class Slot
        {
            public int Count;
            public double Sum, Last, SumX;

            public double Mean => Sum / Count;
            public double X => SumX / Count;

            public void Add(double x, double value)
            {
                Count++;
                Sum += value;
                SumX += x;
                Last = value;
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Groups the readings into slots a few pixels wide. A burst of readings - a backfilled batch, a
        /// reconnect flush, several packets with the same timestamp - would otherwise be drawn as a stack of
        /// vertical zigzags in one column; per slot it becomes a single averaged point.
        ///
        /// Slots are fixed stretches of clock time (every 2 s, say), not fixed pixels. The window slides
        /// forward every second, and slots measured from its left edge would regroup the same old readings
        /// on every refresh, making the line twitch; clock-aligned slots always group them the same way.
        /// </summary>
        private static List<Slot> BuildSlots(IReadOnlyList<TimelinePointDto> points, TimelineDto timeline, Rect plot, Func<DateTimeOffset, double> x)
        {
            long slotTicks = SlotLength((timeline.ToUtc - timeline.FromUtc) * (SlotPixels / plot.Width)).Ticks;
            var slots = new List<Slot>();
            long currentKey = long.MinValue;

            // The points are already in timestamp order, so each slot is one contiguous run.
            foreach (var point in points)
            {
                long key = point.TimestampUtc.UtcTicks / slotTicks;
                if (key != currentKey)
                {
                    slots.Add(new Slot());
                    currentKey = key;
                }

                slots[^1].Add(x(point.TimestampUtc), point.Value);
            }

            return slots;
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Rounds the ideal slot length up to a tidy one, so small changes in the chart's width don't regroup
        /// the readings either.
        /// </summary>
        private static TimeSpan SlotLength(TimeSpan ideal)
        {
            foreach (double seconds in new[] { 0.1, 0.25, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300 })
            {
                if (ideal <= TimeSpan.FromSeconds(seconds))
                    return TimeSpan.FromSeconds(seconds);
            }

            return TimeSpan.FromMinutes(10);
        }
        //----------------------------------------------------------//


        private void DrawSeries(DrawingContext drawing, TimelineDto timeline, Rect plot, bool onOff, Func<DateTimeOffset, double> x, Func<double, double> y)
        {
            var points = timeline.Points;
            var slots = BuildSlots(points, timeline, plot, x);

            // The line ends exactly on the latest reading, so it meets the dot and the big value above the chart.
            var last = points[^1];
            var lastPoint = new Point(x(last.TimestampUtc), y(last.Value));

            Point At(int i) => i == slots.Count - 1
                ? lastPoint
                : new Point(slots[i].X, y(onOff ? slots[i].Last : slots[i].Mean));

            var line = new StreamGeometry();
            using (var context = line.Open())
            {
                context.BeginFigure(At(0), false, false);

                for (int i = 1; i < slots.Count; i++)
                {
                    var next = At(i);

                    // On/off devices step: hold the previous state until the change.
                    if (onOff)
                        context.LineTo(new Point(next.X, At(i - 1).Y), true, false);

                    context.LineTo(next, true, false);
                }
            }

            line.Freeze();

            var pen = new Pen(LineBrush, 1.8) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            drawing.DrawGeometry(null, pen, line);

            // Latest reading: a blue dot inside a white ring inside a blue ring.
            var ringPen = new Pen(LineBrush, 1.6);
            ringPen.Freeze();
            drawing.DrawEllipse(Brushes.White, ringPen, lastPoint, 6.5, 6.5);
            drawing.DrawEllipse(LineBrush, null, lastPoint, 3.6, 3.6);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Axis labels carry short units ("35%"); longer ones would crowd the axis, so they are left off.
        /// </summary>
        private static string AxisSuffix(string unit) => unit.Length is > 0 and <= 2 ? unit : string.Empty;
        //----------------------------------------------------------//


        private void DrawText(DrawingContext drawing, string text, Point at, double size, Brush brush, bool centred = false, bool rightAligned = false, bool bold = false, bool middle = false)
        {
            var formatted = Format(text, size, brush, bold);

            double x = centred ? at.X - formatted.Width / 2 : rightAligned ? at.X - formatted.Width : at.X;
            drawing.DrawText(formatted, new Point(x, middle ? at.Y - formatted.Height / 2 : at.Y));
        }
        //----------------------------------------------------------//


        private FormattedText Format(string text, double size, Brush brush, bool bold = false) => new(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            bold ? new Typeface(LabelFace.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal) : LabelFace,
            size,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        //----------------------------------------------------------//


        private static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}

//------------------------------------------- End Of File -------------------------------------------//
