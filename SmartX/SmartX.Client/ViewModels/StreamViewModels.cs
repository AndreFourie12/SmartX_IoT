using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SmartX.Shared.Contracts;
using SmartX.Shared.Models;
using SmartX.Shared.Streaming;

namespace SmartX.Client.ViewModels
{
    /// <summary>
    /// Brushes from Theme.xaml, so view models can pick a colour without the XAML needing converters.
    /// </summary>
    public static class Palette
    {
        public static Brush Get(string key) => (Brush)Application.Current.FindResource(key);

        public static Brush ForAlertState(DeviceAlertState state) => state switch
        {
            DeviceAlertState.Critical => Get("StatusRed"),
            DeviceAlertState.Warning => Get("StatusAmberDot"),
            DeviceAlertState.Offline => Get("TextSecondary"),
            DeviceAlertState.AwaitingData => Get("TextMuted"),
            _ => Get("StatusGreenDot")
        };

        // The pale fill behind a status tag, matching ForAlertState.
        public static Brush SoftForAlertState(DeviceAlertState state) => state switch
        {
            DeviceAlertState.Critical => Get("StatusRedSoft"),
            DeviceAlertState.Warning => Get("StatusAmberSoft"),
            DeviceAlertState.Normal => Get("StatusGreenSoft"),
            _ => Get("NeutralSoft")
        };

        public static Brush ForSeverity(AlertSeverity severity) => severity switch
        {
            AlertSeverity.Critical => Get("StatusRed"),
            AlertSeverity.Warning => Get("StatusAmberDot"),
            _ => Get("AccentBlue")
        };
    }
    //----------------------------------------------------------//


    /// <summary>
    /// One device in the Command Stream fleet list.
    /// </summary>
    public sealed class DeviceRowViewModel : ObservableObject
    {
        private string _displayName = string.Empty;
        private string _valueText = "—";
        private string _statusLabel = string.Empty;
        private Brush _statusBrush = Brushes.Gray;
        private Brush _statusSoftBrush = Brushes.WhiteSmoke;
        private string? _alertReason;
        private string _lastSeenText = string.Empty;
        private bool _highlighted;
        private string? _highlightReason;
        private string? _pendingText;
        private DeviceStateDto _dto;

        public DeviceRowViewModel(DeviceStateDto dto, DateTimeOffset serverNow)
        {
            _dto = dto;
            Id = dto.Id;
            Update(dto, serverNow);
        }

        public string Id { get; }
        public DeviceStateDto Dto => _dto;
        public SensorCategory Category => _dto.Category;
        public bool IsActuator => _dto.IsActuator;
        public string Subtitle => $"{Id} · {_dto.Category.DisplayName()}";
        public string Location => _dto.Location;

        public string DisplayName { get => _displayName; private set => Set(ref _displayName, value); }
        public string ValueText { get => _valueText; private set => Set(ref _valueText, value); }
        public string StatusLabel { get => _statusLabel; private set => Set(ref _statusLabel, value); }
        public Brush StatusBrush { get => _statusBrush; private set => Set(ref _statusBrush, value); }
        public Brush StatusSoftBrush { get => _statusSoftBrush; private set => Set(ref _statusSoftBrush, value); }
        public string? AlertReason { get => _alertReason; private set => Set(ref _alertReason, value); }
        public string LastSeenText { get => _lastSeenText; private set => Set(ref _lastSeenText, value); }
        public bool Highlighted { get => _highlighted; private set => Set(ref _highlighted, value); }
        public string? HighlightReason { get => _highlightReason; private set => Set(ref _highlightReason, value); }
        public string? PendingText { get => _pendingText; private set => Set(ref _pendingText, value); }


        public void Update(DeviceStateDto dto, DateTimeOffset serverNow)
        {
            _dto = dto;

            DisplayName = dto.DisplayName;
            ValueText = dto.LastValue is null ? "—" : $"{dto.LastValue}{(dto.Unit.Length > 0 ? " " + dto.Unit : string.Empty)}";
            StatusBrush = Palette.ForAlertState(dto.AlertState);
            StatusSoftBrush = Palette.SoftForAlertState(dto.AlertState);
            StatusLabel = dto.AlertState switch
            {
                DeviceAlertState.AwaitingData => "WAITING",
                DeviceAlertState.Normal => "OK",
                _ => dto.AlertState.ToString().ToUpperInvariant()
            };
            AlertReason = dto.AlertReason;
            Highlighted = dto.Highlighted;
            HighlightReason = dto.HighlightReason;

            PendingText = dto.CommandPending && dto.DesiredOn is { } desired
                ? $"→ {dto.Category.StateLabel(desired)} pending"
                : null;

            LastSeenText = dto.LastSeenUtc is { } seen ? Ago(serverNow - seen) : "never";
        }
        //----------------------------------------------------------//


