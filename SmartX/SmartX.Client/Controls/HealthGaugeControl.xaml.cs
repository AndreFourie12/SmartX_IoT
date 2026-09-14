using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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
        /// Dependency property backing Score, so the gauge can be set or bound from XAML.
        /// Redraws the gauge whenever the value changes.
        /// </summary>
        public static readonly DependencyProperty ScoreProperty = DependencyProperty.Register(
            nameof(Score), typeof(int), typeof(HealthGaugeControl),
            new PropertyMetadata(100, OnScoreChanged));
        //----------------------------------------------------------//


        /// <summary>
        /// The health percentage the gauge is currently showing, from 0 to 100.
        /// Setting it goes through the dependency property, which triggers a redraw.
        /// </summary>
        public int Score
        {
            get => (int)GetValue(ScoreProperty);
            set => SetValue(ScoreProperty, value);
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Builds the control and draws the gauge once its visual tree has loaded.
        /// Drawing on Loaded avoids touching arcs that do not exist yet.
        /// </summary>
        public HealthGaugeControl()
        {
            InitializeComponent();
            Loaded += (_, _) => Redraw();
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Called by WPF whenever the Score property changes.
        /// Casts the sender back to the gauge and asks it to redraw.
        /// </summary>
        private static void OnScoreChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
            ((HealthGaugeControl)d).Redraw();
        //----------------------------------------------------------//


        /// <summary>
        /// Rebuilds both the grey track arc and the coloured score arc from the current Score.
        /// Also updates the number in the middle and recolours everything to match the score band.
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



        //// <summary>
        /// Builds the path geometry for an arc sweeping the given number of degrees from the gauge's start angle.
        /// The large arc flag is set once the sweep passes 180 degrees so the curve bends the right way.
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
        /// Converts an angle in degrees into the point sitting on the gauge's circle at that angle.
        /// Used to work out where an arc should start and end.
        /// </summary>
        private static Point PointOnCircle(double angleDegrees)
        {
            double radians = angleDegrees * Math.PI / 180.0;
            return new Point(CenterX + Radius * Math.Cos(radians), CenterY + Radius * Math.Sin(radians));
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Picks the colour band for a score, green from 80, amber from 50, and red below that.
        /// Used for both the arc and the number in the middle.
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
