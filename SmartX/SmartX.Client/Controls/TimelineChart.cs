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
        private static readonly Brush WarningFill = Frozen(Color.FromArgb(0x22, 0xF5, 0x9E, 0x0B));
        private static readonly Brush CriticalFill = Frozen(Color.FromArgb(0x22, 0xDC, 0x26, 0x26));
        private static readonly Brush WarningLine = Frozen(Color.FromRgb(0xF5, 0x9E, 0x0B));
        private static readonly Brush CriticalLine = Frozen(Color.FromRgb(0xDC, 0x26, 0x26));
        private static readonly Typeface LabelFace = new("Cascadia Mono, Consolas, Courier New");

        private const double PlotLeft = 50;
        private const double PlotRight = 12;
        private const double PlotTop = 10;
        private const double PlotBottom = 24;


        protected override void OnRender(DrawingContext drawing)
        {
            double width = ActualWidth, height = ActualHeight;
            drawing.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));

            var timeline = Timeline;
            var plot = new Rect(PlotLeft, PlotTop, Math.Max(1, width - PlotLeft - PlotRight), Math.Max(1, height - PlotTop - PlotBottom));

            if (timeline is null || timeline.Points.Count == 0)
            {
                DrawText(drawing, timeline is null ? "Select a device to see its timeline." : "No readings in this window yet.",
                    new Point(width / 2, height / 2), 12.5, AxisText, centred: true, middle: true);
                return;
            }

            bool onOff = timeline.Category.IsActuator();
            var (min, max) = onOff ? (-0.15, 1.15) : ValueRange(timeline);

            double fromTicks = timeline.FromUtc.UtcTicks, toTicks = timeline.ToUtc.UtcTicks;
            double X(DateTimeOffset t) => plot.Left + (t.UtcTicks - fromTicks) / Math.Max(1, toTicks - fromTicks) * plot.Width;
            double Y(double v) => plot.Bottom - (v - min) / (max - min) * plot.Height;

            drawing.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));

            if (!onOff && timeline.Band is { } band)
                DrawBands(drawing, band, plot, min, max, Y);

            DrawGrid(drawing, plot, min, max, onOff, Y);
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


        private void DrawBands(DrawingContext drawing, Shared.Streaming.ThresholdBand band, Rect plot, double min, double max, Func<double, double> y)
        {
            void Shade(double from, double to, Brush fill)
            {
                double top = Math.Clamp(y(Math.Max(from, to)), plot.Top, plot.Bottom);
                double bottom = Math.Clamp(y(Math.Min(from, to)), plot.Top, plot.Bottom);
                if (bottom - top > 0.5)
                    drawing.DrawRectangle(fill, null, new Rect(plot.Left, top, plot.Width, bottom - top));
            }

            void Line(double? value, Brush brush)
            {
                if (value is not { } v || v < min || v > max)
                    return;

                var pen = new Pen(brush, 1) { DashStyle = DashStyles.Dash };
                pen.Freeze();
                drawing.DrawLine(pen, new Point(plot.Left, y(v)), new Point(plot.Right, y(v)));
                DrawText(drawing, v.ToString("0.#", CultureInfo.InvariantCulture), new Point(plot.Right - 2, y(v) - 15), 10, brush, rightAligned: true);
            }

            if (band.WarningLow is { } warnLow)
                Shade(band.CriticalLow ?? min, warnLow, WarningFill);
            if (band.CriticalLow is { } critLow)
                Shade(min, critLow, CriticalFill);
            if (band.WarningHigh is { } warnHigh)
                Shade(warnHigh, band.CriticalHigh ?? max, WarningFill);
            if (band.CriticalHigh is { } critHigh)
                Shade(critHigh, max, CriticalFill);

            Line(band.WarningLow, WarningLine);
            Line(band.CriticalLow, CriticalLine);
            Line(band.WarningHigh, WarningLine);
            Line(band.CriticalHigh, CriticalLine);
        }
        //----------------------------------------------------------//


        private void DrawGrid(DrawingContext drawing, Rect plot, double min, double max, bool onOff, Func<double, double> y)
        {
            var gridPen = new Pen(GridBrush, 1);
            gridPen.Freeze();

            if (onOff)
            {
                foreach (var (value, label) in new[] { (0.0, "OFF"), (1.0, "ON") })
                {
                    drawing.DrawLine(gridPen, new Point(plot.Left, y(value)), new Point(plot.Right, y(value)));
                    DrawText(drawing, label, new Point(plot.Left - 6, y(value) - 7), 10.5, AxisText, rightAligned: true);
                }

                return;
            }

            for (int i = 0; i <= 4; i++)
            {
                double value = min + (max - min) * i / 4;
                drawing.DrawLine(gridPen, new Point(plot.Left, y(value)), new Point(plot.Right, y(value)));
                DrawText(drawing, value.ToString(Math.Abs(max - min) < 10 ? "0.0" : "0", CultureInfo.InvariantCulture),
                    new Point(plot.Left - 6, y(value) - 7), 10.5, AxisText, rightAligned: true);
            }
        }
        //----------------------------------------------------------//


        private void DrawTimeAxis(DrawingContext drawing, TimelineDto timeline, Rect plot)
        {
            for (int i = 0; i <= 4; i++)
            {
                var at = timeline.FromUtc + (timeline.ToUtc - timeline.FromUtc) * (i / 4.0);
                double x = plot.Left + plot.Width * i / 4;
                DrawText(drawing, at.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                    new Point(x, plot.Bottom + 5), 10, AxisText, centred: i is > 0 and < 4, rightAligned: i == 4);
            }
        }
        //----------------------------------------------------------//


        private void DrawSeries(DrawingContext drawing, TimelineDto timeline, Rect plot, bool onOff, Func<DateTimeOffset, double> x, Func<double, double> y)
        {
            var points = timeline.Points;

            var line = new StreamGeometry();
            using (var context = line.Open())
            {
                context.BeginFigure(new Point(x(points[0].TimestampUtc), y(points[0].Value)), false, false);

                for (int i = 1; i < points.Count; i++)
                {
                    if (onOff)
                        context.LineTo(new Point(x(points[i].TimestampUtc), y(points[i - 1].Value)), true, false);

                    context.LineTo(new Point(x(points[i].TimestampUtc), y(points[i].Value)), true, false);
                }
            }

            line.Freeze();

            // Soft fill under the line.
            var area = new StreamGeometry();
            using (var context = area.Open())
            {
                context.BeginFigure(new Point(x(points[0].TimestampUtc), plot.Bottom), true, true);
                foreach (var point in points)
                    context.LineTo(new Point(x(point.TimestampUtc), y(point.Value)), false, false);
                context.LineTo(new Point(x(points[^1].TimestampUtc), plot.Bottom), false, false);
            }

            area.Freeze();

            var fill = new LinearGradientBrush(Color.FromArgb(0x30, 0x25, 0x63, 0xEB), Color.FromArgb(0x00, 0x25, 0x63, 0xEB), 90);
            fill.Freeze();

            if (!onOff)
                drawing.DrawGeometry(fill, null, area);

            var pen = new Pen(LineBrush, 1.8) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            drawing.DrawGeometry(null, pen, line);

            var last = points[^1];
            var lastPoint = new Point(x(last.TimestampUtc), y(last.Value));

            var ringPen = new Pen(LineBrush, 2);
            ringPen.Freeze();
            drawing.DrawEllipse(Brushes.White, ringPen, lastPoint, 5, 5);

            DrawValueTag(drawing, $"{last.Display} {timeline.Unit}".Trim(), lastPoint, plot);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// The latest value as a filled tag just left of the last point, kept inside the plot.
        /// </summary>
        private void DrawValueTag(DrawingContext drawing, string text, Point anchor, Rect plot)
        {
            var formatted = Format(text, 11, Brushes.White, bold: true);

            double width = formatted.Width + 14, height = formatted.Height + 6;
            double left = Math.Max(plot.Left, anchor.X - 10 - width);
            // Ensure the clamp upper bound is not less than the lower bound to avoid ArgumentException
            double maxTop = Math.Max(plot.Top, plot.Bottom - height);
            double top = Math.Clamp(anchor.Y - height / 2, plot.Top, maxTop);

            drawing.DrawRoundedRectangle(LineBrush, null, new Rect(left, top, width, height), 4, 4);
            drawing.DrawText(formatted, new Point(left + 7, top + 3));
        }
        
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