        public static string Ago(TimeSpan elapsed) => elapsed.TotalSeconds switch
        {
            < 2 => "just now",
            < 60 => $"{elapsed.TotalSeconds:F0}s ago",
            < 3600 => $"{elapsed.TotalMinutes:F0}m ago",
            _ => $"{elapsed.TotalHours:F0}h ago"
        };
    }
    //----------------------------------------------------------//


    /// <summary>
    /// One "Suggested Actions" card.
    /// </summary>
    public sealed class InsightViewModel : ObservableObject
    {
        private string _title = string.Empty;
        private string _detail = string.Empty;
        private string _evidence = string.Empty;
        private double _confidence;
        private Brush _severityBrush = Brushes.Gray;
        private bool _critical;
        private SuggestedCommandDto? _command;

        public InsightViewModel(InsightDto dto)
        {
            Id = dto.Id;
            Update(dto);
        }

        public string Id { get; }
        public InsightKind Kind { get; private set; }
        public string? DeviceId { get; private set; }

        public string Title { get => _title; private set => Set(ref _title, value); }
        public string Detail { get => _detail; private set => Set(ref _detail, value); }
        public string Evidence { get => _evidence; private set => Set(ref _evidence, value); }
        public Brush SeverityBrush { get => _severityBrush; private set => Set(ref _severityBrush, value); }

        // Critical suggestions are tinted red; everything else uses the amber of automated insights.
        public bool IsCritical
        {
            get => _critical;
            private set
            {
                if (Set(ref _critical, value))
                {
                    Raise(nameof(CardBackground));
                    Raise(nameof(CardBorder));
                    Raise(nameof(AccentText));
                    Raise(nameof(AccentBar));
                    Raise(nameof(AccentTrack));
                }
            }
        }

        public Brush CardBackground => Palette.Get(IsCritical ? "StatusRedSoft" : "InsightCardBackground");
        public Brush CardBorder => Palette.Get(IsCritical ? "StatusRedBorder" : "InsightBorder");
        public Brush AccentText => Palette.Get(IsCritical ? "StatusRedText" : "InsightText");
        public Brush AccentBar => Palette.Get(IsCritical ? "StatusRed" : "InsightBar");
        public Brush AccentTrack => Palette.Get(IsCritical ? "StatusRedBorder" : "InsightBarTrack");

        public double Confidence
        {
            get => _confidence;
            private set
            {
                if (Set(ref _confidence, value))
                {
                    Raise(nameof(ConfidenceText));
                    Raise(nameof(ConfidenceBarWidth));
                }
            }
        }

        public string ConfidenceText => $"{Confidence:P0}";
        public double ConfidenceBarWidth => Math.Clamp(Confidence, 0, 1) * 68;

        public SuggestedCommandDto? Command
        {
            get => _command;
            private set
            {
                if (Set(ref _command, value))
                    Raise(nameof(HasCommand));
            }
        }

        public bool HasCommand => Command is not null;

        public string KindLabel => Kind switch
        {
            InsightKind.LearnedResponse => "LEARNED",
            InsightKind.NextActionPrediction => "SEQUENCE",
            InsightKind.ZoneRule => "ZONE RULE",
            _ => "FORECAST"
        };


        public void Update(InsightDto dto)
        {
            Kind = dto.Kind;
            DeviceId = dto.DeviceId;
            Title = dto.Title;
            Detail = dto.Detail;
            Evidence = dto.Evidence;
            Confidence = dto.Confidence;
            SeverityBrush = Palette.ForSeverity(dto.Severity);
            IsCritical = dto.Severity == AlertSeverity.Critical;
            Command = dto.Command;
            Raise(nameof(KindLabel));
        }
        //----------------------------------------------------------//

    }
    //----------------------------------------------------------//


