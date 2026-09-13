using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SmartX.Client.Controls
{
    /// <summary>
    /// Interaction logic for HealthGaugeControl.xaml
    /// </summary>
    public partial class HealthGaugeControl : UserControl
    {
        private const double StartAngleDegrees = 135;
        private const double SweepRangeDegrees = 270;
        private const double Radius = 31;
        private const double CenterX = 37;
        private const double CenterY = 37;


        /// <summary>
        /// 
        /// </summary>
        public static readonly DependencyProperty ScoreProperty = DependencyProperty.Register(
            nameof(Score), typeof(int), typeof(HealthGaugeControl),
            new PropertyMetadata(100, OnScoreChanged));
        //----------------------------------------------------------//


        /// <summary>
        /// 
        /// </summary>
        public int Score
        {
            get => (int)GetValue(ScoreProperty);
            set => SetValue(ScoreProperty, value);
        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        public HealthGaugeControl()
        {
            InitializeComponent();
            Loaded += (_, _) => Redraw();
        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        private static void OnScoreChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
            ((HealthGaugeControl)d).Redraw();
        //----------------------------------------------------------//


        /// <summary>
        /// 
        /// </summary>
        private void Redraw()
        {
            if (TrackArc is null || ScoreArc is null)
                return;

            TrackArc.Data = BuildArcGeometry(SweepRangeDegrees);

            int clamped = Math.Clamp(Score, 0, 100);
            double sweep = SweepRangeDegrees * (clamped / 100.0);
            ScoreArc.Data = BuildArcGeometry(Math.Max(sweep, 0.001));
            ScoreArc.Stroke = new SolidColorBrush(ColorForScore(clamped));

            ScoreText.Text = clamped.ToString();
            ScoreText.Foreground = new SolidColorBrush(ColorForScore(clamped));
        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        private static Geometry BuildArcGeometry(double sweepDegrees)
        {
            Point start = PointOnCircle(StartAngleDegrees);
            Point end = PointOnCircle(StartAngleDegrees + sweepDegrees);
            bool isLargeArc = sweepDegrees > 180;

            var figure = new PathFigure { StartPoint = start, IsClosed = false };
            figure.Segments.Add(new ArcSegment(
                end,
                new Size(Radius, Radius),
                0,
                isLargeArc,
                SweepDirection.Clockwise,
                isStroked: true));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return geometry;
        }
        //----------------------------------------------------------//


        /// <summary>
        /// 
        /// </summary>
        private static Point PointOnCircle(double angleDegrees)
        {
            double radians = angleDegrees * Math.PI / 180.0;
            return new Point(CenterX + Radius * Math.Cos(radians), CenterY + Radius * Math.Sin(radians));
        }
        //----------------------------------------------------------//


        /// <summary>
        /// 
        /// </summary>
        private static Color ColorForScore(int score) => score switch
        {
            >= 80 => Color.FromRgb(0x35, 0xC7, 0x5E),  // green
            >= 50 => Color.FromRgb(0xF2, 0xA6, 0x1E),  // amber
            _ => Color.FromRgb(0xE5, 0x4B, 0x4B)       // red
        };
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
