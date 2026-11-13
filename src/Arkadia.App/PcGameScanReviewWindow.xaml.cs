using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using Arkadia.Core.Models;
using Arkadia.Infrastructure.Providers;

namespace Arkadia.App;

public partial class PcGameScanReviewWindow : Window
{
    public PcGameScanReviewWindow(PcGameScanResult scanResult)
    {
        InitializeComponent();
        Candidates = new ObservableCollection<PcGameScanReviewItem>(
            scanResult.Candidates.Select(candidate => new PcGameScanReviewItem(candidate)));
        ScanSummary = string.Format(
            CultureInfo.CurrentCulture,
            Localization.LocalizationManager.Instance["Games.ScanReview.Summary"],
            scanResult.Candidates.Count,
            scanResult.SkippedDirectories,
            scanResult.WasTruncated
                ? Localization.LocalizationManager.Instance["Games.ScanReview.Truncated"]
                : string.Empty);
        DataContext = this;
    }

    public ObservableCollection<PcGameScanReviewItem> Candidates { get; }

    public string ScanSummary { get; }

    public IReadOnlyList<GameScanCandidate> SelectedCandidates =>
        Candidates.Where(candidate => candidate.IsSelected)
            .Select(candidate => candidate.Candidate)
            .ToArray();

    private void AddSelected_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCandidates.Count == 0)
        {
            return;
        }

        DialogResult = true;
    }
}

public sealed class PcGameScanReviewItem(GameScanCandidate candidate)
{
    public GameScanCandidate Candidate { get; } = candidate;

    public string Title => Candidate.Title;

    public string ExecutablePath => Candidate.ExecutablePath;

    public bool IsSelected { get; set; }
}
