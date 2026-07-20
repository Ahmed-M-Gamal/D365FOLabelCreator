using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using D365LabelCreator.ViewModels;

namespace D365LabelCreator.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        // When a label group is opened (or the next one is auto-selected after Validate),
        // select the first metadata item automatically.
        _vm.ItemsReloaded += () =>
        {
            if (ItemsList.Items.Count > 0)
                ItemsList.SelectedIndex = 0;
        };

        _vm.PreviewUpdated += RebuildPreview;
    }

    // git-diff style colours: removed = red, added = green.
    private static readonly Brush RemovedBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x75));
    private static readonly Brush RemovedBg = new SolidColorBrush(Color.FromArgb(0x40, 0xE0, 0x6C, 0x75));
    private static readonly Brush AddedBrush = new SolidColorBrush(Color.FromRgb(0x98, 0xC3, 0x79));
    private static readonly Brush AddedBg = new SolidColorBrush(Color.FromArgb(0x40, 0x98, 0xC3, 0x79));

    private const double PreviewFontSize = 12;

    private void RebuildPreview()
    {
        if (!_vm.HasPreview)
        {
            PreviewBeforeBox.Document = new FlowDocument();
            PreviewAfterBox.Document = new FlowDocument();
            return;
        }

        string beforeText = _vm.PreviewPrefix + _vm.PreviewOldText + _vm.PreviewSuffix;
        string afterText = _vm.PreviewPrefix + _vm.PreviewNewText + _vm.PreviewSuffix;

        // Fit the page to the widest line of either pane (same width for both, so the synced
        // horizontal scrolling lines up). A small margin guards against wrapping the longest line.
        double width = Math.Max(MeasureLongestLine(beforeText), MeasureLongestLine(afterText)) * 1.02 + 24;

        PreviewBeforeBox.Document = BuildPreviewDocument(
            _vm.PreviewPrefix, _vm.PreviewOldText, _vm.PreviewSuffix, RemovedBrush, RemovedBg, width, out var beforeRun);
        PreviewAfterBox.Document = BuildPreviewDocument(
            _vm.PreviewPrefix, _vm.PreviewNewText, _vm.PreviewSuffix, AddedBrush, AddedBg, width, out var afterRun);

        // Centre both panes on the change once the documents have been laid out.
        Dispatcher.InvokeAsync(() =>
        {
            CenterOnChange(PreviewBeforeBox, beforeRun);
            CenterOnChange(PreviewAfterBox, afterRun);
        }, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Builds the diff document. PageWidth is set to the content's own width so the horizontal
    /// scroll range matches the text instead of an arbitrary page.
    /// </summary>
    private static FlowDocument BuildPreviewDocument(
        string prefix, string changed, string suffix,
        Brush changedFg, Brush changedBg, double pageWidth, out Run changedRun)
    {
        var doc = new FlowDocument
        {
            PageWidth = pageWidth,
            PagePadding = new Thickness(4),
            FontFamily = new FontFamily("Consolas"),
            FontSize = PreviewFontSize,
            Foreground = (Brush)Application.Current.Resources["FgBrush"],
        };

        changedRun = new Run(changed) { Foreground = changedFg, Background = changedBg };
        var paragraph = new Paragraph { Margin = new Thickness(0) };
        paragraph.Inlines.Add(new Run(prefix));
        paragraph.Inlines.Add(changedRun);
        paragraph.Inlines.Add(new Run(suffix));
        doc.Blocks.Add(paragraph);
        return doc;
    }

    /// <summary>Width of the widest line, used to size the flow document to its content.</summary>
    private double MeasureLongestLine(string text)
    {
        var typeface = new Typeface(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double charWidth = Measure("0", typeface, pixelsPerDip); // Consolas is monospaced
        double max = 0;

        foreach (var raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0)
                continue;

            // Metadata XML is tab-indented, and FormattedText and the flow document can size tab
            // stops differently. Also size the line by columns (tabs advancing to the widest common
            // stop) and take whichever is larger, so we never under-measure and cause wrapping.
            int columns = 0;
            foreach (char c in line)
                columns = c == '\t' ? ((columns / 8) + 1) * 8 : columns + 1;

            double width = Math.Max(Measure(line, typeface, pixelsPerDip), columns * charWidth);
            if (width > max)
                max = width;
        }
        return max;

        static double Measure(string s, Typeface tf, double dpi) =>
            new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                tf, PreviewFontSize, Brushes.Black, dpi).WidthIncludingTrailingWhitespace;
    }

    /// <summary>Scrolls the pane so the changed span sits in the middle of the viewport.</summary>
    private static void CenterOnChange(RichTextBox box, Run changedRun)
    {
        var rect = changedRun.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        if (rect.IsEmpty)
            return;

        double target = box.VerticalOffset + rect.Top + (rect.Height / 2) - (box.ViewportHeight / 2);
        box.ScrollToVerticalOffset(Math.Max(0, target));

        // Only scroll sideways when the change would otherwise be off-screen.
        double absoluteLeft = box.HorizontalOffset + rect.Left;
        double horizontal = absoluteLeft > box.ViewportWidth * 0.9
            ? Math.Max(0, absoluteLeft - (box.ViewportWidth / 2))
            : 0;
        box.ScrollToHorizontalOffset(horizontal);
    }

    /// <summary>Keeps the two preview panes scrolled to the same place.</summary>
    private void PreviewScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not RichTextBox source)
            return;
        var other = ReferenceEquals(source, PreviewBeforeBox) ? PreviewAfterBox : PreviewBeforeBox;

        // Comparing before assigning stops the two panes from bouncing off each other.
        if (Math.Abs(other.VerticalOffset - source.VerticalOffset) > 0.5)
            other.ScrollToVerticalOffset(source.VerticalOffset);
        if (Math.Abs(other.HorizontalOffset - source.HorizontalOffset) > 0.5)
            other.ScrollToHorizontalOffset(source.HorizontalOffset);
    }

    private void ItemsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = ItemsList.SelectedItems.Cast<OccurrenceViewModel>().ToList();

        // The focused item (drives the preview) is the last one clicked, else the primary selection.
        OccurrenceViewModel? focused =
            e.AddedItems.Cast<OccurrenceViewModel>().LastOrDefault()
            ?? ItemsList.SelectedItem as OccurrenceViewModel;

        _vm.UpdateSelection(selected, focused);
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) => ItemsList.SelectAll();

    private void UnselectAll_Click(object sender, RoutedEventArgs e) => ItemsList.UnselectAll();
}