    /// <summary>
    /// One entry in a filter dropdown. Value is null for the "All ..." entry.
    /// </summary>
    public sealed record FilterChoice<T>(string Label, T? Value) where T : struct;
    //----------------------------------------------------------//


    /// <summary>
    /// One line in the live event stream.
    /// </summary>
    public sealed record EventRow(
        long Id,
        string TimeText,
        string Title,
        string Detail,
        Brush Brush,
        Brush BadgeBrush,
        string Glyph,
        bool ViaPriorityLane,
        string? PriorityText,
        string? DeviceId)
    {
        public static EventRow From(StreamEventDto dto)
        {
            // The glyph colour and the pale circle behind it.
            var (glyph, badge) = dto.Kind switch
            {
                StreamEventKind.AlertCleared or StreamEventKind.DeviceReconnected or StreamEventKind.CommandAcknowledged =>
                    ("StatusGreen", "StatusGreenSoft"),
                StreamEventKind.CommandIssued or StreamEventKind.CommandUndone =>
                    ("AccentBlue", "AccentBlueSoft"),
                StreamEventKind.DeviceOffline => ("TextSecondary", "NeutralSoft"),
                _ => dto.Severity switch
                {
                    AlertSeverity.Critical => ("StatusRed", "StatusRedSoft"),
                    AlertSeverity.Warning => ("StatusAmber", "StatusAmberSoft"),
                    _ => ("AccentBlue", "AccentBlueSoft")
                }
            };

            return Create(dto, Palette.Get(glyph), Palette.Get(badge));
        }
        //----------------------------------------------------------//


        private static EventRow Create(StreamEventDto dto, Brush brush, Brush badge) => new(
            dto.Id,
            dto.TimestampUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            dto.Title,
            dto.Detail,
            brush,
            badge,
            dto.Kind switch
            {
                StreamEventKind.AlertRaised or StreamEventKind.AlertEscalated => "⚠",
                StreamEventKind.AlertEased => "▼",
                StreamEventKind.AlertCleared => "✓",
                StreamEventKind.DeviceOffline => "○",
                StreamEventKind.DeviceReconnected => "●",
                StreamEventKind.CommandIssued => "▶",
                StreamEventKind.CommandUndone => "↶",
                StreamEventKind.CommandAcknowledged => "✓",
                StreamEventKind.LoadTest => "≋",
                _ => "•"
            },
            dto.ViaPriorityLane,
            dto.ViaPriorityLane
                ? $"{FormatLatency(dto.LatencyMs)} · jumped {dto.Bypassed ?? 0} queued"
                : null,
            dto.DeviceId);

        private static string FormatLatency(double? ms) => ms switch
        {
            null => "—",
            < 1 => $"{ms:F2} ms",
            < 1000 => $"{ms:F0} ms",
            _ => $"{ms / 1000:F1} s"
        };
    }
    //----------------------------------------------------------//


    /// <summary>
    /// A command on the undo stack.
    /// </summary>
    public sealed record CommandRow(string Id, string Label, string Subtitle, bool IsNext)
    {
        public static CommandRow From(CommandRecordDto dto, bool isNext) => new(
            dto.Id,
            dto.Label,
            $"{dto.Id} · {dto.Changes.Count} device{(dto.Changes.Count == 1 ? "" : "s")} · {dto.IssuedAtUtc.ToLocalTime():HH:mm:ss}"
                + (dto.Origin == CommandOrigin.Suggestion ? " · from suggestion" : string.Empty),
            isNext);
    }
    //----------------------------------------------------------//


    /// <summary>
    /// A learned condition -> response pattern.
    /// </summary>
    public sealed record PatternRow(string Condition, string Response, string Support, double BarWidth)
    {
        public static PatternRow From(LearnedPatternDto dto) => new(
            dto.Condition,
            dto.Response,
            $"{dto.Count}/{dto.Episodes} · {dto.Confidence:P0}" + (dto.LiveCount > 0 ? $" · {dto.LiveCount} live" : string.Empty),
            Math.Clamp(dto.Confidence, 0, 1) * 60);
    }
    //----------------------------------------------------------//
}

//------------------------------------------- End Of File -------------------------------------------//
